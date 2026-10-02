extends SceneTree

const SAVE_PATH := "user://tests/workshop_path_integration.json"
const CreationOps = preload("res://scripts/creation_ops.gd")
const Join = preload("res://scripts/path_platform_join.gd")

var failures := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", SAVE_PATH)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH))
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	var workshop = scene.workshop_runtime
	if workshop == null or not workshop.edit_enabled:
		_fail("workshop did not initialize")
		return
	scene._jump_to_view("pin")
	scene._set_walking_mode(true)
	await physics_frame
	workshop._place_in_front()
	if workshop.state.revision != 1 or workshop.platform_nodes.size() != 1:
		_fail("platform could not be placed on the local map")
		return
	var platform_id: String = workshop.selected_entity_id
	var platform_node: StaticBody3D = workshop.platform_nodes[platform_id]
	workshop._place_path_for_selected()
	if workshop.state.revision != 2 or workshop.path_nodes.size() != 1 or workshop.join_nodes.size() != 1:
		_fail("connected path could not be placed: " + workshop.message)
		return
	var path_id: String = workshop.selected_entity_id
	var path: Dictionary = workshop.state.get_entity(path_id)
	var platform: Dictionary = workshop.state.get_entity(platform_id)
	var join_id: String = workshop.join_relations.keys()[0]
	var original_join: Dictionary = workshop.join_relations[join_id].duplicate(true)
	var original_path_node: StaticBody3D = workshop.path_nodes[path_id]
	var original_join_node: StaticBody3D = workshop.join_nodes[join_id]
	_expect(path.get("target_platform_id") == platform_id and path["points"].size() == 2, "path preserves source intent and target ID")
	_expect(path.get("style") == platform.get("style") and path["style"].get("recipe_sha256") == CreationOps.STYLE_SHA256, "source parts pin one style recipe")
	_expect(path.get("join_generator", {}).get("version") == Join.GENERATOR_VERSION and original_join.get("style") == path.get("style"), "derived join retains source style and generator pin")
	_expect(Join.generate(path, platform)["relationship"] == original_join, "runtime relation comes from pinned generator")
	var wrong_style := path.duplicate(true)
	wrong_style["style"]["recipe_sha256"] = "0".repeat(64)
	_expect(CreationOps.validate_stored_entity(wrong_style, workshop.state, workshop.PLOT, Callable(scene.map_runtime, "surface_height_at")).get("error") == "unsupported_stored_style", "tampered style pin is rejected")
	var wrong_join_generator := path.duplicate(true)
	wrong_join_generator["join_generator"]["version"] = 999
	_expect(CreationOps.validate_stored_entity(wrong_join_generator, workshop.state, workshop.PLOT, Callable(scene.map_runtime, "surface_height_at")).get("error") == "unsupported_stored_join_generator", "unknown join generator is rejected")
	_expect(not workshop.state.get_snapshot().has("relationships"), "derived join is not stored as an independent edit")
	var denied := {"op": "revise_path", "action_id": "denied_path_edit", "expected_revision": 2,
		"entity_id": path_id, "target_platform_id": platform_id, "width_m": path["width_m"],
		"start_x_m": path["points"][0]["x_m"], "start_z_m": path["points"][0]["z_m"],
		"end_x_m": path["points"][1]["x_m"], "end_z_m": path["points"][1]["z_m"]}
	var no_edit: Dictionary = workshop._authority()
	no_edit["can_edit"] = false
	_expect(CreationOps.prepare(denied, workshop.state, no_edit, Callable(scene.map_runtime, "surface_height_at")).get("error") == "authority_denied", "local permission gate rejects path edit")
	var malformed := path.duplicate(true)
	malformed["points"][1]["y_m"] += 2.0
	_expect(CreationOps.validate_stored_entity(malformed, workshop.state, workshop.PLOT, Callable(scene.map_runtime, "surface_height_at")).get("error") == "stored_terrain_anchor_mismatch", "saved path cannot move vertically without validation")
	workshop._move_path_endpoint()
	if workshop.state.revision != 3:
		_fail("path endpoint could not move: " + workshop.message)
		return
	var revised_path: Dictionary = workshop.state.get_entity(path_id)
	var revised_join: Dictionary = workshop.join_relations[join_id]
	_expect(workshop.platform_nodes[platform_id] == platform_node, "unchanged platform node survives path edit")
	_expect(workshop.path_nodes[path_id] != original_path_node and workshop.join_nodes[join_id] != original_join_node, "only path and join nodes regenerate")
	_expect(revised_path["id"] == path_id and revised_join["id"] == join_id, "source and relationship IDs remain stable")
	_expect(revised_path["points"][1] != path["points"][1] and revised_join["params"]["start"] != original_join["params"]["start"], "moved endpoint updates join geometry")
	_expect(FileAccess.file_exists(SAVE_PATH), "source edits are saved")
	workshop._reload()
	_expect(workshop.state.revision == 3 and _same_semantics(workshop.state.get_entity(path_id), revised_path), "reload retains source path edit: " + workshop.message)
	_expect(_same_semantics(workshop.join_relations[join_id], revised_join) and workshop.platform_nodes[platform_id] == platform_node, "reload regenerates same join without replacing nearby work: " + workshop.message)
	await physics_frame
	var start: Dictionary = revised_join["params"]["start"]
	var end: Dictionary = revised_join["params"]["end"]
	var midpoint := Vector3((float(start["x_m"]) + float(end["x_m"])) * 0.5, (float(start["y_m"]) + float(end["y_m"])) * 0.5, (float(start["z_m"]) + float(end["z_m"])) * 0.5)
	var query := PhysicsRayQueryParameters3D.create(midpoint + Vector3.UP * 2.0, midpoint - Vector3.UP * 2.0)
	query.exclude = [scene.player_body.get_rid()]
	var hit: Dictionary = scene.get_world_3d().direct_space_state.intersect_ray(query)
	_expect(hit.get("collider") == workshop.join_nodes[join_id], "join has a walkable top collision surface")
	workshop.selected_entity_id = platform_id
	workshop._remove_selected()
	_expect(workshop.state.revision == 3 and workshop.message.contains("platform_has_connected_path"), "connected platform cannot be removed before its path")
	workshop.selected_entity_id = path_id
	workshop._remove_selected()
	_expect(workshop.state.revision == 4 and workshop.path_nodes.is_empty() and workshop.join_nodes.is_empty() and workshop.platform_nodes[platform_id] == platform_node, "removing path removes derived join but retains platform")
	workshop._remove_selected()
	_expect(workshop.state.revision == 5 and workshop.platform_nodes.is_empty(), "platform can be removed after its path")
	scene._set_walking_mode(false)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH))
	print("Workshop path integration: ", "PASS" if failures == 0 else "%d failure(s)" % failures)
	quit(0 if failures == 0 else 1)


func _expect(condition: bool, description: String) -> void:
	if condition:
		return
	failures += 1
	push_error("Workshop path integration: " + description)


func _same_semantics(left: Variant, right: Variant) -> bool:
	if left is Dictionary and right is Dictionary:
		if left.size() != right.size():
			return false
		for key in left:
			if not right.has(key) or not _same_semantics(left[key], right[key]):
				return false
		return true
	if left is Array and right is Array:
		if left.size() != right.size():
			return false
		for index in range(left.size()):
			if not _same_semantics(left[index], right[index]):
				return false
		return true
	if (typeof(left) in [TYPE_FLOAT, TYPE_INT]) and (typeof(right) in [TYPE_FLOAT, TYPE_INT]):
		return is_finite(float(left)) and is_finite(float(right)) and absf(float(left) - float(right)) <= 0.000001
	return left == right


func _fail(description: String) -> void:
	push_error("Workshop path integration: " + description)
	quit(1)
