extends Node3D
## Local construction fixture over the immutable map. This is deliberately not
## Home Earth authority: the caller supplies one developer-only plot and actor.

const WORLD_STATE_SCRIPT := preload("res://scripts/world_state.gd")
const CREATION_OPS_SCRIPT := preload("res://scripts/creation_ops.gd")
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
	if FileAccess.file_exists(save_path):
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
	if edit_enabled:
		_restore_selection()
		_render_entities()
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
	for entity in candidate.get_entities().values():
		var result: Dictionary = CREATION_OPS_SCRIPT.validate_stored_entity(entity, candidate, PLOT, Callable(viewer.map_runtime, "surface_height_at"))
		if not result.get("ok", false):
			return result
	return {"ok": true}


func _new_action_id() -> String:
	return Crypto.new().generate_random_bytes(16).hex_encode()


func _place_in_front() -> void:
	if not edit_enabled:
		_update_hint()
		return
	if not state.get_entities().is_empty():
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
	if entity.is_empty():
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


func _remove_selected() -> void:
	if state.get_entity(selected_entity_id).is_empty():
		message = "There is no selected platform"
		_update_hint()
		return
	_submit({"op": "remove_platform", "action_id": _new_action_id(), "expected_revision": state.revision, "entity_id": selected_entity_id})


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
	if intent["op"] == "remove_platform":
		selected_entity_id = ""
		_restore_selection()
	_render_entities()
	message = "Platform %s • revision %d" % [intent["op"].trim_suffix("_platform"), state.revision]
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
	for entity_id in state.get_entities().keys():
		selected_entity_id = entity_id


func _render_entities() -> void:
	for instance in platform_nodes.values():
		instance.queue_free()
	platform_nodes.clear()
	var check := _validate_state(state)
	if not check.get("ok", false):
		edit_enabled = false
		message = "Workshop object rejected: " + str(check.get("error", "invalid_entity"))
		_update_hint()
		return
	for entity_id in state.get_entities():
		var entity: Dictionary = state.get_entity(entity_id)
		if entity.get("kind") != "stone_platform":
			continue
		var transform: Dictionary = entity["transform"]
		var params: Dictionary = entity["params"]
		var size := Vector3(float(params["width_m"]), float(params["thickness_m"]), float(params["depth_m"]))
		var body := StaticBody3D.new()
		body.name = entity_id
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
		mesh.material_override = viewer._style_material(str(entity["material_role"]))
		body.add_child(mesh)
		add_child(body)
		platform_nodes[entity_id] = body


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
	hint.text = "LOCAL WORKSHOP  •  P place  •  E resize  •  O remove  •  F5 save  •  F9 reload\n" + message
