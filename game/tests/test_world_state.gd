extends SceneTree

const WorldState = preload("res://scripts/world_state.gd")
const SAVE_PATH := "user://tests/world_state_test.json"
const BROKEN_PATH := "user://tests/world_state_broken.json"

var failures := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var manifest = JSON.parse_string(FileAccess.get_file_as_string("res://maps/barton_creek/manifest.json"))
	_expect(manifest is Dictionary, "read Barton Creek manifest")
	if not manifest is Dictionary:
		quit(1)
		return
	var state = WorldState.new()
	_expect(state.initialize("home-barton-creek-local", "barton_creek_v0-local", manifest), "initialize pinned world")
	var entity := {
		"kind": "platform",
		"plot_id": "workshop-01",
		"owner_id": "local-player",
		"provenance": "player-authored",
		"generator_version": 1,
		"transform": {"x_m": 12.0, "y_m": 1.0, "z_m": 18.0, "yaw_deg": 0.0},
		"params": {"width_m": 3.0, "depth_m": 4.0, "thickness_m": 0.3},
	}
	var placement := {"action_id": "action-001", "expected_revision": 0, "op": "place", "actor_id": "local-player", "entity": entity}
	var first: Dictionary = state.apply_validated_edit(placement)
	_expect(first.get("ok") == true and first.get("entity_id") == "edit:action-001" and state.revision == 1, "place gets stable ID and revision")
	var replay: Dictionary = state.apply_validated_edit(placement)
	_expect(replay.get("ok") == true and replay.get("replayed") == true and state.revision == 1, "place retry is idempotent")
	var conflict := placement.duplicate(true)
	conflict["entity"]["params"]["width_m"] = 9.0
	_expect(state.apply_validated_edit(conflict).get("error") == "action_id_conflict", "same action ID cannot change intent")
	var stale := {"action_id": "action-stale", "expected_revision": 0, "op": "remove", "actor_id": "local-player", "entity_id": "edit:action-001"}
	_expect(state.apply_validated_edit(stale).get("error") == "revision_conflict", "stale edit rejected")
	var updated_entity := entity.duplicate(true)
	updated_entity["params"]["width_m"] = 5.0
	var revision := {"action_id": "action-002", "expected_revision": 1, "op": "update", "actor_id": "local-player", "entity_id": "edit:action-001", "entity": updated_entity}
	_expect(state.apply_validated_edit(revision).get("ok") == true and state.get_entity("edit:action-001")["params"]["width_m"] == 5.0, "update retains stable ID")
	var copy := state.get_entity("edit:action-001")
	copy["params"]["width_m"] = 20.0
	_expect(state.get_entity("edit:action-001")["params"]["width_m"] == 5.0, "read does not expose mutable state")
	_expect(state.save_to_path(SAVE_PATH), "first snapshot save")
	var removal := {"action_id": "action-003", "expected_revision": 2, "op": "remove", "actor_id": "local-player", "entity_id": "edit:action-001"}
	_expect(state.apply_validated_edit(removal).get("ok") == true and state.get_entity("edit:action-001").is_empty(), "remove persists a tombstone receipt")
	_expect(state.apply_validated_edit(removal).get("replayed") == true and state.revision == 3, "remove retry remains idempotent")
	_expect(state.save_to_path(SAVE_PATH), "atomic replacement of existing snapshot")
	var restored = WorldState.new()
	var loaded: bool = restored.load_from_path(SAVE_PATH, manifest, "home-barton-creek-local", "barton_creek_v0-local")
	if not loaded:
		print("Reload failed: ", restored.last_error)
	_expect(loaded, "reload saved world")
	_expect(restored.revision == 3 and restored.get_entities().is_empty() and restored.has_action("action-003"), "edits and receipts survive reload")
	_expect(restored.apply_validated_edit(removal).get("replayed") == true, "idempotency survives reload")
	var wrong_map: Dictionary = manifest.duplicate(true)
	wrong_map["features_sha256"] = "0".repeat(64)
	_expect(not restored.load_from_path(SAVE_PATH, wrong_map), "wrong base map rejected")
	_expect(restored.revision == 3, "failed load preserves live state")
	var wrong_spatial: Dictionary = manifest.duplicate(true)
	wrong_spatial["height_origin_m"] = float(wrong_spatial["height_origin_m"]) + 1.0
	_expect(not restored.load_from_path(SAVE_PATH, wrong_spatial) and restored.last_error == "base_map_mismatch", "changed height decoder rejected despite identical source bytes")
	wrong_spatial = manifest.duplicate(true)
	wrong_spatial["center_projected_m"][0] = float(wrong_spatial["center_projected_m"][0]) + 10.0
	_expect(not restored.load_from_path(SAVE_PATH, wrong_spatial) and restored.last_error == "base_map_mismatch", "changed local origin rejected despite identical source bytes")
	_expect(restored.revision == 3, "spatial mismatch preserves live state")
	_expect(not restored.load_from_path(SAVE_PATH, manifest, "other-world"), "cross-world load rejected")
	_expect(not restored.save_to_path("user://../escape.json"), "path traversal rejected")
	var old_snapshot: Dictionary = state.get_snapshot()
	old_snapshot["version"] = 1
	var old_file := FileAccess.open(BROKEN_PATH, FileAccess.WRITE)
	if old_file != null:
		old_file.store_string(JSON.stringify(old_snapshot))
		old_file.close()
	_expect(not restored.load_from_path(BROKEN_PATH, manifest) and restored.last_error == "unsupported_save_version", "unmigrated spatial-unpinned schema rejected")
	var bad := FileAccess.open(BROKEN_PATH, FileAccess.WRITE)
	if bad != null:
		bad.store_string("{not json")
		bad.close()
	_expect(not restored.load_from_path(BROKEN_PATH, manifest), "corrupt save rejected")
	_expect(restored.revision == 3, "corrupt save does not mutate live state")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(BROKEN_PATH))
	print("World state tests: ", "PASS" if failures == 0 else "%d failure(s)" % failures)
	quit(0 if failures == 0 else 1)


func _expect(condition: bool, description: String) -> void:
	if condition:
		return
	failures += 1
	push_error("World state test failed: " + description)
