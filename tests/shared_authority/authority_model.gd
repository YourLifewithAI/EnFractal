extends RefCounted
## Transient, single-process authority seam for hostile packet tests. No persistence.

const WORLD_ID := "fixture_world"
const FRAME_ID := "fixture_flat_cell"
const AUTHORITY_EPOCH := 1
const RULES_REVISION := 1
const TICK_HZ := 30.0
const SPEED_MPS := 3.0
const WIND_MPS := Vector2(1.0, 0.0)
const BOUND_M := 10.0

var _crypto := Crypto.new()
var _tickets: Dictionary = {}
var _avatars: Dictionary = {}
var _connection_to_avatar: Dictionary = {}
var _join_attempted: Dictionary = {}
var tick_number := 0


func _init() -> void:
	for avatar in ["alice", "bob"]:
		_avatars[avatar] = {
			"position": Vector2.ZERO,
			"last_seq": 0,
			"pending": {},
			"session_id": "",
			"connection": -1,
			"private_marker": "private_for_" + avatar,
		}


func issue_ticket(avatar: String) -> String:
	if not _avatars.has(avatar):
		return ""
	var ticket := _crypto.generate_random_bytes(32).hex_encode()
	_tickets[ticket.sha256_text()] = {
		"avatar": avatar, "world_id": WORLD_ID, "frame_id": FRAME_ID,
		"authority_epoch": AUTHORITY_EPOCH,
		"expires_at_ms": Time.get_ticks_msec() + 30000,
		"used": false,
	}
	return ticket


func handle(connection: int, packet: Dictionary) -> Dictionary:
	if not packet.has("type") or not packet["type"] is String:
		return {"ok": false, "code": "invalid_message"}
	if packet["type"] == "join":
		return _join(connection, packet)
	if packet["type"] == "intent":
		return _intent(connection, packet)
	return {"ok": false, "code": "unsupported_operation"}


func _join(connection: int, packet: Dictionary) -> Dictionary:
	if _join_attempted.has(connection):
		return {"ok": false, "code": "join_already_attempted"}
	_join_attempted[connection] = true
	if not _only_keys(packet, ["type", "ticket"]) or not packet.has("ticket") or not packet["ticket"] is String:
		return {"ok": false, "code": "invalid_admission"}
	if _connection_to_avatar.has(connection):
		return {"ok": false, "code": "already_admitted"}
	var digest: String = packet["ticket"].sha256_text()
	if not _tickets.has(digest) or bool(_tickets[digest]["used"]):
		return {"ok": false, "code": "invalid_admission"}
	var ticket_record: Dictionary = _tickets[digest]
	if ticket_record["world_id"] != WORLD_ID or ticket_record["frame_id"] != FRAME_ID or ticket_record["authority_epoch"] != AUTHORITY_EPOCH or Time.get_ticks_msec() > ticket_record["expires_at_ms"]:
		return {"ok": false, "code": "invalid_admission"}
	_tickets[digest]["used"] = true
	var avatar: String = _tickets[digest]["avatar"]
	var record: Dictionary = _avatars[avatar]
	# A new admission fences an old connection for this logical avatar.
	record["session_id"] = _crypto.generate_random_bytes(16).hex_encode()
	record["connection"] = connection
	record["last_seq"] = 0
	record["pending"] = {}
	_connection_to_avatar[connection] = avatar
	return {"ok": true, "code": "joined", "avatar": avatar, "session_id": record["session_id"], "authority_epoch": AUTHORITY_EPOCH}


func _intent(connection: int, packet: Dictionary) -> Dictionary:
	if not _connection_to_avatar.has(connection):
		return {"ok": false, "code": "not_admitted"}
	var avatar: String = _connection_to_avatar[connection]
	var record: Dictionary = _avatars[avatar]
	if record["connection"] != connection:
		return {"ok": false, "code": "session_replaced"}
	if not _only_keys(packet, ["type", "session_id", "seq", "world_id", "frame_id", "authority_epoch", "move_x", "move_z", "glide"]):
		return {"ok": false, "code": "forbidden_authority_field"}
	if not packet.has("session_id") or typeof(packet["session_id"]) != TYPE_STRING:
		return {"ok": false, "code": "invalid_message"}
	if packet["session_id"] != record["session_id"]:
		return {"ok": false, "code": "session_connection_mismatch"}
	if not packet.has("world_id") or typeof(packet["world_id"]) != TYPE_STRING or not packet.has("frame_id") or typeof(packet["frame_id"]) != TYPE_STRING:
		return {"ok": false, "code": "invalid_message"}
	if packet.get("world_id") != WORLD_ID or packet.get("frame_id") != FRAME_ID:
		return {"ok": false, "code": "target_mismatch"}
	if not packet.has("authority_epoch") or not (typeof(packet["authority_epoch"]) == TYPE_INT or typeof(packet["authority_epoch"]) == TYPE_FLOAT):
		return {"ok": false, "code": "invalid_message"}
	var epoch_value: float = float(packet["authority_epoch"])
	if not is_finite(epoch_value) or epoch_value < 0.0 or epoch_value > 2147483647.0 or epoch_value != floorf(epoch_value):
		return {"ok": false, "code": "invalid_message"}
	if int(epoch_value) != AUTHORITY_EPOCH:
		return {"ok": false, "code": "stale_epoch"}
	if not packet.has("seq") or not (typeof(packet["seq"]) == TYPE_INT or typeof(packet["seq"]) == TYPE_FLOAT):
		return {"ok": false, "code": "invalid_sequence"}
	var seq_value: float = float(packet["seq"])
	if not is_finite(seq_value) or seq_value < 1.0 or seq_value > 2147483647.0 or seq_value != floorf(seq_value):
		return {"ok": false, "code": "invalid_sequence"}
	var seq: int = int(seq_value)
	if seq <= record["last_seq"]:
		return {"ok": false, "code": "stale_sequence"}
	if not packet.has("move_x") or not packet.has("move_z") or not packet.has("glide") or typeof(packet["glide"]) != TYPE_BOOL:
		return {"ok": false, "code": "invalid_message"}
	var x: Variant = packet["move_x"]
	var z: Variant = packet["move_z"]
	if not (typeof(x) == TYPE_FLOAT or typeof(x) == TYPE_INT) or not (typeof(z) == TYPE_FLOAT or typeof(z) == TYPE_INT):
		return {"ok": false, "code": "invalid_number"}
	if not is_finite(float(x)) or not is_finite(float(z)):
		return {"ok": false, "code": "invalid_number"}
	var move := Vector2(float(x), float(z))
	if move.length_squared() > 1.0 or absf(move.x) > 1.0 or absf(move.y) > 1.0:
		return {"ok": false, "code": "parameter_out_of_range"}
	record["last_seq"] = seq
	record["pending"] = {"move": move, "glide": packet["glide"]}
	return {"ok": true, "code": "accepted_intent", "avatar": avatar, "seq": seq}


func advance_tick() -> void:
	tick_number += 1
	for avatar in _avatars:
		var record: Dictionary = _avatars[avatar]
		if record["pending"].is_empty():
			continue
		var input: Dictionary = record["pending"]
		var delta: Vector2 = input["move"] * (SPEED_MPS / TICK_HZ)
		if input["glide"]:
			delta += WIND_MPS / TICK_HZ
		var next: Vector2 = record["position"] + delta
		record["position"] = Vector2(clampf(next.x, -BOUND_M, BOUND_M), clampf(next.y, -BOUND_M, BOUND_M))
		record["pending"] = {}


func snapshot(connection: int) -> Dictionary:
	if not _connection_to_avatar.has(connection):
		return {}
	var avatar: String = _connection_to_avatar[connection]
	var record: Dictionary = _avatars[avatar]
	if record["connection"] != connection:
		return {}
	return {
		"type": "snapshot",
		"tick": tick_number,
		"authority_epoch": AUTHORITY_EPOCH,
		"rules_revision": RULES_REVISION,
		"positions": shared_positions(),
		"private_marker": record["private_marker"],
	}


func shared_positions() -> Dictionary:
	var result := {}
	for avatar in _avatars:
		var pos: Vector2 = _avatars[avatar]["position"]
		result[avatar] = {"x": pos.x, "z": pos.y}
	return result


func is_bound_session(connection: int, session_id: String) -> bool:
	if not _connection_to_avatar.has(connection):
		return false
	var record: Dictionary = _avatars[_connection_to_avatar[connection]]
	return record["connection"] == connection and record["session_id"] == session_id


func state_fingerprint() -> String:
	var result := {"tick": tick_number, "positions": shared_positions(), "last_seq": {}, "pending": {}}
	for avatar in _avatars:
		result["last_seq"][avatar] = _avatars[avatar]["last_seq"]
		result["pending"][avatar] = str(_avatars[avatar]["pending"])
	return JSON.stringify(result).sha256_text()


func _only_keys(packet: Dictionary, allowed: Array[String]) -> bool:
	for key in packet:
		if not key in allowed:
			return false
	return true
