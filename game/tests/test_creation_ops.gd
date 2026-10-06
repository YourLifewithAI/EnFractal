extends SceneTree

const WorldState = preload("res://scripts/world_state.gd")
const CreationOps = preload("res://scripts/creation_ops.gd")
const SAVE_PATH := "user://tests/creation_ops_test.json"
const MALFORMED_SAVE_PATH := "user://tests/creation_ops_malformed_test.json"

var failures := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var manifest = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/flat_room_manifest.json"))
	_expect(manifest is Dictionary, "read flat room fixture manifest")
	if not manifest is Dictionary:
		quit(1)
		return
	var state = WorldState.new()
	_expect(state.initialize("home-test-room-local", "test_flat_room_v0-local", manifest), "initialize world")
	var authority := {
		"actor_id": "local-player",
		"world_id": state.world_id,
		"frame_id": state.frame_id,
		"can_edit": true,
		"plot": {"id": "workshop-01", "owner_id": "local-player", "min_x_m": -60.0, "max_x_m": 60.0, "min_z_m": 290.0, "max_z_m": 410.0},
	}
	var placement := {
		"op": "place_platform", "action_id": "platform-001", "expected_revision": 0,
		"x_m": 0.0, "z_m": 350.0, "yaw_deg": 15.0, "width_m": 3.0, "depth_m": 2.0,
		"owner_id": "forged-owner", "material_role": "unapproved",
	}
	var prepared: Dictionary = CreationOps.prepare(placement, state, authority, Callable(self, "_flat_height"))
	_expect(prepared.get("ok") == true, "valid platform prepared")
	if not prepared.get("ok", false):
		print("Creation operation tests: failed initial preparation: ", prepared)
		quit(1)
		return
	var entity: Dictionary = prepared["command"]["entity"]
	_expect(entity.get("kind") == "stone_platform" and entity.get("owner_id") == "local-player", "host determines semantic kind and owner")
	_expect(entity.get("material_role") == "rock" and entity["provenance"]["classification"] == "player_change", "approved style and authored provenance")
	_expect(entity["params"]["width_m"] == 3.0 and entity["generator"]["version"] == 1, "editable dimensions and generator version retained")
	var first: Dictionary = state.apply_validated_edit(prepared["command"])
	_expect(first.get("ok") == true and first.get("entity_id") == "edit:platform-001", "placement creates stable semantic ID")
	var repeat: Dictionary = CreationOps.prepare(placement, state, authority, Callable(self, "_flat_height"))
	_expect(repeat.get("ok") == true and state.apply_validated_edit(repeat["command"]).get("replayed") == true, "placement retry does not duplicate")
	var entity_id: String = first["entity_id"]
	var revision := placement.duplicate(true)
	revision["op"] = "revise_platform"
	revision["action_id"] = "platform-002"
	revision["expected_revision"] = 1
	revision["entity_id"] = entity_id
	revision["width_m"] = 4.0
	var revised: Dictionary = CreationOps.prepare(revision, state, authority, Callable(self, "_flat_height"))
	_expect(revised.get("ok") == true and state.apply_validated_edit(revised["command"]).get("ok") == true, "revised platform accepted")
	_expect(state.get_entity(entity_id)["params"]["width_m"] == 4.0 and state.get_entity(entity_id)["id"] == entity_id, "revision keeps editable identity")
	_expect(state.save_to_path(SAVE_PATH), "save edited platform")
	var restored = WorldState.new()
	_expect(restored.load_from_path(SAVE_PATH, manifest, state.world_id, state.frame_id), "reload edited platform")
	_expect(restored.get_entity(entity_id)["params"]["width_m"] == 4.0 and restored.get_entity(entity_id)["material_role"] == "rock", "editable platform and style survive reload")
	state = restored
	var plot: Dictionary = authority["plot"]
	var stored: Dictionary = state.get_entity(entity_id)
	var stored_check: Dictionary = CreationOps.validate_stored_entity(stored, state, plot, Callable(self, "_flat_height"))
	_expect(stored_check.get("ok") == true, "valid saved platform is safe to render: " + str(stored_check))
	var broken: Dictionary = stored.duplicate(true)
	broken.erase("transform")
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("ok") == false, "missing transform rejected before rendering")
	broken = stored.duplicate(true)
	broken["params"]["width_m"] = -300.0
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("ok") == false, "invalid collider width rejected")
	broken = stored.duplicate(true)
	broken["params"]["thickness_m"] = INF
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("ok") == false, "nonfinite collider thickness rejected")
	broken = stored.duplicate(true)
	broken["transform"]["y_m"] += 50.0
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "stored_terrain_anchor_mismatch", "displaced terrain anchor rejected")
	broken = stored.duplicate(true)
	broken["transform"]["x_m"] = 200.0
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("ok") == false, "stored entity outside workshop rejected")
	broken = stored.duplicate(true)
	broken["world_id"] = "another-world"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "stored_world_mismatch", "cross-world stored entity rejected")
	broken = stored.duplicate(true)
	broken["frame_id"] = "another-frame"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "stored_world_mismatch", "cross-frame stored entity rejected")
	broken = stored.duplicate(true)
	broken["plot_id"] = "mall"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "stored_plot_mismatch", "cross-plot stored entity rejected")
	broken = stored.duplicate(true)
	broken["provenance"]["author_id"] = "other-player"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "invalid_stored_provenance", "owner/provenance mismatch rejected")
	broken = stored.duplicate(true)
	broken["owner_id"] = "other-player"
	broken["provenance"]["author_id"] = "other-player"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "invalid_stored_owner", "other owner rejected by trusted local plot")
	broken = stored.duplicate(true)
	broken["material_role"] = "glass"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "invalid_stored_roles", "unapproved style role rejected")
	broken = stored.duplicate(true)
	broken["collision_role"] = "none"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "invalid_stored_roles", "unapproved collider role rejected")
	broken = stored.duplicate(true)
	broken["generator"]["version"] = 999
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "unsupported_stored_generator", "unknown generator revision rejected")
	broken = stored.duplicate(true)
	broken["extra_behavior"] = "ignored-by-renderer"
	_expect(CreationOps.validate_stored_entity(broken, state, plot, Callable(self, "_flat_height")).get("error") == "invalid_stored_entity", "unknown stored fields require migration")
	# The generic state format can legally persist arbitrary JSON entities. Verify
	# the creation boundary rejects one after an actual save/load round trip.
	var unvalidated = WorldState.new()
	_expect(unvalidated.initialize(state.world_id, state.frame_id, manifest), "initialize malformed-save fixture")
	var malformed_entity: Dictionary = prepared["command"]["entity"].duplicate(true)
	malformed_entity["params"]["width_m"] = -300.0
	_expect(unvalidated.apply_validated_edit({"op": "place", "action_id": "malformed-001", "expected_revision": 0, "actor_id": "local-player", "entity": malformed_entity}).get("ok") == true, "generic state accepts syntactically valid malformed entity")
	var other_owner_entity: Dictionary = prepared["command"]["entity"].duplicate(true)
	other_owner_entity["owner_id"] = "other-player"
	other_owner_entity["provenance"]["author_id"] = "other-player"
	_expect(unvalidated.apply_validated_edit({"op": "place", "action_id": "other-owner-001", "expected_revision": 1, "actor_id": "other-player", "entity": other_owner_entity}).get("ok") == true, "generic state accepts syntactically valid other-owner entity")
	_expect(unvalidated.save_to_path(MALFORMED_SAVE_PATH), "save malformed fixture")
	var reloaded_malformed = WorldState.new()
	_expect(reloaded_malformed.load_from_path(MALFORMED_SAVE_PATH, manifest, state.world_id, state.frame_id), "generic state reloads malformed fixture")
	_expect(CreationOps.validate_stored_entity(reloaded_malformed.get_entity("edit:malformed-001"), reloaded_malformed, plot, Callable(self, "_flat_height")).get("ok") == false, "creation validator rejects malformed saved geometry")
	_expect(CreationOps.validate_stored_entity(reloaded_malformed.get_entity("edit:other-owner-001"), reloaded_malformed, plot, Callable(self, "_flat_height")).get("error") == "invalid_stored_owner", "creation validator rejects saved other-owner geometry")
	var denied := placement.duplicate(true)
	denied["action_id"] = "platform-outside"
	denied["expected_revision"] = 2
	denied["x_m"] = 145.0
	_expect(CreationOps.prepare(denied, state, authority, Callable(self, "_flat_height")).get("error") == "outside_authorized_plot", "outside plot rejected clearly")
	denied["x_m"] = 0.0
	denied["width_m"] = 20.0
	_expect(CreationOps.prepare(denied, state, authority, Callable(self, "_flat_height")).get("error") == "invalid_platform_size", "oversize platform rejected clearly")
	denied["width_m"] = INF
	_expect(CreationOps.prepare(denied, state, authority, Callable(self, "_flat_height")).get("error") == "invalid_number", "nonfinite dimension rejected")
	denied["width_m"] = 3.0
	_expect(CreationOps.prepare(denied, state, authority, Callable(self, "_steep_height")).get("error") == "terrain_too_steep", "steep ground rejected clearly")
	var other_authority: Dictionary = authority.duplicate(true)
	other_authority["actor_id"] = "other-player"
	var removal := {"op": "remove_platform", "action_id": "platform-003", "expected_revision": 2, "entity_id": entity_id}
	_expect(CreationOps.prepare(removal, state, other_authority, Callable(self, "_flat_height")).get("error") == "authority_denied", "other player cannot use owner-only workshop plot")
	other_authority["actor_id"] = "local-player"
	other_authority["can_edit"] = false
	_expect(CreationOps.prepare(removal, state, other_authority, Callable(self, "_flat_height")).get("error") == "authority_denied", "untrusted edit authority denied")
	var prepared_removal: Dictionary = CreationOps.prepare(removal, state, authority, Callable(self, "_flat_height"))
	_expect(prepared_removal.get("ok") == true and state.apply_validated_edit(prepared_removal["command"]).get("ok") == true, "remove platform")
	_expect(state.get_entity(entity_id).is_empty(), "platform removed from active scene")
	var removal_retry: Dictionary = CreationOps.prepare(removal, state, authority, Callable(self, "_flat_height"))
	_expect(removal_retry.get("ok") == true and state.apply_validated_edit(removal_retry["command"]).get("replayed") == true, "remove retry reaches receipt after entity is gone")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(MALFORMED_SAVE_PATH))
	print("Creation operation tests: ", "PASS" if failures == 0 else "%d failure(s)" % failures)
	quit(0 if failures == 0 else 1)


func _flat_height(_x: float, _z: float) -> float:
	return 210.0


func _steep_height(x: float, _z: float) -> float:
	return x


func _expect(condition: bool, description: String) -> void:
	if condition:
		return
	failures += 1
	push_error("Creation operation test failed: " + description)
