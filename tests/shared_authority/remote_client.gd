extends SceneTree
## One of two independent Godot client processes in a loopback authority fixture.

const Model = preload("res://authority_model.gd")
const TIMEOUT_MS := 6000

var _role := ""
var _run_id := ""
var _run_dir := ""
var _config: Dictionary = {}
var _host: ENetConnection
var _peer: ENetPacketPeer
var _inbox: Array[Dictionary] = []
var _replay_host: ENetConnection
var _replay_inbox: Array[Dictionary] = []
var _session := ""
var _cases: Array[Dictionary] = []
var _error := ""
var _observed_tick := -1
var _observed_positions: Dictionary = {}
var _observed_rules_revision := -1


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_role = _argument("--role")
	_run_id = _argument("--run-id")
	_run_dir = _argument("--run-dir")
	if not _role in ["alice", "bob"] or _run_id.is_empty() or _run_dir.is_empty():
		_error = "missing_client_arguments"
		_finish()
		return
	_config = _read_json(_run_dir.path_join("admission.json"))
	if _config.get("run_id") != _run_id or _config.get("host") != "127.0.0.1":
		_error = "invalid_admission_config"
		_finish()
		return
	var connection := _connect()
	if connection.is_empty():
		_error = "dtls_connection_failed"
		_finish()
		return
	_host = connection["host"]
	_peer = connection["peer"]
	if _role == "alice":
		_run_alice()
	else:
		_run_bob()
	if _session.is_empty():
		_error = "missing_session"
	else:
		_record("bound_completion", _request(_host, _peer, _inbox, {"type": "probe_done", "run_id": _run_id, "session_id": _session}).get("code") == "probe_done")
	_finish()


func _run_alice() -> void:
	var before := _base_intent(1, 1.0, 0.0, false)
	_record("preadmission_intent_rejected", _request(_host, _peer, _inbox, before).get("code") == "not_admitted")
	_record("preadmission_no_snapshot", _await_snapshot(_host, _inbox, 200).is_empty())
	_join("alice", _config["tickets"]["alice"])
	if _session.is_empty():
		return
	if not _write_json(_run_dir.path_join("alice-session.json"), {"run_id": _run_id, "session_id": _session}):
		_error = "session_barrier_write_failed"
		return
	_record("valid_glide_intent", _request(_host, _peer, _inbox, _base_intent(1, 1.0, 0.0, true)).get("code") == "accepted_intent")
	_verify_snapshot("private_for_alice")
	var forged := _base_intent(2, 0.0, 0.0, false)
	forged["position_m"] = {"x": 1000000.0, "z": 1000000.0}
	_record("forged_position_rejected", _request(_host, _peer, _inbox, forged).get("code") == "forbidden_authority_field")
	_record("duplicate_sequence_rejected", _request(_host, _peer, _inbox, _base_intent(1, -1.0, 0.0, false)).get("code") == "stale_sequence")
	var forged_rules := _base_intent(2, 0.0, 0.0, true)
	forged_rules["wind_mps"] = {"x": 999.0, "z": 0.0}
	_record("forged_rules_rejected", _request(_host, _peer, _inbox, forged_rules).get("code") == "forbidden_authority_field")


func _run_bob() -> void:
	var session_marker := _wait_for_json(_run_dir.path_join("alice-session.json"), TIMEOUT_MS)
	if session_marker.get("run_id") != _run_id or not session_marker.has("session_id"):
		_error = "alice_session_barrier_timeout"
		return
	var replay_connection := _connect()
	if replay_connection.is_empty():
		_error = "replay_probe_connection_failed"
		return
	_replay_host = replay_connection["host"]
	var replay_peer: ENetPacketPeer = replay_connection["peer"]
	_record("one_use_ticket_rejected_on_other_connection", _request(_replay_host, replay_peer, _replay_inbox, {"type": "join", "ticket": _config["tickets"]["alice"]}).get("code") == "invalid_admission")
	_record("replay_probe_no_snapshot_before_broadcast", _await_snapshot(_replay_host, _replay_inbox, 200).is_empty())
	_join("bob", _config["tickets"]["bob"])
	if _session.is_empty():
		return
	if not _write_json(_run_dir.path_join("bob-session.json"), {"run_id": _run_id, "session_id": _session}):
		_error = "bob_session_write_failed"
		return
	var copied := _base_intent(1, 1.0, 0.0, false)
	copied["session_id"] = session_marker["session_id"]
	_record("copied_other_process_session_rejected", _request(_host, _peer, _inbox, copied).get("code") == "session_connection_mismatch")
	_record("valid_move_intent", _request(_host, _peer, _inbox, _base_intent(1, -1.0, 0.0, false)).get("code") == "accepted_intent")
	_verify_snapshot("private_for_bob")
	_record("replay_probe_no_snapshot_after_tick_one", _observed_tick == 1 and _await_snapshot(_replay_host, _replay_inbox, 250).is_empty())
	var forged_owner := _base_intent(2, 1.0, 0.0, false)
	forged_owner["owner_id"] = "alice"
	_record("forged_owner_rejected", _request(_host, _peer, _inbox, forged_owner).get("code") == "forbidden_authority_field")
	var wrong_world := _base_intent(2, 1.0, 0.0, false)
	wrong_world["world_id"] = "other_world"
	_record("wrong_world_rejected", _request(_host, _peer, _inbox, wrong_world).get("code") == "target_mismatch")
	var stale_epoch := _base_intent(2, 1.0, 0.0, false)
	stale_epoch["authority_epoch"] = 0
	_record("stale_epoch_rejected", _request(_host, _peer, _inbox, stale_epoch).get("code") == "stale_epoch")


func _join(avatar: String, ticket: String) -> void:
	var response := _request(_host, _peer, _inbox, {"type": "join", "ticket": ticket})
	var passed: bool = response.get("code") == "joined" and response.get("avatar") == avatar and str(response.get("session_id", "")).length() >= 16
	_record("valid_one_use_join", passed)
	if passed:
		_session = response["session_id"]


func _verify_snapshot(private_marker: String) -> void:
	var snapshot := _await_snapshot(_host, _inbox, TIMEOUT_MS)
	var expected_alice: float = (Model.SPEED_MPS + Model.WIND_MPS.x) / Model.TICK_HZ
	var expected_bob: float = -Model.SPEED_MPS / Model.TICK_HZ
	var positions: Variant = snapshot.get("positions", {})
	var valid: bool = snapshot.get("type") == "snapshot" and snapshot.get("tick") == 1 and \
		snapshot.get("authority_epoch") == Model.AUTHORITY_EPOCH and snapshot.get("rules_revision") == Model.RULES_REVISION and \
		positions is Dictionary and positions.has("alice") and positions.has("bob")
	if valid:
		valid = absf(float(positions["alice"].get("x", 9999.0)) - expected_alice) < 0.000001 and \
			absf(float(positions["bob"].get("x", 9999.0)) - expected_bob) < 0.000001 and \
			absf(float(positions["alice"].get("z", 9999.0))) < 0.000001 and \
			absf(float(positions["bob"].get("z", 9999.0))) < 0.000001
	_record("received_exact_tick_one_shared_positions", valid)
	_record("snapshot_exact_allowlist", _snapshot_allowlisted(snapshot))
	var other_marker := "private_for_bob" if _role == "alice" else "private_for_alice"
	_record("private_marker_filtered", snapshot.get("private_marker") == private_marker and JSON.stringify(snapshot).find(other_marker) == -1)
	if valid:
		_observed_tick = snapshot["tick"]
		_observed_positions = snapshot["positions"]
		_observed_rules_revision = snapshot["rules_revision"]


func _snapshot_allowlisted(snapshot: Dictionary) -> bool:
	if not _only_keys(snapshot, ["type", "tick", "authority_epoch", "rules_revision", "positions", "private_marker"]):
		return false
	var positions: Variant = snapshot.get("positions")
	if not positions is Dictionary or not _only_keys(positions, ["alice", "bob"]):
		return false
	for avatar in ["alice", "bob"]:
		var position: Variant = positions.get(avatar)
		if not position is Dictionary or not _only_keys(position, ["x", "z"]):
			return false
		for axis in ["x", "z"]:
			if not position.has(axis) or not (typeof(position[axis]) == TYPE_FLOAT or typeof(position[axis]) == TYPE_INT):
				return false
	return true


func _only_keys(value: Dictionary, expected: Array[String]) -> bool:
	if value.size() != expected.size():
		return false
	for key in expected:
		if not value.has(key):
			return false
	return true


func _base_intent(seq: int, x: float, z: float, glide: bool) -> Dictionary:
	return {"type": "intent", "session_id": _session, "seq": seq,
		"world_id": Model.WORLD_ID, "frame_id": Model.FRAME_ID,
		"authority_epoch": Model.AUTHORITY_EPOCH, "move_x": x, "move_z": z, "glide": glide}


func _connect() -> Dictionary:
	var certificate := X509Certificate.new()
	if certificate.load(_config.get("certificate_path", "")) != OK:
		return {}
	var host := ENetConnection.new()
	if host.create_host_bound("127.0.0.1", 0, 1, 2) != OK:
		return {}
	if host.dtls_client_setup(_config.get("hostname", ""), TLSOptions.client(certificate)) != OK:
		host.destroy()
		return {}
	var peer := host.connect_to_host("127.0.0.1", int(_config.get("port", -1)), 2)
	if peer == null:
		host.destroy()
		return {}
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < TIMEOUT_MS:
		var event := host.service(5)
		if event[0] == ENetConnection.EVENT_CONNECT:
			return {"host": host, "peer": peer}
		if event[0] == ENetConnection.EVENT_DISCONNECT:
			break
	host.destroy()
	return {}


func _request(host: ENetConnection, peer: ENetPacketPeer, inbox: Array[Dictionary], packet: Dictionary) -> Dictionary:
	if peer.send(0, JSON.stringify(packet).to_utf8_buffer(), ENetPacketPeer.FLAG_RELIABLE) != OK:
		return {"code": "send_failed"}
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < TIMEOUT_MS:
		_service_into(host, inbox)
		for i in inbox.size():
			if inbox[i].has("code"):
				return inbox.pop_at(i)
	return {"code": "response_timeout"}


func _await_snapshot(host: ENetConnection, inbox: Array[Dictionary], timeout_ms: int) -> Dictionary:
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < timeout_ms:
		_service_into(host, inbox)
		for i in inbox.size():
			if inbox[i].get("type") == "snapshot":
				return inbox.pop_at(i)
	return {}


func _service_into(host: ENetConnection, inbox: Array[Dictionary]) -> void:
	var event: Array = host.service(5)
	if event[0] == ENetConnection.EVENT_RECEIVE:
		var decoded: Variant = JSON.parse_string(event[1].get_packet().get_string_from_utf8())
		if decoded is Dictionary:
			inbox.append(decoded)


func _record(name: String, passed: bool) -> void:
	_cases.append({"name": name, "passed": passed})


func _finish() -> void:
	var passed: bool = _error.is_empty()
	for item in _cases:
		passed = passed and item["passed"]
	var report := {"schema": "enfractal-phase2-multiprocess-client-v1", "run_id": _run_id,
		"role": _role, "process_id": OS.get_process_id(), "engine": Engine.get_version_info()["string"], "os": OS.get_name(),
		"observed_tick": _observed_tick, "observed_positions": _observed_positions,
		"observed_rules_revision": _observed_rules_revision,
		"utc": Time.get_datetime_string_from_system(true), "error": _error, "cases": _cases, "passed": passed}
	if not _run_dir.is_empty() and not _role.is_empty():
		_write_json(_run_dir.path_join(_role + "-report.json"), report)
	print(JSON.stringify(report))
	if _host != null:
		_host.destroy()
	if _replay_host != null:
		_replay_host.destroy()
	quit(0 if passed else 1)


func _argument(name: String) -> String:
	var args: PackedStringArray = OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == name:
			return args[i + 1]
	return ""


func _read_json(path: String) -> Dictionary:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		return {}
	var decoded: Variant = JSON.parse_string(file.get_as_text())
	return decoded if decoded is Dictionary else {}


func _wait_for_json(path: String, timeout_ms: int) -> Dictionary:
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < timeout_ms:
		var decoded := _read_json(path)
		if not decoded.is_empty():
			return decoded
		OS.delay_msec(5)
	return {}


func _write_json(path: String, value: Dictionary) -> bool:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		return false
	file.store_string(JSON.stringify(value, "\t") + "\n")
	file.close()
	return true
