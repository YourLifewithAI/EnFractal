extends SceneTree
## The creation authority against room bounds, locks, a surface query and the contract principals.
## The fixture room is a 120 x 40 x 120 m workshop whose floor surface is at 10 m; obj:garden is the
## old protected garden, now protected only while it is locked.

const Authority = preload("res://scripts/creation_authority.gd")
const Compiler = preload("res://scripts/creation_compiler.gd")
const PLAYER := "player:local"
const COMPANION := "companion:local"
const ROOM := {
	"room_id": "authority_fixture",
	"manifest_sha256": "abababababababababababababababababababababababababababababababab",
	"bounds": {"min_m": [-60, 0, 290], "max_m": [60, 40, 410]},
	"entities": {
		"shell:floor": {"min_m": [-60, 9.9, 290], "max_m": [60, 10, 410]},
		"obj:garden": {"min_m": [-15, 10, 380], "max_m": [15, 11, 410]},
		"obj:table": {"min_m": [20, 10, 300], "max_m": [22, 11, 302]},
	},
}
var failures := 0
var checks := 0
var paths: Array[String] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var host = _fresh("main")
	_expect(not host.snapshot(PLAYER).consent[PLAYER] and not host.snapshot(PLAYER).consent[COMPANION], "fresh room requires explicit consent from both principals")
	_expect(host.snapshot(PLAYER).roles[COMPANION] == "editor", "the companion starts as an editor: it is the player's magic")
	var source := _source()
	var initial := _command(host, "place", "initial", source)
	var first: Dictionary = host.submit(PLAYER, initial)
	_expect(first.get("ok", false), "valid source compiles and persists")
	if not first.get("ok", false):
		print(first)
		quit(1)
		return
	var id: String = first.instance_id
	var snapshot: Dictionary = host.snapshot(PLAYER)
	_expect(snapshot.instances[id].owner_id == PLAYER and snapshot.instances[id].position_m == [0.0, 10.0, 350.0], "host assigns owner and surface height")
	_expect(snapshot.instances[id].artifact.hash == Compiler.compile(source).artifact.hash, "host derives compiled artifact")
	_expect(first.created == [id] and first.affected.is_empty(), "a placement receipt names the created entity")
	var replay: Dictionary = host.submit(PLAYER, initial)
	_expect(replay.ok and replay.replayed and host.revision == 1, "committed retry precedes revision conflict")
	var altered := initial.duplicate(true)
	altered.source.name = "Changed payload"
	_expect_code(host.submit(PLAYER, altered), "action_id_conflict", "action ID cannot change content")
	_expect_code(host.submit("intruder", initial), "principal_unknown", "unknown principal cannot read a receipt")
	_expect_code(host.submit(COMPANION, initial), "revision_conflict", "other principal never receives the player's receipt")
	for forged_field in ["owner_id", "principal", "height_m", "artifact", "cost", "permissions", "id", "approved_by", "approval"]:
		var forged := _command(host, "place", "forged_" + forged_field, source)
		forged[forged_field] = PLAYER
		_expect_code(host.submit(COMPANION, forged), "field_unknown", "reject caller field " + forged_field)
	_expect(host.set_role(PLAYER, COMPANION, "visitor").ok, "player can make the companion a visitor")
	_expect_code(host.submit(COMPANION, _command(host, "place", "visitor_place", source)), "build_denied", "visitor cannot build")
	_expect_code(host.set_role(COMPANION, COMPANION, "editor"), "role_denied", "companion cannot grant its own role")
	_expect_code(host.set_role(PLAYER, PLAYER, "visitor"), "role_invalid", "the player always owns the room")
	_expect(host.set_role(PLAYER, COMPANION, "editor").ok, "player grants editor role back")
	var revise := _command(host, "revise", "companion_revision", source, id)
	_expect_code(host.submit(COMPANION, revise), "ownership_denied", "editor cannot revise another creator")
	_expect_code(host.submit(COMPANION, _command(host, "remove", "companion_remove", {}, id)), "ownership_denied", "editor cannot remove another creator")
	var companion_request := _command(host, "place", "companion_place", source)
	companion_request.x_m = 5.0
	var companion: Dictionary = host.submit(COMPANION, companion_request)
	_expect(companion.ok, "editor places own invention")
	_expect(host.set_consent(COMPANION, true).ok, "companion opts into friendly effects")
	_expect(host.can_activate(COMPANION, id), "consenting companion can use the player's invention")
	_expect(host.can_affect(PLAYER, COMPANION, Vector3(0, 12, 350)), "authorized effect may reach consenting companion")
	var stale := _command(host, "place", "stale_permissions", source)
	_expect(host.set_role(PLAYER, COMPANION, "visitor").ok, "player revokes editor role")
	stale.expected_revision = host.revision
	_expect_code(host.submit(COMPANION, stale), "permission_revision_conflict", "preview cannot cross permission revision")
	_expect(not host.can_affect(COMPANION, PLAYER, Vector3(0, 12, 350)), "revoked creator's live effects cease")
	_expect(not host.snapshot(PLAYER).instances[companion.instance_id].active, "revoked creator's instance becomes inactive")
	var revoked_retry: Dictionary = host.submit(COMPANION, companion_request)
	_expect(revoked_retry.ok and revoked_retry.replayed, "committed principal-bound retry survives role revocation")
	_expect(host.set_consent(COMPANION, false).ok, "companion consent revoked")
	_expect(not host.can_affect(PLAYER, COMPANION, Vector3(0, 12, 350)), "revoked consent blocks next effect tick")
	_expect(not host.can_activate(COMPANION, id), "revoked consent blocks activation")
	_expect(host.preflight(PLAYER, _source(), 0.0, 385.0, 0.0).ok, "an unlocked garden is ordinary room space")
	_expect(not host.can_affect(PLAYER, PLAYER, Vector3(61, 12, 350)), "outside the room rejects effects")
	_expect(host.submit(PLAYER, _command(host, "remove", "moderate_companion", {}, companion.instance_id)).ok, "player can moderate the companion's creation")
	_expect(host.set_role(PLAYER, COMPANION, "editor").ok, "companion is an editor again")
	var copied: Dictionary = host.snapshot(PLAYER)
	copied.instances[id].source.name = "snapshot tamper"
	_expect(host.snapshot(PLAYER).instances[id].source.name != "snapshot tamper", "snapshot cannot mutate authority")
	_test_locks()
	_test_bounds()
	_test_surface()
	_test_capacity()
	_test_runtime()
	_test_approval_and_meta()
	_test_load(host, initial, id)
	_test_persist_failure()
	_test_occupancy()
	_test_preflight()
	for path in paths:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path + ".pending"))
	print("Creation authority: %d checks, %d failures" % [checks, failures])
	quit(1 if failures else 0)


func _test_locks() -> void:
	var host = _fresh("locks")
	_expect(host.entity_revision("obj:garden") == 0 and host.entity_revision("shell:floor") == 0 and host.entity_revision("obj:missing") == -1, "room entities start at revision 0; unknown ids are not in the room")
	_expect(host.preflight(PLAYER, _source(), 0.0, 379.75, 0.0).ok, "the garden is buildable while unlocked")
	var lock := _lock(host, "lock", "lock_garden", ["obj:garden"])
	var locked: Dictionary = host.submit(COMPANION, lock)
	_expect(locked.ok and locked.affected == ["obj:garden"] and host.is_locked("obj:garden"), "the companion can protect an object")
	_expect(host.entity_revision("obj:garden") == 1 and host.revision == 1, "a lock advances the object's and the room's revision")
	_expect(host.submit(COMPANION, lock).replayed, "a lock retry replays its receipt")
	_expect_code(host.submit(PLAYER, _lock(host, "lock", "lock_again", ["obj:garden"])), "already_locked", "a second lock is refused, not silently merged")
	var near_garden := _command(host, "place", "near_locked", _source())
	near_garden.z_m = 379.75
	_expect_code(host.submit(PLAYER, near_garden), "protected_zone", "assembly footprint cannot cross a locked object")
	_expect_code(host.preflight(PLAYER, _wind_source(), 0.0, 377.5, 0.0), "protected_zone", "a wind radius cannot reach into a locked object")
	_expect(not host.can_affect(PLAYER, PLAYER, Vector3(0, 12, 385)), "a locked zone receives no effects")
	_expect_code(host.submit(COMPANION, _lock(host, "unlock", "companion_unlock", ["obj:garden"])), "unlock_denied", "the companion can never unlock")
	_expect(host.is_locked("obj:garden") and host.revision == 1, "a refused unlock changes nothing")
	_expect_code(host.submit(PLAYER, _lock(host, "lock", "lock_floor", ["shell:floor"])), "target_invalid", "room shell parts are not lock targets")
	_expect_code(host.submit(PLAYER, _lock(host, "lock", "lock_ghost", ["obj:ghost"])), "instance_not_found", "an object that is not in this room cannot be locked")
	_expect_code(host.submit(PLAYER, _lock(host, "lock", "lock_twice", ["obj:table", "obj:table"])), "targets_invalid", "duplicate targets are refused")
	var placed: Dictionary = host.submit(PLAYER, _command(host, "place", "lockable", _source()))
	_expect(placed.ok, "a creation is placed for locking")
	_expect(host.submit(PLAYER, _lock(host, "lock", "lock_creation", [placed.instance_id])).ok, "the player locks a creation")
	_expect(host.entity_revision(placed.instance_id) == 2 and host.snapshot(PLAYER).instances[placed.instance_id].locked, "a locked creation's revision advances and snapshots show the lock")
	_expect_code(host.submit(PLAYER, _command(host, "revise", "revise_locked", _source(), placed.instance_id)), "target_locked", "a locked creation cannot be revised, even by the player")
	_expect_code(host.submit(PLAYER, _command(host, "remove", "remove_locked", {}, placed.instance_id)), "target_locked", "a locked creation cannot be removed, even by the player")
	var beside := _command(host, "place", "beside_locked", _source())
	beside.x_m = 0.5
	_expect_code(host.submit(PLAYER, beside), "protected_zone", "a new creation cannot overlap a locked creation")
	host.set_consent(PLAYER, true)
	_expect(host.can_affect(PLAYER, PLAYER, Vector3(0, 12, 350), placed.instance_id), "a locked creation's own field still works inside its own footprint")
	_expect(not host.can_affect(PLAYER, PLAYER, Vector3(0, 12, 350)), "other creations' fields cannot reach into a locked creation")
	var unlock: Dictionary = host.submit(PLAYER, _lock(host, "unlock", "unlock_creation", [placed.instance_id]))
	_expect(unlock.ok and not host.is_locked(placed.instance_id) and host.entity_revision(placed.instance_id) == 3, "the player unlocks directly and the revision advances")
	_expect_code(host.submit(PLAYER, _lock(host, "unlock", "unlock_again", [placed.instance_id])), "not_locked", "unlocking an unprotected thing is refused")
	_expect(host.submit(PLAYER, _command(host, "remove", "remove_unlocked", {}, placed.instance_id)).ok, "an unlocked creation can be removed again")
	var reloaded = Authority.new()
	_expect(reloaded.configure(ROOM, Callable(self, "_flat"), paths.back()).load_required and reloaded.load_saved().ok, "locks reload from the save")
	_expect(reloaded.is_locked("obj:garden") and reloaded.entity_revision("obj:garden") == 1 and reloaded.revision == host.revision, "lock, object revision and room revision survive reload")


func _test_bounds() -> void:
	var host = _fresh("bounds")
	host.submit(PLAYER, _lock(host, "lock", "garden", ["obj:garden"]))
	var source := _source()
	var request := _command(host, "place", "beyond_origin", source)
	request.x_m = 59.75
	_expect_code(host.submit(PLAYER, request), "room_bounds", "full footprint not only origin stays in the room")
	request = _command(host, "place", "rotated_edge", source)
	request.source.parts[0].size_m = [4.0, 1.0, 1.0]
	request.x_m = 58.5
	request.yaw_deg = 45.0
	_expect_code(host.submit(PLAYER, request), "room_bounds", "yaw expands footprint at edge")
	request = _command(host, "place", "rotor_swept_edge", source)
	request.source.parts[0].shape = "rotor"
	request.source.parts[0].size_m = [4.0, 0.1, 0.1]
	request.x_m = 59.0
	request.yaw_deg = 90.0
	_expect_code(host.submit(PLAYER, request), "room_bounds", "thin rotor reserves its complete spin sweep beyond narrow resting footprint")
	request = _command(host, "place", "garden_edge", source)
	request.z_m = 379.75
	_expect_code(host.submit(PLAYER, request), "protected_zone", "assembly boundary cannot cross locked garden")
	request = _command(host, "place", "field_garden", _wind_source())
	request.z_m = 377.5
	_expect_code(host.submit(PLAYER, request), "protected_zone", "wind radius cannot cross locked garden")
	request = _command(host, "place", "field_room", _wind_source())
	request.x_m = 58.0
	_expect_code(host.submit(PLAYER, request), "room_bounds", "wind envelope cannot leave the room")
	request = _command(host, "place", "source_invalid", source)
	request.source.parts[0].shape = "run_script"
	_expect(not host.submit(PLAYER, request).ok, "commit recompiles rejected arbitrary source")
	request = _command(host, "place", "nonfinite", source)
	request.x_m = NAN
	_expect_code(host.submit(PLAYER, request), "request_invalid", "nonfinite values fail before canonicalization")
	request = _command(host, "place", "huge_finite", source)
	request.x_m = 1e300
	_expect_code(host.submit(PLAYER, request), "room_bounds", "huge finite coordinates fail before float32 geometry conversion")
	var low_room := ROOM.duplicate(true)
	low_room.bounds.max_m = [60, 10.8, 410]
	var low = Authority.new()
	low.configure(low_room, Callable(self, "_flat"), _test_path("low_ceiling"))
	low.load_saved()
	_expect_code(low.submit(PLAYER, _command(low, "place", "too_tall", _source())), "room_bounds", "a ground creation must fit under the ceiling")
	var bad_room := ROOM.duplicate(true)
	bad_room.bounds.min_m = [60, 0, 290]
	bad_room.bounds.max_m = [-60, 40, 410]
	_expect_code(Authority.new().configure(bad_room, Callable(self, "_flat"), _test_path("bad_room")), "configuration_invalid", "inverted room bounds are refused")
	var no_pin := ROOM.duplicate(true)
	no_pin.manifest_sha256 = "not-a-hash"
	_expect_code(Authority.new().configure(no_pin, Callable(self, "_flat"), _test_path("bad_room")), "configuration_invalid", "a room without a manifest pin is refused")


func _test_surface() -> void:
	var source := _source()
	var surface_path := _test_path("surface")
	var surface_host = Authority.new()
	surface_host.configure(ROOM, func(_x, _z, _top): return {"ok": false}, surface_path)
	surface_host.load_saved()
	_expect_code(surface_host.submit(PLAYER, _command(surface_host, "place", "no_surface", source)), "surface_unavailable", "missing support fails closed")
	surface_host.configure(ROOM, func(x, _z, _top): return {"ok": true, "height_m": 10.0 + x * 0.1, "entity_id": "shell:floor"}, surface_path)
	surface_host.load_saved()
	_expect_code(surface_host.submit(PLAYER, _command(surface_host, "place", "uneven", source)), "surface_uneven", "a footprint over a 10 cm step is refused")
	surface_host.configure(ROOM, func(_x, _z, _top): return {"ok": true, "height_m": NAN, "entity_id": "shell:floor"}, surface_path)
	surface_host.load_saved()
	_expect_code(surface_host.submit(PLAYER, _command(surface_host, "place", "nan_surface", source)), "surface_unavailable", "a non-finite surface height fails closed")
	# A table top at 10.75 m spans x 20..22; the probe starts just above the height hint.
	var table := func(x: float, _z: float, top: float) -> Dictionary:
		if x >= 20.0 and x <= 22.0 and top >= 10.75:
			return {"ok": true, "height_m": 10.75, "entity_id": "obj:table"}
		return {"ok": true, "height_m": 10.0, "entity_id": "shell:floor"}
	surface_host.configure(ROOM, table, surface_path)
	surface_host.load_saved()
	var small := _source()
	small.parts[0].size_m = [0.4, 0.4, 0.4]
	small.parts[0].position_m = [0, 0.2, 0]
	var on_top: Dictionary = surface_host.preflight(PLAYER, small, 21.0, 301.0, 0.0, "", 10.75, "obj:table")
	_expect(on_top.ok and is_equal_approx(float(on_top.position_m[1]), 10.75) and on_top.surface_entity == "obj:table", "a height hint at the table top places on the table")
	var under: Dictionary = surface_host.preflight(PLAYER, small, 21.0, 301.0, 0.0, "", 10.0)
	_expect(under.ok and is_equal_approx(float(under.position_m[1]), 10.0) and under.surface_entity == "shell:floor", "a floor-level hint places under the table")
	_expect_code(surface_host.preflight(PLAYER, small, 21.0, 301.0, 0.0, "", 10.0, "obj:table"), "surface_mismatch", "'on' that does not match the support is refused")
	_expect_code(surface_host.preflight(PLAYER, small, 21.0, 301.0, 0.0, "", null, "obj:ghost"), "surface_target_invalid", "'on' must name something in this room")


func _test_capacity() -> void:
	var host = _fresh("capacity")
	var source := _source()
	for index in range(1, 24):
		var part: Dictionary = source.parts[0].duplicate(true)
		part.id = "part%d" % index
		source.parts.append(part)
	var first_id := ""
	for index in range(4):
		var request := _command(host, "place", "large%d" % index, source)
		request.x_m = float(index) * 5.0
		var result: Dictionary = host.submit(PLAYER, request)
		_expect(result.ok, "aggregate quota admits assembly %d" % index)
		if index == 0:
			first_id = result.get("instance_id", "")
	_expect_code(host.submit(PLAYER, _command(host, "place", "over_quota", _source())), "owner_capacity", "aggregate components cannot bypass quota with multiple objects")
	_expect(host.submit(PLAYER, _command(host, "revise", "replacement", source, first_id)).ok, "replacement subtracts old reservation")
	_expect(host.snapshot(PLAYER).capacity.world.parts == 96, "replacement has no duplicate capacity charge")
	_expect(host.submit(PLAYER, _command(host, "remove", "release", {}, first_id)).ok, "remove releases reservations")
	_expect(host.submit(PLAYER, _command(host, "place", "after_release", _source())).ok, "released capacity reusable")
	var avatars = _fresh("avatars")
	var avatar := _source()
	avatar.mount = "avatar"
	var equipped: Dictionary = avatars.submit(PLAYER, _command(avatars, "place", "equip", avatar))
	_expect(equipped.ok, "avatar invention can equip")
	_expect_code(avatars.submit(PLAYER, _command(avatars, "place", "equip_again", avatar)), "avatar_slot_full", "one avatar invention per owner")
	_expect(avatars.submit(PLAYER, _command(avatars, "revise", "equip_revision", avatar, equipped.instance_id)).ok, "equipped design can be revised")
	var count_host = _fresh("counts")
	for index in range(8):
		_expect(count_host.submit(PLAYER, _command(count_host, "place", "small%d" % index, _source())).ok, "bounded small invention slot %d" % index)
	_expect_code(count_host.submit(PLAYER, _command(count_host, "place", "ninth", _source())), "owner_capacity", "ninth owner instance rejected")
	for index in range(8):
		_expect(count_host.submit(COMPANION, _command(count_host, "place", "companion_small%d" % index, _source())).ok, "second creator slot %d" % index)
	_expect(count_host.snapshot(PLAYER).capacity.world.instances == 16, "room capacity aggregates both creators")
	_expect_code(count_host.submit(COMPANION, _command(count_host, "place", "room_seventeenth", _source())), "world_capacity", "seventeenth room instance rejected")


func _test_runtime() -> void:
	var host = _fresh("runtime")
	host.set_consent(PLAYER, true)
	var placed: Dictionary = host.submit(PLAYER, _command(host, "place", "runtime_thing", _wind_source()))
	var id: String = placed.instance_id
	var activate := _command(host, "activate", "use", {}, id)
	var receipt: Dictionary = host.submit(PLAYER, activate)
	_expect(receipt.ok and receipt.transient and host.revision == 2, "activation does not advance persisted revision")
	_expect(host.submit(PLAYER, activate).replayed, "activation retry marked to prevent duplicate execution")
	for index in range(5):
		_expect(host.consume_runtime_budget(id, PLAYER, 1, 2, 10.0).ok, "bounded runtime activation %d" % index)
	_expect_code(host.consume_runtime_budget(id, PLAYER, 1, 2, 10.0), "runtime_budget", "per-instance burst budget enforced")
	_expect(host.consume_runtime_budget(id, PLAYER, 1, 2, 11.0).ok, "runtime rolling window expires without catch-up")
	_expect_code(host.consume_runtime_budget(id, PLAYER, 1, 2, 10.0), "runtime_cost_invalid", "runtime clock cannot move backward")
	_expect_code(host.consume_runtime_budget(id, PLAYER, -1, 2, 11.1), "runtime_cost_invalid", "negative costs cannot refund budgets")
	host.set_consent(PLAYER, false)
	_expect_code(host.consume_runtime_budget(id, PLAYER, 1, 2, 12.0), "activation_denied", "runtime permission checked at evaluation time")
	var aggregate = _fresh("runtime_aggregate")
	aggregate.set_consent(PLAYER, true)
	var ids: Array[String] = []
	for index in range(3):
		ids.append(aggregate.submit(PLAYER, _command(aggregate, "place", "aggregate%d" % index, _wind_source())).instance_id)
	for index in range(10):
		_expect(aggregate.consume_runtime_budget(ids[index / 5], PLAYER, 0, 2, 1.0).ok, "shared actor allows bounded activation %d" % index)
	_expect_code(aggregate.consume_runtime_budget(ids[2], PLAYER, 0, 2, 1.0), "runtime_budget", "multiple instances cannot multiply actor activation budget")
	aggregate.set_consent(COMPANION, true)
	_expect_code(aggregate.consume_runtime_budget(ids[2], COMPANION, 0, 2, 1.0), "runtime_budget", "companion activation also charges the creator's budget")
	_expect(aggregate.consume_runtime_budget(ids[0], COMPANION, 4, 2, 2.0).ok, "field admission before actor field limit")
	_expect(aggregate.consume_runtime_budget(ids[1], COMPANION, 4, 2, 2.0).ok, "field admission reaches actor field limit")
	_expect_code(aggregate.consume_runtime_budget(ids[2], COMPANION, 1, 2, 2.0), "runtime_budget", "shared actor field budget enforced")
	_expect_code(aggregate.consume_runtime_budget(ids[0], COMPANION, 0, 17, 3.0), "runtime_cost_invalid", "runtime cannot claim more than compiled node cap")


func _test_approval_and_meta() -> void:
	var host = _fresh("approval")
	var placed: Dictionary = host.submit(PLAYER, _command(host, "place", "players_thing", _source()))
	var remove := _command(host, "remove", "companion_removes", {}, placed.instance_id)
	_expect_code(host.submit(COMPANION, remove), "ownership_denied", "without approval the companion cannot remove the player's creation")
	var fingerprint := "cd".repeat(32)
	var approved: Dictionary = host.submit(COMPANION, remove, {"fingerprint": fingerprint, "op": "entity.remove", "at_utc": "2026-10-06T00:05:00Z", "approved_by": PLAYER})
	_expect(approved.ok and not host.snapshot(PLAYER).instances.has(placed.instance_id), "an approved held command commits under the companion's own principal")
	var record: Dictionary = host.receipt_for(COMPANION, "companion_removes")
	_expect(record.fingerprint == fingerprint and record.meta.approved_by == PLAYER and record.meta.op == "entity.remove" and record.principal == COMPANION, "the durable receipt keeps the contract fingerprint, op and approver")
	_expect(host.receipt_for(PLAYER, "companion_removes").is_empty(), "receipts are principal-bound")
	_expect(host.submit(COMPANION, remove, {"fingerprint": fingerprint}).replayed, "the same contract fingerprint replays")
	_expect_code(host.submit(COMPANION, remove, {"fingerprint": "ef".repeat(32)}), "action_id_conflict", "a different contract fingerprint conflicts")
	var direct: Dictionary = host.receipt_for(PLAYER, "players_thing")
	_expect(direct.meta.op == "creation.place" and direct.meta.at_utc.ends_with("Z") and direct.meta.approved_by.is_empty(), "receipts committed without a host still name their contract op and time")
	var second: Dictionary = host.submit(PLAYER, _command(host, "place", "players_second", _source()))
	_expect_code(host.submit(PLAYER, _command(host, "remove", "self_approved", {}, second.instance_id), {"approved_by": PLAYER}), "request_invalid", "the player cannot be recorded as approving their own command")
	_expect_code(host.submit(COMPANION, _command(host, "remove", "bad_meta", {}, second.instance_id), {"approved_by": "companion:local"}), "request_invalid", "only the player can approve")
	_expect_code(host.submit(COMPANION, _command(host, "remove", "bad_meta2", {}, second.instance_id), {"fingerprint": "nothex"}), "request_invalid", "a malformed fingerprint is refused")
	_expect_code(host.submit(COMPANION, _command(host, "remove", "bad_meta3", {}, second.instance_id), {"op": "entity.remove", "trust_me": "yes"}), "request_invalid", "unknown metadata is refused")
	var revise := _command(host, "revise", "approved_revision", _source(), second.instance_id)
	revise.source.name = "Approved rename"
	var revised: Dictionary = host.submit(COMPANION, revise, {"approved_by": PLAYER})
	_expect(revised.ok and host.snapshot(PLAYER).instances[second.instance_id].owner_id == PLAYER, "an approved revision keeps the player as owner")


func _test_load(host, initial: Dictionary, id: String) -> void:
	var path: String = paths[0]
	var restored = Authority.new()
	_expect(restored.configure(ROOM, Callable(self, "_flat"), path).load_required, "existing save requires validated load")
	_expect_code(restored.submit(PLAYER, initial), "save_not_ready", "existing save cannot be overwritten before load")
	_expect(restored.load_saved().ok, "saved source recompiles and validates on load")
	var snapshot: Dictionary = restored.snapshot(PLAYER)
	_expect(snapshot.instances.has(id) and snapshot.instances[id].artifact.hash == host.snapshot(PLAYER).instances[id].artifact.hash, "reload preserves editable compiled identity")
	_expect(not snapshot.consent[PLAYER] and not snapshot.consent[COMPANION], "consent resets on reload")
	_expect(restored.submit(PLAYER, initial).replayed, "persisted retry ledger survives reload and consent reset")
	var old_text := FileAccess.get_file_as_string(path)
	var corrupt := FileAccess.open(path, FileAccess.WRITE)
	corrupt.store_string("{broken")
	corrupt.close()
	_expect_code(restored.load_saved(), "save_invalid", "corrupt JSON refused")
	_expect_code(restored.submit(PLAYER, _command(restored, "place", "after_corrupt", _source())), "save_not_ready", "corrupt load locks subsequent writes")
	_expect(FileAccess.get_file_as_string(path) == "{broken", "corrupt save not overwritten")
	_write(path, old_text.replace("\"revision\":", "\"revision\":0,\"revision\":"))
	_expect_code(restored.load_saved(), "save_invalid", "a save with a duplicate key is refused")
	var incompatible: Dictionary = JSON.parse_string(old_text)
	incompatible.room_pin.manifest_sha256 = "0".repeat(64)
	_write(path, JSON.stringify(incompatible))
	_expect_code(restored.load_saved(), "save_incompatible", "room pin mismatch fails closed")
	var old_version: Dictionary = JSON.parse_string(old_text)
	old_version.version = 1
	_write(path, JSON.stringify(old_version))
	_expect_code(restored.load_saved(), "save_incompatible", "version 1 map-pinned saves are refused, not migrated")
	var malformed: Dictionary = JSON.parse_string(old_text)
	malformed.instances[0].source.parts[0].shape = "unapproved"
	_write(path, JSON.stringify(malformed))
	_expect_code(restored.load_saved(), "save_source_invalid", "saved source always recompiled")
	malformed = JSON.parse_string(old_text)
	malformed.instances[0].position_m[1] += 10.0
	_write(path, JSON.stringify(malformed))
	_expect_code(restored.load_saved(), "save_placement_invalid", "saved forged height refused")
	malformed = JSON.parse_string(old_text)
	malformed.receipts.values()[0].principal = "intruder"
	_write(path, JSON.stringify(malformed))
	_expect_code(restored.load_saved(), "save_receipt_invalid", "saved receipt principal validated")
	malformed = JSON.parse_string(old_text)
	malformed.receipts[PLAYER + "|initial"].meta.approved_by = PLAYER
	_write(path, JSON.stringify(malformed))
	_expect_code(restored.load_saved(), "save_receipt_invalid", "a forged approval on the player's own receipt is refused")
	malformed = JSON.parse_string(old_text)
	malformed.locks = {"obj:ghost": {"locked_by": PLAYER, "locked_revision": 1}}
	_write(path, JSON.stringify(malformed))
	_expect_code(restored.load_saved(), "save_invalid", "a saved lock on something not in the room is refused")
	_write(path, old_text)
	_expect(restored.load_saved().ok, "valid restored file releases load lock")


func _test_persist_failure() -> void:
	var parent_file := _test_path("not_a_directory")
	_write(parent_file, "keep this file")
	var host = Authority.new()
	host.configure(ROOM, Callable(self, "_flat"), parent_file + "/save.json")
	var result: Dictionary = host.submit(PLAYER, _command(host, "place", "cannot_save", _source()))
	_expect(not result.ok and host.revision == 0 and host.snapshot(PLAYER).instances.is_empty(), "failed persistence does not publish candidate state")
	_expect(FileAccess.get_file_as_string(parent_file) == "keep this file", "failed persistence preserves existing file")
	var lock_host = Authority.new()
	lock_host.configure(ROOM, Callable(self, "_flat"), parent_file + "/locks.json")
	_expect(not lock_host.submit(PLAYER, _lock(lock_host, "lock", "cannot_lock", ["obj:table"])).ok and not lock_host.is_locked("obj:table") and lock_host.entity_revision("obj:table") == 0, "a lock that cannot persist is not published")


func _test_occupancy() -> void:
	var host = _fresh("occupancy")
	var calls := [0]
	host.occupancy_query = func(artifact: Dictionary, position: Vector3, yaw: float, replaced: String):
		calls[0] += 1
		_expect(artifact.has("hash") and position == Vector3(0, 10, 350) and yaw == 0.0 and replaced.is_empty(), "trusted occupancy callback receives compiled host placement")
		return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "The companion is standing here."}
	var request := _command(host, "place", "occupancy_place", _source())
	_expect_code(host.submit(PLAYER, request), "avatar_occupied", "live avatar clearance rejects placement at authority boundary")
	_expect(host.revision == 0 and host.snapshot(PLAYER).instances.is_empty() and calls[0] == 1, "occupancy denial publishes no state or receipt")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": true}
	var result: Dictionary = host.submit(PLAYER, request)
	_expect(result.ok, "same uncommitted action can succeed after obstacle clears")
	host.occupancy_query = func(_artifact, _position, _yaw, replaced):
		_expect(replaced == result.instance_id, "revision occupancy callback identifies replaced assembly")
		return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "A body entered the footprint."}
	_expect_code(host.submit(PLAYER, _command(host, "revise", "occupied_revision", _source(), result.instance_id)), "avatar_occupied", "revision also checks live avatar clearance")
	_expect(host.revision == 1 and host.snapshot(PLAYER).instances[result.instance_id].revision == 1, "occupied revision preserves previous assembly")


func _test_preflight() -> void:
	var host = _fresh("preflight")
	var path: String = paths.back()
	var source := _source()
	var original_source := Compiler.canonical_json(source)
	var original_state := Compiler.canonical_json(host.snapshot(PLAYER))
	host.last_error = "preserve_diagnostic"
	var checked: Dictionary = host.preflight(PLAYER, source, 0.0, 350.0, 15.0)
	_expect(checked.ok and checked.position_m == [0.0, 10.0, 350.0] and checked.artifact.hash == Compiler.compile(source).artifact.hash, "preflight returns compiled source and host placement")
	_expect(checked.revision == 0 and checked.permission_revision == 0 and Compiler.canonical_json(host.snapshot(PLAYER)) == original_state, "valid preflight does not mutate revisions or instance state")
	_expect(Compiler.canonical_json(source) == original_source and host.last_error == "preserve_diagnostic", "preflight leaves caller source and authority diagnostic unchanged")
	_expect(not FileAccess.file_exists(path), "preflight never creates a save file")
	checked.artifact.source.name = "altered result"
	_expect(Compiler.canonical_json(source) == original_source and host.snapshot(PLAYER).instances.is_empty(), "preflight result does not alias caller source or live state")
	_expect_code(host.preflight(PLAYER, source, 0.0, 420.0, 0.0), "room_bounds", "preflight rejects placement outside the room before confirmation")
	host.set_role(PLAYER, COMPANION, "visitor")
	_expect_code(host.preflight(COMPANION, source, 0.0, 350.0, 0.0), "build_denied", "preflight uses current build permissions")
	host.set_role(PLAYER, COMPANION, "editor")
	original_state = Compiler.canonical_json(host.snapshot(PLAYER))
	_expect_code(host.preflight("unknown_player", source, 0.0, 350.0, 0.0), "principal_unknown", "preflight does not admit unknown principals")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "A body is here."}
	_expect_code(host.preflight(PLAYER, source, 0.0, 350.0, 0.0), "avatar_occupied", "preflight checks live clearance")
	_expect(Compiler.canonical_json(host.snapshot(PLAYER)) == original_state and host.last_error == "preserve_diagnostic", "failed preflights leave state untouched")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": true}
	_expect(host.preflight(PLAYER, source, 0.0, 350.0, 0.0).ok, "clear candidate passes preflight")
	var command := _command(host, "place", "after_preflight", source)
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "A body entered after testing."}
	_expect_code(host.submit(PLAYER, command), "avatar_occupied", "commit rechecks clearance changed after successful preflight")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": true}
	var committed: Dictionary = host.submit(PLAYER, command)
	_expect(committed.ok and committed.instance_id == "creation:00000001", "preflight reserves neither IDs nor revisions and failed commit consumes no receipt")
	var saved: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	_expect(saved.receipts.size() == 1, "only actual committed action enters receipt ledger")
	_expect_code(host.preflight(COMPANION, source, 0.0, 350.0, 0.0, committed.instance_id), "ownership_denied", "revision preflight rejects editing another creator's source")
	_expect(host.preflight(COMPANION, source, 5.0, 350.0, 0.0).ok, "editor can preflight own new placement")
	host.set_role(PLAYER, COMPANION, "visitor")
	_expect_code(host.submit(COMPANION, _command(host, "place", "after_permission_revoke", source)), "build_denied", "commit rechecks role revoked after successful preflight")
	for index in range(7):
		host.submit(PLAYER, _command(host, "place", "preflight_slot%d" % index, source))
	var before_file := FileAccess.get_file_as_string(path)
	var before_full_state := Compiler.canonical_json(host.snapshot(PLAYER))
	_expect_code(host.preflight(PLAYER, source, 0.0, 350.0, 0.0), "owner_capacity", "preflight enforces aggregate capacity")
	_expect(host.preflight(PLAYER, source, 0.0, 350.0, 0.0, committed.instance_id).ok, "revision preflight subtracts replaced reservation")
	_expect(FileAccess.get_file_as_string(path) == before_file and Compiler.canonical_json(host.snapshot(PLAYER)) == before_full_state, "preflight does not rewrite an existing save or alter its state")


func _fresh(label: String):
	var host = Authority.new()
	var path := _test_path(label)
	_expect(host.configure(ROOM, Callable(self, "_flat"), path).ok, "configure " + label)
	_expect(host.load_saved().ok, "initialize empty " + label)
	return host


func _test_path(label: String) -> String:
	var path := "user://tests/manual_authority_" + label + ".json"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path + ".pending"))
	paths.append(path)
	return path


func _command(host, op: String, action_id: String, source := {}, instance_id := "") -> Dictionary:
	var command := {"op": op, "action_id": action_id, "expected_revision": host.revision, "expected_permission_revision": host.permission_revision}
	if op in ["place", "revise"]:
		command.merge({"source": source.duplicate(true), "x_m": 0.0, "z_m": 350.0, "yaw_deg": 0.0})
	if op != "place":
		command["instance_id"] = instance_id
	return command


func _lock(host, op: String, action_id: String, targets: Array) -> Dictionary:
	return {"op": op, "action_id": action_id, "expected_revision": host.revision, "expected_permission_revision": host.permission_revision, "targets": targets}


func _source() -> Dictionary:
	return {"schema": "enfractal.creation", "version": 1, "name": "Test invention", "seed": 1, "mount": "ground", "parts": [{"id": "base", "shape": "box", "position_m": [0, 0.5, 0], "rotation_deg": [0, 0, 0], "size_m": [1, 1, 1], "material": "stone"}], "nodes": [{"id": "use", "op": "interact", "part_id": "base", "params": {}}], "edges": []}


func _wind_source() -> Dictionary:
	var source := _source()
	source.nodes.append({"id": "lift", "op": "wind", "part_id": "base", "params": {"direction": [0, 1, 0], "acceleration_mps2": 3.0, "radius_m": 3.0, "duration_s": 1.0}})
	source.edges.append({"from": "use", "to": "lift"})
	return source


func _flat(_x: float, _z: float, _from_y: float) -> Dictionary:
	return {"ok": true, "height_m": 10.0, "entity_id": "shell:floor"}


func _write(path: String, text: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(path).get_base_dir())
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(text)
	file.close()


func _expect(value: bool, label: String) -> void:
	checks += 1
	if not value:
		failures += 1
		push_error("Authority check failed: " + label)


func _expect_code(result: Dictionary, code: String, label: String) -> void:
	_expect(not result.get("ok", true) and result.get("code") == code and result.has("path") and result.has("message"), label + ": " + str(result))
