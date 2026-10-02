extends SceneTree

const Authority = preload("res://scripts/creation_authority.gd")
const Compiler = preload("res://scripts/creation_compiler.gd")
const PIN := {"id": "authority-fixture", "sha256": "fixture-pinned-source-v1"}
var failures := 0
var checks := 0
var paths: Array[String] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var host = _fresh("main")
	_expect(not host.snapshot("local_player").consent.local_player and not host.snapshot("local_player").consent.guest_player, "fresh world requires explicit consent from both players")
	var source := _source()
	var initial := _command(host, "place", "initial", source)
	var first: Dictionary = host.submit("local_player", initial)
	_expect(first.get("ok", false), "valid source compiles and persists")
	if not first.get("ok", false):
		print(first)
		quit(1)
		return
	var id: String = first.instance_id
	var snapshot: Dictionary = host.snapshot("local_player")
	_expect(snapshot.instances[id].owner_id == "local_player" and snapshot.instances[id].position_m == [0.0, 10.0, 350.0], "host assigns owner and terrain height")
	_expect(snapshot.instances[id].artifact.hash == Compiler.compile(source).artifact.hash, "host derives compiled artifact")
	var replay: Dictionary = host.submit("local_player", initial)
	_expect(replay.ok and replay.replayed and host.revision == 1, "committed retry precedes revision conflict")
	var altered := initial.duplicate(true)
	altered.source.name = "Changed payload"
	_expect_code(host.submit("local_player", altered), "action_id_conflict", "action ID cannot change content")
	_expect_code(host.submit("intruder", initial), "principal_unknown", "unknown principal cannot read a receipt")
	_expect_code(host.submit("guest_player", initial), "revision_conflict", "other principal never receives owner's receipt")
	for forged_field in ["owner_id", "principal", "height_m", "artifact", "cost", "permissions", "id"]:
		var forged := _command(host, "place", "forged_" + forged_field, source)
		forged[forged_field] = "local_player"
		_expect_code(host.submit("guest_player", forged), "field_unknown", "reject caller field " + forged_field)
	_expect_code(host.submit("guest_player", _command(host, "place", "visitor_place", source)), "build_denied", "visitor cannot build")
	_expect_code(host.set_role("guest_player", "guest_player", "editor"), "role_denied", "guest cannot grant own role")
	_expect(host.set_role("local_player", "guest_player", "editor").ok, "host grants editor role")
	var revise := _command(host, "revise", "guest_revision", source, id)
	_expect_code(host.submit("guest_player", revise), "ownership_denied", "editor cannot revise another creator")
	_expect_code(host.submit("guest_player", _command(host, "remove", "guest_remove", {}, id)), "ownership_denied", "editor cannot remove another creator")
	var guest_request := _command(host, "place", "guest_place", source)
	guest_request.x_m = 5.0
	var guest: Dictionary = host.submit("guest_player", guest_request)
	_expect(guest.ok, "editor places own invention")
	_expect(host.set_consent("guest_player", true).ok, "guest opts into friendly effects")
	_expect(host.can_activate("guest_player", id), "consenting visitor/editor can use public invention")
	_expect(host.can_affect("local_player", "guest_player", Vector3(0, 12, 350)), "authorized effect may reach consenting guest")
	var stale := _command(host, "place", "stale_permissions", source)
	_expect(host.set_role("local_player", "guest_player", "visitor").ok, "host revokes editor role")
	stale.expected_revision = host.revision
	_expect_code(host.submit("guest_player", stale), "permission_revision_conflict", "preview cannot cross permission revision")
	_expect(not host.can_affect("guest_player", "local_player", Vector3(0, 12, 350)), "revoked owner's live effects cease")
	_expect(not host.snapshot("local_player").instances[guest.instance_id].active, "revoked owner's instance becomes inactive")
	var revoked_retry: Dictionary = host.submit("guest_player", guest_request)
	_expect(revoked_retry.ok and revoked_retry.replayed, "committed principal-bound retry survives role revocation")
	_expect(host.set_consent("guest_player", false).ok, "guest revokes consent")
	_expect(not host.can_affect("local_player", "guest_player", Vector3(0, 12, 350)), "revoked consent blocks next effect tick")
	_expect(not host.can_activate("guest_player", id), "revoked consent blocks activation")
	_expect(not host.can_affect("local_player", "local_player", Vector3(0, 12, 385)), "protected garden rejects effects")
	_expect(not host.can_affect("local_player", "local_player", Vector3(61, 12, 350)), "outside plot rejects effects")
	_expect(host.submit("local_player", _command(host, "remove", "moderate_guest", {}, guest.instance_id)).ok, "owner can moderate removed guest object")
	var copied: Dictionary = host.snapshot("local_player")
	copied.instances[id].source.name = "snapshot tamper"
	_expect(host.snapshot("local_player").instances[id].source.name != "snapshot tamper", "snapshot cannot mutate authority")
	_test_bounds()
	_test_capacity()
	_test_runtime()
	_test_load(host, initial, id)
	_test_persist_failure()
	_test_occupancy()
	_test_preflight()
	for path in paths:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path + ".pending"))
	print("Creation authority: %d checks, %d failures" % [checks, failures])
	quit(1 if failures else 0)


func _test_bounds() -> void:
	var host = _fresh("bounds")
	var source := _source()
	var request := _command(host, "place", "beyond_origin", source)
	request.x_m = 59.75
	_expect_code(host.submit("local_player", request), "plot_bounds", "full footprint not only origin stays in plot")
	request = _command(host, "place", "rotated_edge", source)
	request.source.parts[0].size_m = [4.0, 1.0, 1.0]
	request.x_m = 58.5
	request.yaw_deg = 45.0
	_expect_code(host.submit("local_player", request), "plot_bounds", "yaw expands footprint at edge")
	request = _command(host, "place", "rotor_swept_edge", source)
	request.source.parts[0].shape = "rotor"
	request.source.parts[0].size_m = [4.0, 0.1, 0.1]
	request.x_m = 59.0
	request.yaw_deg = 90.0
	_expect_code(host.submit("local_player", request), "plot_bounds", "thin rotor reserves its complete spin sweep beyond narrow resting footprint")
	request = _command(host, "place", "garden_edge", source)
	request.z_m = 379.75
	_expect_code(host.submit("local_player", request), "protected_zone", "assembly boundary cannot cross protected garden")
	request = _command(host, "place", "field_garden", _wind_source())
	request.z_m = 377.5
	_expect_code(host.submit("local_player", request), "protected_zone", "wind radius cannot cross protected garden")
	request = _command(host, "place", "field_plot", _wind_source())
	request.x_m = 58.0
	_expect_code(host.submit("local_player", request), "plot_bounds", "wind envelope cannot leave plot")
	request = _command(host, "place", "source_invalid", source)
	request.source.parts[0].shape = "run_script"
	_expect(not host.submit("local_player", request).ok, "commit recompiles rejected arbitrary source")
	request = _command(host, "place", "nonfinite", source)
	request.x_m = NAN
	_expect_code(host.submit("local_player", request), "request_invalid", "nonfinite values fail before canonicalization")
	request = _command(host, "place", "huge_finite", source)
	request.x_m = 1e300
	_expect_code(host.submit("local_player", request), "plot_bounds", "huge finite coordinates fail before float32 geometry conversion")
	var terrain_path := _test_path("terrain")
	var terrain_host = Authority.new()
	terrain_host.configure(PIN, func(_x, _z): return NAN, terrain_path)
	_expect_code(terrain_host.submit("local_player", _command(terrain_host, "place", "no_terrain", source)), "terrain_unavailable", "unavailable terrain fails closed")
	terrain_host.configure(PIN, func(x, _z): return x * 10.0, terrain_path)
	_expect_code(terrain_host.submit("local_player", _command(terrain_host, "place", "uneven", source)), "terrain_too_uneven", "unstable terrain footing rejected")


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
		var result: Dictionary = host.submit("local_player", request)
		_expect(result.ok, "aggregate quota admits assembly %d" % index)
		if index == 0:
			first_id = result.get("instance_id", "")
	_expect_code(host.submit("local_player", _command(host, "place", "over_quota", _source())), "owner_capacity", "aggregate components cannot bypass quota with multiple objects")
	_expect(host.submit("local_player", _command(host, "revise", "replacement", source, first_id)).ok, "replacement subtracts old reservation")
	_expect(host.snapshot("local_player").capacity.world.parts == 96, "replacement has no duplicate capacity charge")
	_expect(host.submit("local_player", _command(host, "remove", "release", {}, first_id)).ok, "remove releases reservations")
	_expect(host.submit("local_player", _command(host, "place", "after_release", _source())).ok, "released capacity reusable")
	var avatars = _fresh("avatars")
	var avatar := _source()
	avatar.mount = "avatar"
	var equipped: Dictionary = avatars.submit("local_player", _command(avatars, "place", "equip", avatar))
	_expect(equipped.ok, "avatar invention can equip")
	_expect_code(avatars.submit("local_player", _command(avatars, "place", "equip_again", avatar)), "avatar_slot_full", "one avatar invention per owner")
	_expect(avatars.submit("local_player", _command(avatars, "revise", "equip_revision", avatar, equipped.instance_id)).ok, "equipped design can be revised")
	var count_host = _fresh("counts")
	for index in range(8):
		_expect(count_host.submit("local_player", _command(count_host, "place", "small%d" % index, _source())).ok, "bounded small invention slot %d" % index)
	_expect_code(count_host.submit("local_player", _command(count_host, "place", "ninth", _source())), "owner_capacity", "ninth owner instance rejected")
	count_host.set_role("local_player", "guest_player", "editor")
	for index in range(8):
		_expect(count_host.submit("guest_player", _command(count_host, "place", "guest_small%d" % index, _source())).ok, "second owner slot %d" % index)
	_expect(count_host.snapshot("local_player").capacity.world.instances == 16, "world capacity aggregates both owners")
	_expect_code(count_host.submit("guest_player", _command(count_host, "place", "world_seventeenth", _source())), "world_capacity", "seventeenth world instance rejected")


func _test_runtime() -> void:
	var host = _fresh("runtime")
	host.set_consent("local_player", true)
	var placed: Dictionary = host.submit("local_player", _command(host, "place", "runtime_thing", _wind_source()))
	var id: String = placed.instance_id
	var activate := _command(host, "activate", "use", {}, id)
	var receipt: Dictionary = host.submit("local_player", activate)
	_expect(receipt.ok and receipt.transient and host.revision == 2, "activation does not advance persisted revision")
	_expect(host.submit("local_player", activate).replayed, "activation retry marked to prevent duplicate execution")
	for index in range(5):
		_expect(host.consume_runtime_budget(id, "local_player", 1, 2, 10.0).ok, "bounded runtime activation %d" % index)
	_expect_code(host.consume_runtime_budget(id, "local_player", 1, 2, 10.0), "runtime_budget", "per-instance burst budget enforced")
	_expect(host.consume_runtime_budget(id, "local_player", 1, 2, 11.0).ok, "runtime rolling window expires without catch-up")
	_expect_code(host.consume_runtime_budget(id, "local_player", 1, 2, 10.0), "runtime_cost_invalid", "runtime clock cannot move backward")
	_expect_code(host.consume_runtime_budget(id, "local_player", -1, 2, 11.1), "runtime_cost_invalid", "negative costs cannot refund budgets")
	host.set_consent("local_player", false)
	_expect_code(host.consume_runtime_budget(id, "local_player", 1, 2, 12.0), "activation_denied", "runtime permission checked at evaluation time")
	var aggregate = _fresh("runtime_aggregate")
	aggregate.set_consent("local_player", true)
	var ids: Array[String] = []
	for index in range(3):
		ids.append(aggregate.submit("local_player", _command(aggregate, "place", "aggregate%d" % index, _wind_source())).instance_id)
	for index in range(10):
		_expect(aggregate.consume_runtime_budget(ids[index / 5], "local_player", 0, 2, 1.0).ok, "shared actor allows bounded activation %d" % index)
	_expect_code(aggregate.consume_runtime_budget(ids[2], "local_player", 0, 2, 1.0), "runtime_budget", "multiple instances cannot multiply actor activation budget")
	aggregate.set_consent("guest_player", true)
	_expect_code(aggregate.consume_runtime_budget(ids[2], "guest_player", 0, 2, 1.0), "runtime_budget", "guest activation also charges invention owner's budget")
	_expect(aggregate.consume_runtime_budget(ids[0], "guest_player", 4, 2, 2.0).ok, "field admission before actor field limit")
	_expect(aggregate.consume_runtime_budget(ids[1], "guest_player", 4, 2, 2.0).ok, "field admission reaches actor field limit")
	_expect_code(aggregate.consume_runtime_budget(ids[2], "guest_player", 1, 2, 2.0), "runtime_budget", "shared actor field budget enforced")
	_expect_code(aggregate.consume_runtime_budget(ids[0], "guest_player", 0, 17, 3.0), "runtime_cost_invalid", "runtime cannot claim more than compiled node cap")


func _test_load(host, initial: Dictionary, id: String) -> void:
	var path: String = paths[0]
	var restored = Authority.new()
	_expect(restored.configure(PIN, Callable(self, "_flat"), path).load_required, "existing save requires validated load")
	_expect_code(restored.submit("local_player", initial), "save_not_ready", "existing save cannot be overwritten before load")
	_expect(restored.load_saved().ok, "saved source recompiles and validates on load")
	var snapshot: Dictionary = restored.snapshot("local_player")
	_expect(snapshot.instances.has(id) and snapshot.instances[id].artifact.hash == host.snapshot("local_player").instances[id].artifact.hash, "reload preserves editable compiled identity")
	_expect(not snapshot.consent.local_player and not snapshot.consent.guest_player, "consent resets on reload")
	_expect(restored.submit("local_player", initial).replayed, "persisted retry ledger survives reload and consent reset")
	var old_text := FileAccess.get_file_as_string(path)
	var corrupt := FileAccess.open(path, FileAccess.WRITE)
	corrupt.store_string("{broken")
	corrupt.close()
	_expect_code(restored.load_saved(), "save_invalid", "corrupt JSON refused")
	_expect_code(restored.submit("local_player", _command(restored, "place", "after_corrupt", _source())), "save_not_ready", "corrupt load locks subsequent writes")
	_expect(FileAccess.get_file_as_string(path) == "{broken", "corrupt save not overwritten")
	var incompatible: Dictionary = JSON.parse_string(old_text)
	incompatible.base_pin = "wrong-map"
	_write(path, JSON.stringify(incompatible))
	_expect_code(restored.load_saved(), "save_incompatible", "base pin mismatch fails closed")
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
	_write(path, old_text)
	_expect(restored.load_saved().ok, "valid restored file releases load lock")


func _test_persist_failure() -> void:
	var parent_file := _test_path("not_a_directory")
	_write(parent_file, "keep this file")
	var host = Authority.new()
	host.configure(PIN, Callable(self, "_flat"), parent_file + "/save.json")
	var result: Dictionary = host.submit("local_player", _command(host, "place", "cannot_save", _source()))
	_expect(not result.ok and host.revision == 0 and host.snapshot("local_player").instances.is_empty(), "failed persistence does not publish candidate state")
	_expect(FileAccess.get_file_as_string(parent_file) == "keep this file", "failed persistence preserves existing file")


func _test_occupancy() -> void:
	var host = _fresh("occupancy")
	var calls := [0]
	host.occupancy_query = func(artifact: Dictionary, position: Vector3, yaw: float, replaced: String):
		calls[0] += 1
		_expect(artifact.has("hash") and position == Vector3(0, 10, 350) and yaw == 0.0 and replaced.is_empty(), "trusted occupancy callback receives compiled host placement")
		return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "The mannequin is standing here."}
	var request := _command(host, "place", "occupancy_place", _source())
	_expect_code(host.submit("local_player", request), "avatar_occupied", "live avatar clearance rejects placement at authority boundary")
	_expect(host.revision == 0 and host.snapshot("local_player").instances.is_empty() and calls[0] == 1, "occupancy denial publishes no state or receipt")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": true}
	var result: Dictionary = host.submit("local_player", request)
	_expect(result.ok, "same uncommitted action can succeed after obstacle clears")
	host.occupancy_query = func(_artifact, _position, _yaw, replaced):
		_expect(replaced == result.instance_id, "revision occupancy callback identifies replaced assembly")
		return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "A player entered the footprint."}
	_expect_code(host.submit("local_player", _command(host, "revise", "occupied_revision", _source(), result.instance_id)), "avatar_occupied", "revision also checks live avatar clearance")
	_expect(host.revision == 1 and host.snapshot("local_player").instances[result.instance_id].revision == 1, "occupied revision preserves previous assembly")


func _test_preflight() -> void:
	var host = _fresh("preflight")
	var path: String = paths.back()
	var source := _source()
	var original_source := Compiler.canonical_json(source)
	var original_state := Compiler.canonical_json(host.snapshot("local_player"))
	host.last_error = "preserve_diagnostic"
	var checked: Dictionary = host.preflight("local_player", source, 0.0, 350.0, 15.0)
	_expect(checked.ok and checked.position_m == [0.0, 10.0, 350.0] and checked.artifact.hash == Compiler.compile(source).artifact.hash, "preflight returns compiled source and host placement")
	_expect(checked.revision == 0 and checked.permission_revision == 0 and Compiler.canonical_json(host.snapshot("local_player")) == original_state, "valid preflight does not mutate revisions or instance state")
	_expect(Compiler.canonical_json(source) == original_source and host.last_error == "preserve_diagnostic", "preflight leaves caller source and authority diagnostic unchanged")
	_expect(not FileAccess.file_exists(path), "preflight never creates a save file")
	checked.artifact.source.name = "altered result"
	_expect(Compiler.canonical_json(source) == original_source and host.snapshot("local_player").instances.is_empty(), "preflight result does not alias caller source or live state")
	_expect_code(host.preflight("local_player", source, 0.0, 390.0, 0.0), "protected_zone", "preflight rejects protected placement before confirmation")
	_expect_code(host.preflight("guest_player", source, 0.0, 350.0, 0.0), "build_denied", "preflight uses current build permissions")
	_expect_code(host.preflight("unknown_player", source, 0.0, 350.0, 0.0), "principal_unknown", "preflight does not admit unknown principals")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "A player is here."}
	_expect_code(host.preflight("local_player", source, 0.0, 350.0, 0.0), "avatar_occupied", "preflight checks live clearance")
	_expect(Compiler.canonical_json(host.snapshot("local_player")) == original_state and host.last_error == "preserve_diagnostic" and not FileAccess.file_exists(path), "failed preflights leave state and save untouched")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": true}
	_expect(host.preflight("local_player", source, 0.0, 350.0, 0.0).ok, "clear candidate passes preflight")
	var command := _command(host, "place", "after_preflight", source)
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": false, "code": "avatar_occupied", "path": "position", "message": "A player entered after testing."}
	_expect_code(host.submit("local_player", command), "avatar_occupied", "commit rechecks clearance changed after successful preflight")
	host.occupancy_query = func(_artifact, _position, _yaw, _replaced): return {"ok": true}
	var committed: Dictionary = host.submit("local_player", command)
	_expect(committed.ok and committed.instance_id == "creation:00000001" and host.revision == 1, "preflight reserves neither IDs nor revisions and failed commit consumes no receipt")
	var saved: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(path))
	_expect(saved.receipts.size() == 1, "only actual committed action enters receipt ledger")
	host.set_role("local_player", "guest_player", "editor")
	_expect_code(host.preflight("guest_player", source, 0.0, 350.0, 0.0, committed.instance_id), "ownership_denied", "revision preflight rejects editing another creator's source")
	_expect(host.preflight("guest_player", source, 5.0, 350.0, 0.0).ok, "editor can preflight own new placement")
	host.set_role("local_player", "guest_player", "visitor")
	_expect_code(host.submit("guest_player", _command(host, "place", "after_permission_revoke", source)), "build_denied", "commit rechecks role revoked after successful preflight")
	for index in range(7):
		host.submit("local_player", _command(host, "place", "preflight_slot%d" % index, source))
	var before_file := FileAccess.get_file_as_string(path)
	var before_full_state := Compiler.canonical_json(host.snapshot("local_player"))
	_expect_code(host.preflight("local_player", source, 0.0, 350.0, 0.0), "owner_capacity", "preflight enforces aggregate capacity")
	_expect(host.preflight("local_player", source, 0.0, 350.0, 0.0, committed.instance_id).ok, "revision preflight subtracts replaced reservation")
	_expect(FileAccess.get_file_as_string(path) == before_file and Compiler.canonical_json(host.snapshot("local_player")) == before_full_state, "preflight does not rewrite an existing save or alter its state")


func _fresh(label: String):
	var host = Authority.new()
	var path := _test_path(label)
	_expect(host.configure(PIN, Callable(self, "_flat"), path).ok, "configure " + label)
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


func _source() -> Dictionary:
	return {"schema": "enfractal.creation", "version": 1, "name": "Test invention", "seed": 1, "mount": "ground", "parts": [{"id": "base", "shape": "box", "position_m": [0, 0.5, 0], "rotation_deg": [0, 0, 0], "size_m": [1, 1, 1], "material": "stone"}], "nodes": [{"id": "use", "op": "interact", "part_id": "base", "params": {}}], "edges": []}


func _wind_source() -> Dictionary:
	var source := _source()
	source.nodes.append({"id": "lift", "op": "wind", "part_id": "base", "params": {"direction": [0, 1, 0], "acceleration_mps2": 3.0, "radius_m": 3.0, "duration_s": 1.0}})
	source.edges.append({"from": "use", "to": "lift"})
	return source


func _flat(_x: float, _z: float) -> float:
	return 10.0


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
