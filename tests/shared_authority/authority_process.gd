extends SceneTree
## Separate-process, loopback-only authority fixture. Not a production server.

const Model = preload("res://authority_model.gd")
const HOST := "127.0.0.1"
const CERT_NAME := "enfractal-test.invalid"
const MAX_PACKET_BYTES := 1000
const MAX_RUN_MS := 20000

var _model: RefCounted
var _host: ENetConnection
var _peers: Dictionary = {}
var _connected_peers: Dictionary = {}
var _joined_peers: Dictionary = {}
var _accepted_intents: Dictionary = {}
var _done: Dictionary = {}
var _rejections: Dictionary = {}
var _run_dir := ""
var _run_id := ""
var _error := ""
var _tick_sent := false
var _nonadmitted_peer_present_at_tick := false
var _connected_at_tick := 0
var _snapshot_recipients := 0
var _rejected_state_changed := false
var _started_ms := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_started_ms = Time.get_ticks_msec()
	_run_dir = _argument("--run-dir")
	_run_id = _argument("--run-id")
	if _run_dir.is_empty() or _run_id.is_empty():
		_error = "missing_run_arguments"
		_finish()
		return
	_model = Model.new()
	var crypto := Crypto.new()
	var key := crypto.generate_rsa(2048)
	var now := int(Time.get_unix_time_from_system())
	var certificate := crypto.generate_self_signed_certificate(key, "CN=%s,O=Enfractal Local Test,C=US" % CERT_NAME, _x509_time(now - 86400), _x509_time(now + 86400))
	if key == null or certificate == null:
		_error = "ephemeral_certificate_failed"
		_finish()
		return
	_host = ENetConnection.new()
	if _host.create_host_bound(HOST, 0, 4, 2) != OK:
		_error = "loopback_bind_failed"
		_finish()
		return
	if _host.dtls_server_setup(TLSOptions.server(key, certificate)) != OK:
		_error = "dtls_setup_failed"
		_finish()
		return
	var certificate_path: String = _run_dir.path_join("ephemeral-public.crt")
	if certificate.save(certificate_path) != OK:
		_error = "public_certificate_write_failed"
		_finish()
		return
	var boot := {
		"run_id": _run_id, "host": HOST, "port": _host.get_local_port(),
		"hostname": CERT_NAME, "certificate_path": certificate_path,
		"tickets": {"alice": _model.issue_ticket("alice"), "bob": _model.issue_ticket("bob")},
	}
	if not _write_json(_run_dir.path_join("admission.json"), boot):
		_error = "admission_write_failed"
		_finish()
		return
	while Time.get_ticks_msec() - _started_ms < MAX_RUN_MS and _done.size() < 2:
		_pump()
		if not _tick_sent and _accepted_intents.has("alice") and _accepted_intents.has("bob"):
			_connected_at_tick = _connected_peers.size()
			_nonadmitted_peer_present_at_tick = _connected_at_tick == 3 and _joined_peers.size() == 2
			_model.advance_tick()
			_tick_sent = true
			for peer_key in _joined_peers:
				var snapshot: Dictionary = _model.snapshot(peer_key)
				if not snapshot.is_empty():
					_snapshot_recipients += 1
					_send(_peers[peer_key], snapshot)
			_host.flush()
	if _done.size() < 2:
		_error = "client_completion_timeout"
	else:
		# Let ENet deliver the final reliable acknowledgements before teardown.
		_host.flush()
		var drain_started := Time.get_ticks_msec()
		while Time.get_ticks_msec() - drain_started < 300:
			_pump()
	_finish()


func _pump() -> void:
	var event: Array = _host.service(5)
	if event[0] == ENetConnection.EVENT_NONE:
		return
	var peer: ENetPacketPeer = event[1]
	var key: int = peer.get_instance_id()
	if event[0] == ENetConnection.EVENT_CONNECT:
		_peers[key] = peer
		_connected_peers[key] = true
		return
	if event[0] == ENetConnection.EVENT_DISCONNECT:
		_connected_peers.erase(key)
		return
	if event[0] != ENetConnection.EVENT_RECEIVE:
		return
	_peers[key] = peer
	var raw: PackedByteArray = peer.get_packet()
	var response: Dictionary
	if raw.size() > MAX_PACKET_BYTES:
		response = {"ok": false, "code": "message_too_large"}
	else:
		var decoded: Variant = JSON.parse_string(raw.get_string_from_utf8())
		if not decoded is Dictionary:
			response = {"ok": false, "code": "invalid_message"}
		elif decoded.get("type") == "probe_done":
			response = _probe_done(key, decoded)
		else:
			var before: String = _model.state_fingerprint()
			response = _model.handle(key, decoded)
			if not response.get("ok", false):
				_rejections[response.get("code", "unknown")] = int(_rejections.get(response.get("code", "unknown"), 0)) + 1
				if before != _model.state_fingerprint():
					_rejected_state_changed = true
			if response.get("code") == "joined":
				_joined_peers[key] = response["avatar"]
			elif response.get("code") == "accepted_intent":
				_accepted_intents[response["avatar"]] = true
	_send(peer, response)


func _probe_done(key: int, packet: Dictionary) -> Dictionary:
	if not _joined_peers.has(key):
		return {"ok": false, "code": "not_admitted"}
	if packet.size() != 3 or not packet.has("run_id") or not packet.has("session_id"):
		return {"ok": false, "code": "invalid_message"}
	if packet["run_id"] != _run_id:
		return {"ok": false, "code": "wrong_run"}
	var avatar: String = _joined_peers[key]
	if not packet["session_id"] is String or not _model.is_bound_session(key, packet["session_id"]):
		return {"ok": false, "code": "session_connection_mismatch"}
	# The final marker is a harness control. It does not advance a world tick.
	_done[avatar] = true
	return {"ok": true, "code": "probe_done"}


func _send(peer: ENetPacketPeer, packet: Dictionary) -> void:
	peer.send(0, JSON.stringify(packet).to_utf8_buffer(), ENetPacketPeer.FLAG_RELIABLE)


func _finish() -> void:
	var positions: Dictionary = _model.shared_positions() if _model != null else {}
	var expected_alice: float = (Model.SPEED_MPS + Model.WIND_MPS.x) / Model.TICK_HZ
	var expected_bob: float = -Model.SPEED_MPS / Model.TICK_HZ
	var positions_exact := positions.has("alice") and positions.has("bob") and \
		absf(float(positions["alice"]["x"]) - expected_alice) < 0.000001 and \
		absf(float(positions["bob"]["x"]) - expected_bob) < 0.000001
	var cases := [
		{"name": "two_clients_admitted", "passed": _joined_peers.values().has("alice") and _joined_peers.values().has("bob")},
		{"name": "two_client_intents_accepted", "passed": _accepted_intents.size() == 2},
		{"name": "one_server_owned_tick", "passed": _tick_sent and _model.tick_number == 1},
		{"name": "nonadmitted_peer_excluded_at_broadcast", "passed": _nonadmitted_peer_present_at_tick and _snapshot_recipients == 2},
		{"name": "exact_tick_one_positions", "passed": positions_exact},
		{"name": "adversarial_rejections_preserved_state", "passed": not _rejected_state_changed and int(_rejections.get("not_admitted", 0)) >= 1 and int(_rejections.get("invalid_admission", 0)) >= 1 and int(_rejections.get("forbidden_authority_field", 0)) >= 2 and int(_rejections.get("stale_sequence", 0)) >= 1 and int(_rejections.get("session_connection_mismatch", 0)) >= 1},
		{"name": "both_clients_completed_bound_session", "passed": _done.has("alice") and _done.has("bob")},
	]
	var passed: bool = _error.is_empty()
	for item in cases:
		passed = passed and item["passed"]
	var report := {"schema": "enfractal-phase2-multiprocess-authority-v1", "run_id": _run_id,
		"role": "authority", "process_id": OS.get_process_id(), "engine": Engine.get_version_info()["string"], "os": OS.get_name(),
		"utc": Time.get_datetime_string_from_system(true), "bind_address": HOST,
		"public_listener": false, "private_keys_saved": false, "tick": _model.tick_number if _model != null else -1,
		"connected_peers_at_tick": _connected_at_tick, "snapshot_recipients": _snapshot_recipients,
		"positions": positions, "rejection_codes": _rejections, "error": _error,
		"cases": cases, "passed": passed}
	if not _run_dir.is_empty():
		_write_json(_run_dir.path_join("authority-report.json"), report)
	print(JSON.stringify(report))
	if _host != null:
		_host.destroy()
	quit(0 if passed else 1)


func _argument(name: String) -> String:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == name:
			return args[i + 1]
	return ""


func _write_json(path: String, value: Dictionary) -> bool:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return false
	file.store_string(JSON.stringify(value, "\t") + "\n")
	file.close()
	return true


func _x509_time(unix_time: int) -> String:
	var date := Time.get_datetime_dict_from_unix_time(unix_time)
	return "%04d%02d%02d%02d%02d%02d" % [date["year"], date["month"], date["day"], date["hour"], date["minute"], date["second"]]
