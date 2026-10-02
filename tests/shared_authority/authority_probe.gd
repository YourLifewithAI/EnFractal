extends SceneTree
## Five independent ENet client connections and one authority inside one Godot process.
## The first two clients are gameplay participants; the others probe reuse/reconnect/absence.

const Model = preload("res://authority_model.gd")
const LOOPBACK := "127.0.0.1"
const SERVER_NAME := "enfractal-test.invalid"
const TIMEOUT_MS := 2500
const MAX_PACKET_BYTES := 1000

var _server: ENetConnection
var _clients: Array[ENetConnection] = []
var _outgoing: Array[ENetPacketPeer] = []
var _connected: Array[bool] = []
var _inboxes: Array[Array] = []
var _server_peers: Dictionary = {}
var _model: RefCounted
var _cases: Array[Dictionary] = []
var _sessions: Dictionary = {}
var _setup_error := ""


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_model = Model.new()
	if not _setup():
		_finish()
		return
	var alice_ticket: String = _model.issue_ticket("alice")
	var bob_ticket: String = _model.issue_ticket("bob")

	var preadmission := _base_intent(0, 1, 1.0, 0.0, false)
	_record_response("preadmission_intent", 0, preadmission, "not_admitted", true)
	_broadcast_snapshots()
	_case("preadmission_no_world_snapshot", _await_snapshot(0, 300).is_empty(), "encrypted_peer_gets_no_snapshot_before_ticket")
	_record_join("alice_admitted", 0, alice_ticket, "alice")
	_record_join("bob_admitted", 1, bob_ticket, "bob")
	_record_response("ticket_single_use", 2, {"type": "join", "ticket": alice_ticket}, "invalid_admission", true)
	_broadcast_snapshots()
	_case("ticket_reuse_no_world_snapshot", _await_snapshot(2, 300).is_empty(), "rejected_ticket_gets_no_snapshot")
	_record_response("missing_ticket_rejected", 4, {"type": "join", "ticket": ""}, "invalid_admission", true)
	_broadcast_snapshots()
	_case("missing_ticket_no_world_snapshot", _await_snapshot(4, 300).is_empty(), "missing_ticket_gets_no_snapshot")
	var both_joined := _sessions.has(0) and _sessions.has(1)
	if both_joined:
		_record_response("alice_valid_intent", 0, _base_intent(0, 1, 1.0, 0.0, true), "accepted_intent", false)
		_record_response("bob_valid_intent", 1, _base_intent(1, 1, -1.0, 0.0, false), "accepted_intent", false)
		_model.advance_tick()
		var authority_positions: Dictionary = _model.shared_positions()
		# Negative control: Bob has a queued tick-0 snapshot, but tick 1 is withheld.
		# A reader that accepts the first snapshot would falsely pass this case.
		var bob_server_key: int = _server_key_for_client(1)
		var bob_had_stale_tick0: bool = _has_queued_snapshot(1, 0)
		_broadcast_snapshots(bob_server_key)
		_case("stale_tick0_cannot_pass_missing_tick1", bob_server_key != -1 and bob_had_stale_tick0 and _await_snapshot(1, 300, 1).is_empty(), "queued_tick_0_ignored_when_tick_1_withheld")
		_broadcast_snapshots()
		var alice_snapshot := _await_snapshot(0, TIMEOUT_MS, 1)
		var bob_snapshot := _await_snapshot(1, TIMEOUT_MS, 1)
		var shared_equal: bool = _snapshot_matches_authority(alice_snapshot, 1, authority_positions) and _snapshot_matches_authority(bob_snapshot, 1, authority_positions) and _positions_match(alice_snapshot.get("positions", {}), bob_snapshot.get("positions", {}))
		_case("two_clients_same_authority_snapshot", shared_equal, "both_received_tick_1_epoch_1_rules_1_and_server_positions")
		var private_filtered: bool = shared_equal and alice_snapshot.get("private_marker") == "private_for_alice" and bob_snapshot.get("private_marker") == "private_for_bob"
		_case("private_snapshot_fields_filtered", private_filtered, "each_client_receives_own_marker_only")
		var positions: Dictionary = _model.shared_positions()
		var alice_x: float = positions["alice"]["x"]
		var bob_x: float = positions["bob"]["x"]
		var expected_alice: float = (Model.SPEED_MPS + Model.WIND_MPS.x) / Model.TICK_HZ
		var expected_bob: float = -Model.SPEED_MPS / Model.TICK_HZ
		_case("server_owned_wind_and_glide", absf(alice_x - expected_alice) < 0.000001 and absf(bob_x - expected_bob) < 0.000001, "authority_integrated_intents_under_pinned_rules")

		var forged_position := _base_intent(0, 2, 0.0, 0.0, false)
		forged_position["position_m"] = {"x": 1000000.0, "y": 1000000.0, "z": 1000000.0}
		_record_response("forged_position_rejected", 0, forged_position, "forbidden_authority_field", true)
		var forged_owner := _base_intent(1, 2, 1.0, 0.0, false)
		forged_owner["owner_id"] = "alice"
		forged_owner["avatar_id"] = "avatar_alice"
		_record_response("forged_owner_and_actor_rejected", 1, forged_owner, "forbidden_authority_field", true)
		var forged_wind := _base_intent(0, 2, 0.0, 0.0, true)
		forged_wind["wind_mps"] = {"x": 100.0, "y": 0.0, "z": 0.0}
		forged_wind["world_rules_revision"] = 999
		_record_response("client_rules_rejected", 0, forged_wind, "forbidden_authority_field", true)
		_record_response("duplicate_sequence_rejected", 0, _base_intent(0, 1, 1.0, 0.0, false), "stale_sequence", true)
		_record_response("changed_duplicate_sequence_rejected", 0, _base_intent(0, 1, -1.0, 0.0, true), "stale_sequence", true)
		var copied_session := _base_intent(1, 2, 1.0, 0.0, false)
		copied_session["session_id"] = _sessions[0]
		_record_response("copied_session_rejected", 1, copied_session, "session_connection_mismatch", true)
		var wrong_world := _base_intent(0, 2, 1.0, 0.0, false)
		wrong_world["world_id"] = "other_world"
		_record_response("wrong_world_rejected", 0, wrong_world, "target_mismatch", true)
		var stale_epoch := _base_intent(0, 2, 1.0, 0.0, false)
		stale_epoch["authority_epoch"] = 0
		_record_response("stale_epoch_rejected", 0, stale_epoch, "stale_epoch", true)
		var invalid_session_type := _base_intent(0, 2, 1.0, 0.0, false)
		invalid_session_type["session_id"] = []
		_record_response("non_string_session_rejected", 0, invalid_session_type, "invalid_message", true)
		var invalid_world_type := _base_intent(0, 2, 1.0, 0.0, false)
		invalid_world_type["world_id"] = {}
		_record_response("non_string_world_rejected", 0, invalid_world_type, "invalid_message", true)
		var invalid_frame_type := _base_intent(0, 2, 1.0, 0.0, false)
		invalid_frame_type["frame_id"] = false
		_record_response("non_string_frame_rejected", 0, invalid_frame_type, "invalid_message", true)
		var invalid_epoch_type := _base_intent(0, 2, 1.0, 0.0, false)
		invalid_epoch_type["authority_epoch"] = []
		_record_response("non_numeric_epoch_rejected", 0, invalid_epoch_type, "invalid_message", true)
		var boolean_epoch := _base_intent(0, 2, 1.0, 0.0, false)
		boolean_epoch["authority_epoch"] = true
		_record_response("boolean_epoch_rejected", 0, boolean_epoch, "invalid_message", true)
		_record_response("out_of_range_axis_rejected", 0, _base_intent(0, 2, 2.0, 0.0, false), "parameter_out_of_range", true)
		var bad_number := _base_intent(0, 2, 0.0, 0.0, false)
		bad_number["move_x"] = "NaN"
		_record_response("non_numeric_axis_rejected", 0, bad_number, "invalid_number", true)
		var forged_time := _base_intent(0, 2, 1.0, 0.0, false)
		forged_time["client_elapsed_s"] = 3600.0
		_record_response("client_time_rejected", 0, forged_time, "forbidden_authority_field", true)
		_record_response("unknown_operation_rejected", 0, {"type": "execute_admin"}, "unsupported_operation", true)
		_record_oversized()

		_record_response("old_pending_intent_before_reconnect", 0, _base_intent(0, 2, 1.0, 0.0, false), "accepted_intent", false)
		var before_reconnect_x: float = _model.shared_positions()["alice"]["x"]
		var reconnect_ticket: String = _model.issue_ticket("alice")
		_record_join("alice_reconnected", 3, reconnect_ticket, "alice")
		_record_response("old_connection_fenced", 0, _base_intent(0, 3, 1.0, 0.0, false), "session_replaced", true)
		_model.advance_tick()
		_broadcast_snapshots()
		var replacement_snapshot := _await_snapshot(3, TIMEOUT_MS, 2)
		var after_fence_positions: Dictionary = _model.shared_positions()
		_case("old_pending_intent_cleared", _snapshot_matches_authority(replacement_snapshot, 2, after_fence_positions) and absf(float(after_fence_positions["alice"]["x"]) - before_reconnect_x) < 0.000001, "replacement_tick_2_excludes_old_queued_intent")
		var old_key: int = _server_key_for_client(0)
		_case("old_connection_no_snapshot", old_key != -1 and _await_snapshot(0, 300, 2).is_empty(), "old_session_received_no_tick_2_wire_snapshot")
		_record_response("new_connection_intent", 3, _base_intent(3, 1, 1.0, 0.0, false), "accepted_intent", false)
		_model.advance_tick()
		_broadcast_snapshots()
		var new_snapshot := _await_snapshot(3, TIMEOUT_MS, 3)
		var expected_new_x: float = before_reconnect_x + Model.SPEED_MPS / Model.TICK_HZ
		var new_positions: Dictionary = _model.shared_positions()
		_case("new_connection_tick3_observed", _snapshot_matches_authority(new_snapshot, 3, new_positions) and absf(float(new_positions["alice"]["x"]) - expected_new_x) < 0.000001, "new_session_intent_applied_and_delivered_at_tick_3")
	else:
		_case("both_clients_joined", false, "valid_join_failed")
	_finish()


func _setup() -> bool:
	var crypto := Crypto.new()
	var key := crypto.generate_rsa(2048)
	var now := int(Time.get_unix_time_from_system())
	var certificate := crypto.generate_self_signed_certificate(key, "CN=%s,O=Enfractal Local Test,C=US" % SERVER_NAME, _x509_time(now - 86400), _x509_time(now + 86400))
	if key == null or certificate == null:
		_setup_error = "ephemeral_certificate_failed"
		return false
	_server = ENetConnection.new()
	if _server.create_host_bound(LOOPBACK, 0, 5, 2) != OK:
		_setup_error = "loopback_server_bind_failed"
		return false
	if _server.dtls_server_setup(TLSOptions.server(key, certificate)) != OK:
		_setup_error = "dtls_server_setup_failed"
		return false
	for i in 5:
		var client := ENetConnection.new()
		if client.create_host_bound(LOOPBACK, 0, 1, 2) != OK:
			_setup_error = "loopback_client_bind_failed"
			return false
		if client.dtls_client_setup(SERVER_NAME, TLSOptions.client(certificate)) != OK:
			_setup_error = "dtls_client_setup_failed"
			return false
		var peer := client.connect_to_host(LOOPBACK, _server.get_local_port(), 2)
		if peer == null:
			_setup_error = "client_connect_allocation_failed"
			return false
		_clients.append(client)
		_outgoing.append(peer)
		_connected.append(false)
		_inboxes.append([])
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < TIMEOUT_MS:
		_pump()
		var all_connected := true
		for connected in _connected:
			all_connected = all_connected and connected
		if all_connected:
			return true
	_setup_error = "dtls_connect_timeout"
	return false


func _x509_time(unix_seconds: int) -> String:
	return Time.get_datetime_string_from_unix_time(unix_seconds).replace("-", "").replace(":", "").replace("T", "")


func _pump() -> void:
	var event := _server.service(0)
	if event[0] == ENetConnection.EVENT_CONNECT:
		var peer: ENetPacketPeer = event[1]
		_server_peers[peer.get_instance_id()] = peer
	elif event[0] == ENetConnection.EVENT_RECEIVE:
		var peer: ENetPacketPeer = event[1]
		var key: int = peer.get_instance_id()
		_server_peers[key] = peer
		var raw := peer.get_packet()
		var reply: Dictionary
		if raw.size() > MAX_PACKET_BYTES:
			reply = {"ok": false, "code": "message_too_large"}
		else:
			var decoded: Variant = JSON.parse_string(raw.get_string_from_utf8())
			if not decoded is Dictionary:
				reply = {"ok": false, "code": "invalid_message"}
			else:
				reply = _model.handle(key, decoded)
		peer.send(0, JSON.stringify(reply).to_utf8_buffer(), ENetPacketPeer.FLAG_RELIABLE)
	for i in _clients.size():
		var client_event := _clients[i].service(0)
		if client_event[0] == ENetConnection.EVENT_CONNECT:
			_connected[i] = true
		elif client_event[0] == ENetConnection.EVENT_RECEIVE:
			var received: Variant = JSON.parse_string(client_event[1].get_packet().get_string_from_utf8())
			if received is Dictionary:
				_inboxes[i].append(received)
	OS.delay_msec(1)


func _send(client_index: int, packet: Dictionary) -> Dictionary:
	return _send_bytes(client_index, JSON.stringify(packet).to_utf8_buffer())


func _send_bytes(client_index: int, bytes: PackedByteArray) -> Dictionary:
	if _outgoing[client_index].send(0, bytes, ENetPacketPeer.FLAG_RELIABLE) != OK:
		return {"code": "send_failed"}
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < TIMEOUT_MS:
		_pump()
		for j in _inboxes[client_index].size():
			var item: Dictionary = _inboxes[client_index][j]
			if item.has("code"):
				_inboxes[client_index].remove_at(j)
				return item
	return {"code": "response_timeout"}


func _record_join(name: String, client_index: int, ticket: String, avatar: String) -> void:
	var response := _send(client_index, {"type": "join", "ticket": ticket})
	var passed: bool = response.get("code") == "joined" and response.get("avatar") == avatar and str(response.get("session_id", "")).length() >= 16
	if passed:
		_sessions[client_index] = response["session_id"]
	_case(name, passed, "application_session_bound_to_client_connection")


func _record_response(name: String, client_index: int, packet: Dictionary, expected_code: String, unchanged: bool) -> void:
	var before: String = _model.state_fingerprint()
	var response := _send(client_index, packet)
	var passed: bool = response.get("code") == expected_code
	if unchanged:
		passed = passed and before == _model.state_fingerprint()
	_case(name, passed, "expected_%s_observed_%s" % [expected_code, response.get("code", "none")])


func _record_oversized() -> void:
	var before: String = _model.state_fingerprint()
	var response := _send_bytes(0, ("x".repeat(MAX_PACKET_BYTES + 1)).to_utf8_buffer())
	_case("oversized_packet_rejected", response.get("code") == "message_too_large" and before == _model.state_fingerprint(), "byte_limit_before_decode")


func _base_intent(client_index: int, seq: int, x: float, z: float, glide: bool) -> Dictionary:
	return {"type": "intent", "session_id": _sessions.get(client_index, ""), "seq": seq,
		"world_id": Model.WORLD_ID, "frame_id": Model.FRAME_ID, "authority_epoch": Model.AUTHORITY_EPOCH,
		"move_x": x, "move_z": z, "glide": glide}


func _broadcast_snapshots(skip_key: int = -1) -> void:
	for key in _server_peers:
		if key == skip_key:
			continue
		var snapshot: Dictionary = _model.snapshot(key)
		if not snapshot.is_empty():
			var peer: ENetPacketPeer = _server_peers[key]
			peer.send(0, JSON.stringify(snapshot).to_utf8_buffer(), ENetPacketPeer.FLAG_RELIABLE)


func _await_snapshot(client_index: int, timeout_ms: int = TIMEOUT_MS, expected_tick: int = -1) -> Dictionary:
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < timeout_ms:
		_pump()
		for j in _inboxes[client_index].size():
			var item: Dictionary = _inboxes[client_index][j]
			if item.get("type") == "snapshot":
				_inboxes[client_index].remove_at(j)
				if expected_tick < 0 or item.get("tick") == expected_tick:
					return item
				break
	return {}


func _has_queued_snapshot(client_index: int, expected_tick: int) -> bool:
	for item in _inboxes[client_index]:
		if item is Dictionary and item.get("type") == "snapshot" and item.get("tick") == expected_tick:
			return true
	return false


func _snapshot_matches_authority(snapshot: Dictionary, expected_tick: int, authority_positions: Dictionary) -> bool:
	return not snapshot.is_empty() and snapshot.get("tick") == expected_tick \
		and snapshot.get("authority_epoch") == Model.AUTHORITY_EPOCH \
		and snapshot.get("rules_revision") == Model.RULES_REVISION \
		and _positions_match(snapshot.get("positions", {}), authority_positions)


func _positions_match(received: Variant, expected: Variant) -> bool:
	if not received is Dictionary or not expected is Dictionary:
		return false
	if received.size() != expected.size():
		return false
	for avatar in expected:
		if not received.has(avatar) or not received[avatar] is Dictionary:
			return false
		for axis in ["x", "z"]:
			if not received[avatar].has(axis) or not expected[avatar].has(axis):
				return false
			var observed: Variant = received[avatar][axis]
			if not (typeof(observed) == TYPE_FLOAT or typeof(observed) == TYPE_INT):
				return false
			if absf(float(observed) - float(expected[avatar][axis])) > 0.000001:
				return false
	return true


func _server_key_for_client(client_index: int) -> int:
	# Match the bound ENet peer via its remote UDP port; used only for a local assertion.
	var client_port: int = _clients[client_index].get_local_port()
	for key in _server_peers:
		var peer: ENetPacketPeer = _server_peers[key]
		if peer.get_remote_port() == client_port:
			return key
	return -1


func _case(name: String, passed: bool, detail: String) -> void:
	_cases.append({"name": name, "passed": passed, "detail": detail})


func _finish() -> void:
	var passed := _setup_error.is_empty()
	for item in _cases:
		passed = passed and bool(item["passed"])
	var report := {"schema": "enfractal-phase2-authority-probe-v1", "run_id": OS.get_environment("ENFRACTAL_AUTHORITY_RUN_ID"),
		"engine": Engine.get_version_info()["string"], "os": OS.get_name(), "utc": Time.get_datetime_string_from_system(true),
		"bind_address": LOOPBACK, "public_listener": false, "private_keys_saved": false,
		"separate_client_processes": false, "independent_client_connections": 5,
		"scope": "transient single-process ENet+DTLS authority and adversarial packets; no account service, durable receipts, remote host or loss test",
		"setup_error": _setup_error, "passed": passed, "cases": _cases}
	var output_path: String = OS.get_environment("ENFRACTAL_AUTHORITY_REPORT")
	if not output_path.is_empty():
		var file := FileAccess.open(output_path, FileAccess.WRITE)
		if file != null:
			file.store_string(JSON.stringify(report, "\t") + "\n")
			file.close()
		else:
			passed = false
	print(JSON.stringify(report, "\t"))
	for client in _clients:
		client.destroy()
	if _server != null:
		_server.destroy()
	quit(0 if passed else 1)
