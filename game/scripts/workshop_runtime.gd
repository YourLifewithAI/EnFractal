extends Node3D
## Local construction fixture over the immutable map. This is deliberately not
## Home Earth authority: the caller supplies one developer-only plot and actor.

const WORLD_STATE_SCRIPT := preload("res://scripts/world_state.gd")
const CREATION_OPS_SCRIPT := preload("res://scripts/creation_ops.gd")
const PATH_PLATFORM_JOIN_SCRIPT := preload("res://scripts/path_platform_join.gd")
const ART_DRESSING_SCRIPT := preload("res://scripts/painterly_workshop.gd")
const GROUND_KIT_SCRIPT := preload("res://scripts/painterly_ground_kit.gd")
const SAVE_PATH := "user://worlds/barton_local_workshop.json"
const WORLD_ID := "local_barton_workshop"
const FRAME_ID := "barton_creek_v0_local"
const ACTOR_ID := "local_player"
const PLOT := {"id": "workshop_01", "owner_id": ACTOR_ID, "min_x_m": -60.0, "max_x_m": 60.0, "min_z_m": 290.0, "max_z_m": 410.0}

var viewer: Node3D
var state
var save_path := SAVE_PATH
var edit_enabled := false
var selected_entity_id := ""
var platform_nodes: Dictionary = {}
var path_nodes: Dictionary = {}
var join_nodes: Dictionary = {}
var join_relations: Dictionary = {}
var visual_fingerprints: Dictionary = {}
var art_dressing: Node3D
var _painted_stone: Material
var hint: Label
var message := ""


func configure(map_viewer: Node3D) -> void:
	viewer = map_viewer


func _ready() -> void:
	if viewer == null:
		push_error("Workshop runtime needs its map viewer")
		return
	var test_save := OS.get_environment("ENFRACTAL_WORKSHOP_TEST_SAVE")
	if test_save.begins_with("user://tests/"):
		save_path = test_save
	state = WORLD_STATE_SCRIPT.new()
	# Save/travel worlds share only geography and authored dressing. The earlier
	# local path fixture remains in its own save and cannot leak into a sandbox.
	var isolated_travel := OS.get_environment("ENFRACTAL_SAVE_TRAVEL") == "1"
	if FileAccess.file_exists(save_path) and not isolated_travel:
		if not state.load_from_path(save_path, viewer.manifest, WORLD_ID, FRAME_ID):
			message = "Saved workshop could not load: " + state.last_error
			edit_enabled = false # Never overwrite a rejected or corrupt save.
		else:
			var check := _validate_state(state)
			edit_enabled = check.get("ok", false)
			message = "Local workshop restored" if edit_enabled else "Saved workshop rejected: " + str(check.get("error", "invalid_entity"))
	else:
		edit_enabled = state.initialize(WORLD_ID, FRAME_ID, viewer.manifest)
		message = "Local workshop ready" if edit_enabled else "Local workshop unavailable"
	if isolated_travel:
		edit_enabled = false
	if edit_enabled:
		_restore_selection()
		_render_entities()
	art_dressing = ART_DRESSING_SCRIPT.new()
	art_dressing.name = "IllustrativePlotDressing"
	art_dressing.configure(viewer)
	add_child(art_dressing)
	_setup_hint()


func _setup_hint() -> void:
	var canvas := CanvasLayer.new()
	add_child(canvas)
	hint = Label.new()
	hint.position = Vector2(18, 112)
	hint.add_theme_font_size_override("font_size", 15)
	hint.add_theme_color_override("font_color", Color(0.98, 0.96, 0.88))
	hint.add_theme_color_override("font_shadow_color", Color(0.06, 0.11, 0.09))
	hint.add_theme_constant_override("shadow_offset_x", 1)
	hint.add_theme_constant_override("shadow_offset_y", 1)
	canvas.add_child(hint)
	_update_hint()


func _process(_delta: float) -> void:
	if hint != null:
		hint.visible = viewer.walking


func _unhandled_key_input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed or event.echo or not viewer.walking:
		return
	match event.keycode:
		KEY_P:
			_place_in_front()
		KEY_E:
			_revise_selected()
		KEY_J:
			_place_path_for_selected()
		KEY_K:
			_move_path_endpoint()
		KEY_O:
			_remove_selected()
		KEY_F5:
			_save()
		KEY_F9:
			_reload()
		_:
			return
	get_viewport().set_input_as_handled()


func _authority() -> Dictionary:
	return {"actor_id": ACTOR_ID, "world_id": state.world_id, "frame_id": state.frame_id, "can_edit": edit_enabled, "plot": PLOT}


func _validate_state(candidate) -> Dictionary:
	var platform_count := 0
	var path_count := 0
	for entity in candidate.get_entities().values():
		var result: Dictionary = CREATION_OPS_SCRIPT.validate_stored_entity(entity, candidate, PLOT, Callable(viewer.map_runtime, "surface_height_at"))
		if not result.get("ok", false):
			return result
		if entity.get("kind") == "stone_platform":
			platform_count += 1
		elif entity.get("kind") == "stone_path":
			path_count += 1
	if platform_count > CREATION_OPS_SCRIPT.MAX_PLATFORM_COUNT or path_count > CREATION_OPS_SCRIPT.MAX_PATH_COUNT:
		return {"ok": false, "error": "workshop_entity_limit"}
	return {"ok": true}


func _new_action_id() -> String:
	return Crypto.new().generate_random_bytes(16).hex_encode()


func _place_in_front() -> void:
	if not edit_enabled:
		_update_hint()
		return
	if not platform_nodes.is_empty():
		message = "Resize or remove the test platform before placing another"
		_update_hint()
		return
	var player: CharacterBody3D = viewer.player_body
	var forward := -player.global_basis.z
	var x := player.global_position.x + forward.x * 4.0
	var z := player.global_position.z + forward.z * 4.0
	_submit({
		"op": "place_platform", "action_id": _new_action_id(), "expected_revision": state.revision,
		"x_m": x, "z_m": z, "yaw_deg": rad_to_deg(player.rotation.y),
		"width_m": 3.0, "depth_m": 2.0,
	})


func _revise_selected() -> void:
	var entity: Dictionary = state.get_entity(selected_entity_id)
	if entity.is_empty() or entity.get("kind") != "stone_platform":
		message = "Select a platform by placing one first"
		_update_hint()
		return
	var transform: Dictionary = entity["transform"]
	var params: Dictionary = entity["params"]
	var next_width := float(params["width_m"]) + 1.0
	if next_width > 6.0:
		next_width = 2.0
	_submit({
		"op": "revise_platform", "action_id": _new_action_id(), "expected_revision": state.revision,
		"entity_id": selected_entity_id,
		"x_m": float(transform["x_m"]), "z_m": float(transform["z_m"]),
		"yaw_deg": float(transform["yaw_deg"]), "width_m": next_width,
		"depth_m": float(params["depth_m"]),
	})


func _place_path_for_selected() -> void:
	var platform: Dictionary = state.get_entity(selected_entity_id)
	if platform.get("kind") != "stone_platform":
		for candidate in state.get_entities().values():
			if candidate.get("kind") == "stone_platform":
				platform = candidate
				break
	if platform.get("kind") != "stone_platform":
		message = "Place a platform before adding its path"
		_update_hint()
		return
	for candidate in state.get_entities().values():
		if candidate.get("kind") == "stone_path" and candidate.get("target_platform_id") == platform["id"]:
			message = "Move or remove the connected path before adding another"
			_update_hint()
			return
	var near := _platform_north_point(platform, 2.2)
	var far := _platform_north_point(platform, 5.2)
	_submit({
		"op": "place_path", "action_id": _new_action_id(), "expected_revision": state.revision,
		"target_platform_id": platform["id"], "width_m": 1.2,
		"start_x_m": far.x, "start_z_m": far.y,
		"end_x_m": near.x, "end_z_m": near.y,
	})


func _move_path_endpoint() -> void:
	var path: Dictionary = state.get_entity(selected_entity_id)
	if path.get("kind") != "stone_path":
		for candidate in state.get_entities().values():
			if candidate.get("kind") == "stone_path":
				path = candidate
				break
	if path.get("kind") != "stone_path":
		message = "Add a path before moving its endpoint"
		_update_hint()
		return
	var platform: Dictionary = state.get_entity(str(path["target_platform_id"]))
	if platform.get("kind") != "stone_platform":
		message = "Connected platform is missing"
		_update_hint()
		return
	var near := _platform_north_point(platform, 2.2)
	var far := _platform_north_point(platform, 2.7)
	var current := Vector2(float(path["points"][1]["x_m"]), float(path["points"][1]["z_m"]))
	var moved := far if current.distance_to(near) < 0.2 else near
	_submit({
		"op": "revise_path", "action_id": _new_action_id(), "expected_revision": state.revision,
		"entity_id": path["id"], "target_platform_id": platform["id"], "width_m": float(path["width_m"]),
		"start_x_m": float(path["points"][0]["x_m"]), "start_z_m": float(path["points"][0]["z_m"]),
		"end_x_m": moved.x, "end_z_m": moved.y,
	})


func _platform_north_point(platform: Dictionary, run_m: float) -> Vector2:
	var transform: Dictionary = platform["transform"]
	var local_z := -float(platform["params"]["depth_m"]) * 0.5 - run_m
	var theta := deg_to_rad(float(transform["yaw_deg"]))
	return Vector2(float(transform["x_m"]) + local_z * sin(theta), float(transform["z_m"]) + local_z * cos(theta))


func _remove_selected() -> void:
	var entity: Dictionary = state.get_entity(selected_entity_id)
	if entity.is_empty():
		message = "There is no selected platform"
		_update_hint()
		return
	var operation := "remove_path" if entity.get("kind") == "stone_path" else "remove_platform"
	_submit({"op": operation, "action_id": _new_action_id(), "expected_revision": state.revision, "entity_id": selected_entity_id})


func _submit(intent: Dictionary) -> void:
	if not edit_enabled:
		_update_hint()
		return
	var prepared: Dictionary = CREATION_OPS_SCRIPT.prepare(intent, state, _authority(), Callable(viewer.map_runtime, "surface_height_at"))
	if not prepared.get("ok", false):
		message = "Edit refused: " + str(prepared.get("error", "unknown"))
		_update_hint()
		return
	var receipt: Dictionary = state.apply_validated_edit(prepared["command"])
	if not receipt.get("ok", false):
		message = "Edit refused: " + str(receipt.get("error", "unknown"))
		_update_hint()
		return
	selected_entity_id = str(receipt["entity_id"])
	if intent["op"] in ["remove_platform", "remove_path"]:
		selected_entity_id = ""
		_restore_selection()
	_render_entities()
	message = "Workshop %s • revision %d" % [intent["op"], state.revision]
	if not state.save_to_path(save_path):
		message += " • save failed: " + state.last_error
	_update_hint()


func _save() -> void:
	if not edit_enabled:
		_update_hint()
		return
	message = "Workshop saved" if state.save_to_path(save_path) else "Save failed: " + state.last_error
	_update_hint()


func _reload() -> void:
	if not FileAccess.file_exists(save_path):
		message = "No saved workshop yet"
		_update_hint()
		return
	var replacement = WORLD_STATE_SCRIPT.new()
	if not replacement.load_from_path(save_path, viewer.manifest, WORLD_ID, FRAME_ID):
		message = "Reload failed: " + replacement.last_error
		_update_hint()
		return
	var check := _validate_state(replacement)
	if not check.get("ok", false):
		message = "Reload rejected: " + str(check.get("error", "invalid_entity"))
		_update_hint()
		return
	state = replacement
	edit_enabled = true
	_restore_selection()
	_render_entities()
	message = "Workshop reloaded • revision %d" % state.revision
	_update_hint()


func _restore_selection() -> void:
	selected_entity_id = ""
	var entities: Dictionary = state.get_entities()
	var ids: Array = entities.keys()
	ids.sort()
	for entity_id in ids:
		if entities[entity_id].get("kind") == "stone_platform":
			selected_entity_id = entity_id
	for entity_id in ids:
		if entities[entity_id].get("kind") == "stone_path":
			selected_entity_id = entity_id


func _render_entities() -> void:
	var check := _validate_state(state)
	if not check.get("ok", false):
		edit_enabled = false
		message = "Workshop object rejected: " + str(check.get("error", "invalid_entity"))
		_update_hint()
		return
	var entities: Dictionary = state.get_entities()
	for id in platform_nodes.keys():
		if not entities.has(id) or entities[id].get("kind") != "stone_platform":
			_remove_visual(platform_nodes[id])
			platform_nodes.erase(id)
			visual_fingerprints.erase(id)
	for id in path_nodes.keys():
		if not entities.has(id) or entities[id].get("kind") != "stone_path":
			_remove_visual(path_nodes[id])
			path_nodes.erase(id)
			visual_fingerprints.erase(id)
	for id in entities:
		var entity: Dictionary = entities[id]
		var fingerprint := JSON.stringify(entity, "", true).sha256_text()
		if entity["kind"] == "stone_platform":
			if not platform_nodes.has(id) or visual_fingerprints.get(id, "") != fingerprint:
				if platform_nodes.has(id):
					_remove_visual(platform_nodes[id])
				var platform_body := _build_platform_body(entity)
				add_child(platform_body)
				platform_nodes[id] = platform_body
				visual_fingerprints[id] = fingerprint
		elif entity["kind"] == "stone_path":
			if not path_nodes.has(id) or visual_fingerprints.get(id, "") != fingerprint:
				if path_nodes.has(id):
					_remove_visual(path_nodes[id])
				var path_body := _build_path_body(entity)
				add_child(path_body)
				path_nodes[id] = path_body
				visual_fingerprints[id] = fingerprint
	var next_relations := {}
	for entity in entities.values():
		if entity.get("kind") == "stone_path":
			var joined: Dictionary = PATH_PLATFORM_JOIN_SCRIPT.generate(entity, entities[entity["target_platform_id"]])
			var relation: Dictionary = joined["relationship"]
			next_relations[relation["id"]] = relation
	for id in join_nodes.keys():
		if not next_relations.has(id):
			_remove_visual(join_nodes[id])
			join_nodes.erase(id)
			visual_fingerprints.erase(id)
	for id in next_relations:
		var relation: Dictionary = next_relations[id]
		var fingerprint := JSON.stringify(relation, "", true).sha256_text()
		if not join_nodes.has(id) or visual_fingerprints.get(id, "") != fingerprint:
			if join_nodes.has(id):
				_remove_visual(join_nodes[id])
			var join_body := _build_join_body(relation)
			add_child(join_body)
			join_nodes[id] = join_body
			visual_fingerprints[id] = fingerprint
	join_relations = next_relations
	_refresh_terrain_edit_masks()


func _refresh_terrain_edit_masks() -> void:
	# These are visual contact masks on the same geographic frame as the map;
	# authority and collision remain in the structured source parts above.
	if viewer.terrain_material == null:
		return
	var path_a := Vector4.ZERO
	var path_b := Vector4.ZERO
	var path_width := 0.0
	var platform_mask := Vector3.ZERO
	var entities: Dictionary = state.get_entities()
	var ids: Array = entities.keys()
	ids.sort()
	for id in ids:
		var entity: Dictionary = entities[id]
		if entity.get("kind") == "stone_platform":
			var transform: Dictionary = entity["transform"]
			var params: Dictionary = entity["params"]
			var radius := Vector2(float(params["width_m"]), float(params["depth_m"])).length() * 0.5 + 0.38
			platform_mask = Vector3(float(transform["x_m"]), float(transform["z_m"]), radius)
		elif entity.get("kind") == "stone_path":
			var points: Array = entity["points"]
			path_a = Vector4(float(points[0]["x_m"]), float(points[0]["z_m"]), float(points[1]["x_m"]), float(points[1]["z_m"]))
			path_width = float(entity["width_m"]) + 0.94
	for relation in join_relations.values():
		var params: Dictionary = relation["params"]
		var start: Dictionary = params["start"]
		var end: Dictionary = params["end"]
		path_b = Vector4(float(start["x_m"]), float(start["z_m"]), float(end["x_m"]), float(end["z_m"]))
		break
	viewer.terrain_material.set_shader_parameter("edit_path_a", path_a)
	viewer.terrain_material.set_shader_parameter("edit_path_b", path_b)
	viewer.terrain_material.set_shader_parameter("edit_path_width", path_width)
	viewer.terrain_material.set_shader_parameter("edit_platform", platform_mask)


func _remove_visual(node: Node3D) -> void:
	if node.get_parent() != null:
		node.get_parent().remove_child(node)
	node.queue_free()


func _build_platform_body(entity: Dictionary) -> StaticBody3D:
	var transform: Dictionary = entity["transform"]
	var params: Dictionary = entity["params"]
	var size := Vector3(float(params["width_m"]), float(params["thickness_m"]), float(params["depth_m"]))
	var body := StaticBody3D.new()
	body.name = entity["id"]
	body.collision_layer = 1
	body.collision_mask = 0
	body.position = Vector3(float(transform["x_m"]), float(transform["y_m"]), float(transform["z_m"]))
	body.rotation.y = deg_to_rad(float(transform["yaw_deg"]))
	var shape := BoxShape3D.new()
	shape.size = size
	var collider := CollisionShape3D.new()
	collider.shape = shape
	body.add_child(collider)
	var mesh := MeshInstance3D.new()
	mesh.mesh = _stone_slab_mesh(size)
	mesh.material_override = _stone_material()
	body.add_child(mesh)
	_attach_platform_soil_apron(body, size)
	_attach_stone_seams(body, size.z, size.x, size.y)
	return body


func _build_path_body(entity: Dictionary) -> StaticBody3D:
	var points: Array = entity["points"]
	var from_point := _point_vector(points[0])
	var to_point := _point_vector(points[1])
	var body := _build_walkable_segment(str(entity["id"]), from_point, to_point, float(entity["width_m"]))
	_attach_soil_apron(body, from_point, to_point, float(entity["width_m"]))
	_attach_stone_seams(body, from_point.distance_to(to_point), float(entity["width_m"]), CREATION_OPS_SCRIPT.PATH_THICKNESS_M)
	return body


func _build_join_body(relation: Dictionary) -> StaticBody3D:
	var params: Dictionary = relation["params"]
	var from_point := _point_vector(params["start"])
	var to_point := _point_vector(params["end"])
	var width_m := float(params["width_m"])
	var body := _build_walkable_segment(str(relation["id"]), from_point, to_point, width_m)
	_attach_soil_apron(body, from_point, to_point, width_m)
	_attach_stone_seams(body, from_point.distance_to(to_point), width_m, CREATION_OPS_SCRIPT.PATH_THICKNESS_M)
	return body


func _point_vector(point: Dictionary) -> Vector3:
	return Vector3(float(point["x_m"]), float(point["y_m"]), float(point["z_m"]))


func _build_walkable_segment(id: String, from_point: Vector3, to_point: Vector3, width_m: float) -> StaticBody3D:
	var direction := to_point - from_point
	var length := direction.length()
	var basis := Basis.looking_at(direction / length, Vector3.UP)
	var size := Vector3(width_m, CREATION_OPS_SCRIPT.PATH_THICKNESS_M, length)
	var body := StaticBody3D.new()
	body.name = id
	body.collision_layer = 1
	body.collision_mask = 0
	body.transform = Transform3D(basis, (from_point + to_point) * 0.5 - basis.y * (size.y * 0.5))
	var shape := BoxShape3D.new()
	shape.size = size
	var collider := CollisionShape3D.new()
	collider.shape = shape
	body.add_child(collider)
	var mesh := MeshInstance3D.new()
	mesh.mesh = _stone_slab_mesh(size)
	mesh.material_override = _stone_material()
	body.add_child(mesh)
	return body


func _attach_soil_apron(body: StaticBody3D, from_point: Vector3, to_point: Vector3, width_m: float) -> void:
	# Terrain follows the pinned height grid. The wider soil shoulder is only a
	# visual interpretation; the source path and walkable stone collider stay exact.
	var start := Vector2(from_point.x, from_point.z)
	var end := Vector2(to_point.x, to_point.z)
	var across := (end - start).normalized().orthogonal()
	var half_width := width_m * 0.5 + 0.53
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	var inverse := body.transform.affine_inverse()
	for step in range(4):
		var a := start.lerp(end, float(step) / 4.0)
		var b := start.lerp(end, float(step + 1) / 4.0)
		var edge_a := half_width * (0.75 + 0.25 * sin(PI * float(step + 0.5) / 5.0) + 0.04 * sin(float(step) * 2.1))
		var edge_b := half_width * (0.75 + 0.25 * sin(PI * float(step + 1.5) / 5.0) + 0.04 * sin(float(step + 1) * 2.1))
		var corners := [a - across * edge_a, a + across * edge_a, b - across * edge_b, b + across * edge_b]
		var base := vertices.size()
		for point in corners:
			var y: float = viewer.map_runtime.surface_height_at(point.x, point.y) + 0.035
			vertices.append(inverse * Vector3(point.x, y, point.y))
			normals.append(Vector3.UP)
		indices.append_array(PackedInt32Array([base, base + 2, base + 1, base + 1, base + 2, base + 3]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var shoulder := MeshInstance3D.new()
	shoulder.name = "IllustrativeSoilShoulder"
	shoulder.mesh = mesh
	shoulder.material_override = viewer._style_material("trail")
	shoulder.visible = false # The source terrain shader paints the contact.
	shoulder.set_meta("provenance", "terrain-following visual soil around a player-authored path")
	body.add_child(shoulder)


func _attach_platform_soil_apron(body: StaticBody3D, size: Vector3) -> void:
	# A narrow terrain-following contact ring lets the deck meet the same soil
	# family as its generated path and join, without changing the slab collider.
	var inner := _clipped_outline(size.x * 0.5 + 0.03, size.z * 0.5 + 0.03, 0.16)
	var outer := _clipped_outline(size.x * 0.5 + 0.67, size.z * 0.5 + 0.67, 0.32)
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	var inverse := body.transform.affine_inverse()
	for i in range(inner.size()):
		var next := (i + 1) % inner.size()
		for local_point in [inner[i], outer[i], inner[next], outer[next]]:
			var world_point: Vector3 = body.transform * Vector3(local_point.x, 0.0, local_point.y)
			var ground_y: float = viewer.map_runtime.surface_height_at(world_point.x, world_point.z) + 0.035
			var deck_top_y := body.position.y + size.y * 0.5
			var y := minf(ground_y, deck_top_y - 0.06)
			vertices.append(inverse * Vector3(world_point.x, y, world_point.z))
			normals.append(Vector3.UP)
		var base := i * 4
		indices.append_array(PackedInt32Array([base, base + 2, base + 1, base + 1, base + 2, base + 3]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var contact := MeshInstance3D.new()
	contact.name = "IllustrativeSoilShoulder"
	contact.mesh = mesh
	contact.material_override = viewer._style_material("trail")
	contact.visible = false # Preserve the semantic child; hide the old overlay.
	contact.set_meta("provenance", "terrain-following visual soil contact around a player-authored platform")
	body.add_child(contact)


func _attach_stone_seams(body: StaticBody3D, length_m: float, width_m: float, thickness_m: float) -> void:
	var seams := MultiMesh.new()
	seams.transform_format = MultiMesh.TRANSFORM_3D
	var line := BoxMesh.new()
	var platform_course := thickness_m > CREATION_OPS_SCRIPT.PATH_THICKNESS_M + 0.01
	line.size = Vector3(width_m * 0.86, 0.012, 0.055 if platform_course else 0.025)
	seams.mesh = line
	var count := maxi(1, int(floor(length_m / 0.82)))
	seams.instance_count = count
	for i in range(count):
		var local_z := -length_m * 0.5 + length_m * float(i + 1) / float(count + 1)
		seams.set_instance_transform(i, Transform3D(Basis.IDENTITY, Vector3(0.0, thickness_m * 0.5 + 0.014, local_z)))
	var instance := MultiMeshInstance3D.new()
	instance.name = "StoneCourseJoints"
	instance.multimesh = seams
	instance.material_override = viewer._style_material("trail")
	instance.visible = false # Painted stone material now supplies surface strata.
	instance.set_meta("provenance", "illustrative repeating stone joints on an authored or derived stone part")
	body.add_child(instance)


func _stone_material() -> Material:
	if _painted_stone == null:
		_painted_stone = GROUND_KIT_SCRIPT.make_surface_material("limestone")
	return _painted_stone


func _stone_slab_mesh(size: Vector3) -> ArrayMesh:
	# One small, deterministic mesh per edit. The top remains exactly at the
	# box collider's walking surface; the bevel removes only a narrow visual
	# strip around its edge. Clipped corners soften the otherwise raw box.
	var half_x := size.x * 0.5
	var half_y := size.y * 0.5
	var half_z := size.z * 0.5
	var bevel := minf(0.12, minf(minf(size.x, size.z) * 0.08, size.y * 0.2))
	var corner := minf(0.14, minf(size.x, size.z) * 0.09)
	var outline := _clipped_outline(half_x, half_z, corner)
	var top_outline := _clipped_outline(half_x - bevel, half_z - bevel, corner)
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var top_center := Vector3(0.0, half_y, 0.0)
	var bottom_center := Vector3(0.0, -half_y, 0.0)
	for i in range(outline.size()):
		var next := (i + 1) % outline.size()
		var outer_a := Vector3(outline[i].x, half_y - bevel, outline[i].y)
		var outer_b := Vector3(outline[next].x, half_y - bevel, outline[next].y)
		var top_a := Vector3(top_outline[i].x, half_y, top_outline[i].y)
		var top_b := Vector3(top_outline[next].x, half_y, top_outline[next].y)
		var bottom_a := Vector3(outline[i].x, -half_y, outline[i].y)
		var bottom_b := Vector3(outline[next].x, -half_y, outline[next].y)
		_append_facet(vertices, normals, top_center, top_b, top_a)
		_append_facet(vertices, normals, top_a, top_b, outer_b)
		_append_facet(vertices, normals, top_a, outer_b, outer_a)
		_append_facet(vertices, normals, outer_a, outer_b, bottom_b)
		_append_facet(vertices, normals, outer_a, bottom_b, bottom_a)
		_append_facet(vertices, normals, bottom_center, bottom_a, bottom_b)
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	var result := ArrayMesh.new()
	result.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return result


func _clipped_outline(half_x: float, half_z: float, corner: float) -> PackedVector2Array:
	return PackedVector2Array([
		Vector2(-half_x + corner, -half_z), Vector2(half_x - corner, -half_z),
		Vector2(half_x, -half_z + corner), Vector2(half_x, half_z - corner),
		Vector2(half_x - corner, half_z), Vector2(-half_x + corner, half_z),
		Vector2(-half_x, half_z - corner), Vector2(-half_x, -half_z + corner),
	])


func _append_facet(vertices: PackedVector3Array, normals: PackedVector3Array, a: Vector3, b: Vector3, c: Vector3) -> void:
	var normal := (b - a).cross(c - a).normalized()
	# ArrayMesh front faces use the opposite winding from the cross product
	# used here for the outward lighting normal.
	for point in [a, c, b]:
		vertices.append(point)
		normals.append(normal)


func _update_hint() -> void:
	if hint == null:
		return
	hint.text = "LOCAL WORKSHOP  •  P platform  •  E resize  •  J add path  •  K move endpoint  •  O remove  •  F5 save  •  F9 reload\n" + message
