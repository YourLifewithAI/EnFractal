extends SceneTree
## Multi-view evidence from the real editable workshop, with isolated test saves.
const TEST_SAVE := "user://tests/painterly_patch_capture.json"
var output: String

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	output = OS.get_environment("ENFRACTAL_ART_OUTPUT")
	if output.is_empty():
		output = ProjectSettings.globalize_path("res://../.cache/painterly-patch")
	DirAccess.make_dir_recursive_absolute(output)
	OS.set_environment("ENFRACTAL_ART_CAPTURE", "1")
	OS.set_environment("ENFRACTAL_STYLE", "natural")
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", TEST_SAVE)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	scene._jump_to_view("pin")
	scene._set_walking_mode(true)
	var workshop = scene.workshop_runtime
	if workshop == null or not workshop.edit_enabled:
		_fail("Editable workshop unavailable")
		return
	scene.status_label.get_parent().visible = false
	workshop.hint.get_parent().visible = false
	var camera := Camera3D.new()
	camera.fov = 62
	camera.near = 0.08
	scene.add_child(camera)
	camera.current = true
	_set_view(camera, scene, Vector2(-5,337), Vector2(-1,349), 1.67, 2.0)
	await _settle(scene)
	await _capture("01-arrival-before.png")
	workshop._place_in_front()
	workshop._place_path_for_selected()
	workshop._move_path_endpoint()
	if workshop.state.revision != 3 or workshop.join_relations.size() != 1:
		_fail("Path/platform creation did not produce one stable join: " + workshop.message)
		return
	var before_snapshot: Dictionary = workshop.state.get_snapshot()
	var join_id: String = workshop.join_relations.keys()[0]
	await _settle(scene)
	await _capture("02-arrival-edited.png")
	workshop._reload()
	if not _same_semantics(workshop.state.get_snapshot(), before_snapshot) or not workshop.join_relations.has(join_id):
		_fail("Reload changed editable source or join identity")
		return
	await _settle(scene)
	await _capture("03-arrival-reloaded.png")
	_set_view(camera, scene, Vector2(8,341), Vector2(13,358), 1.67, 1.0)
	await _settle(scene)
	await _capture("04-bank-walk.png")
	_set_view(camera, scene, Vector2(-11,354), Vector2(3,347), 1.67, 1.5)
	await _settle(scene)
	await _capture("05-under-oak.png")
	_set_view(camera, scene, Vector2(15,373), Vector2(0,350), 18.0, 0.4)
	await _settle(scene)
	await _capture("06-construction.png")
	var report := {
		"engine": Engine.get_version_info()["string"],
		"renderer": RenderingServer.get_current_rendering_method(),
		"adapter": RenderingServer.get_video_adapter_name(),
		"profile": workshop.art_dressing.profile,
		"eye_height_m": 1.67,
		"source_map": scene.manifest["region_id"] if scene.manifest.has("region_id") else "barton_creek_v0",
		"source_height_sha256": scene.manifest["heights_sha256"],
		"saved_revision": workshop.state.revision,
		"stable_join": join_id,
		"scene_stats": workshop.art_dressing.stats,
		"surround_tree_count": scene.get_meta("authored_surround_tree_count", 0),
		"texture_memory_bytes": RenderingServer.get_rendering_info(RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED),
		"scope": "ART3 capture, not a target-device performance benchmark. Rain-garden swale and vegetation are illustrative."
	}
	if OS.get_environment("ENFRACTAL_ART_WALK_VIDEO") == "1":
		report["walk"] = await _walk(scene, camera)
	report["outside_patch_contact"] = await _outside_contact(scene, camera)
	var file := FileAccess.open(output.path_join("capture-report.json"), FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t") + "\n")
	file.close()
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	print("Painterly patch capture PASS: editable revision3, stable join and reload. ", output)
	quit(0)

func _set_view(camera: Camera3D, scene: Node3D, eye: Vector2, focus: Vector2, eye_height: float, focus_height: float) -> void:
	camera.position = Vector3(eye.x, scene.map_runtime.surface_height_at(eye.x,eye.y) + eye_height, eye.y)
	camera.look_at(Vector3(focus.x,scene.map_runtime.surface_height_at(focus.x,focus.y)+focus_height,focus.y))

func _settle(scene: Node3D) -> void:
	for frame in range(240):
		await process_frame
		if frame >= 50 and scene.pending_lod.is_empty() and scene.lod_task_id < 0:
			break
	await RenderingServer.frame_post_draw

func _capture(filename: String) -> void:
	await RenderingServer.frame_post_draw
	var error := root.get_texture().get_image().save_png(output.path_join(filename))
	if error != OK:
		_fail("Could not save " + filename)
	print("Captured ", filename)

func _walk(scene: Node3D, review_camera: Camera3D) -> Dictionary:
	var frames_dir := output.path_join("walk-frames")
	DirAccess.make_dir_recursive_absolute(frames_dir)
	review_camera.current = false
	scene.player_body.spawn_at(-4,338,PI,true)
	scene.terrain_colliders.update_center(scene.player_body.global_position)
	scene.walk_camera.current = true
	scene.walk_camera.fov = 65
	scene.walk_camera.rotation.x = 0.02
	var event := InputEventKey.new()
	event.keycode = KEY_W
	event.pressed = true
	Input.parse_input_event(event)
	var stopped := false
	var grounded_frames := 0
	Engine.max_fps = 30
	for frame in range(210):
		await process_frame
		if scene.player_body.global_position.z >= 348.0 and not stopped:
			event.pressed = false
			Input.parse_input_event(event)
			stopped = true
		if stopped:
			scene.player_body.rotation.y += 0.008
		if scene.player_body.is_on_floor():
			grounded_frames += 1
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png(frames_dir.path_join("frame-%04d.png" % frame))
	event.pressed = false
	Input.parse_input_event(event)
	print("Walk-through: grounded rendered frames=",grounded_frames,"/210; final position=",scene.player_body.global_position)
	return {"frames": 210, "grounded_frames": grounded_frames, "final_position": [scene.player_body.global_position.x, scene.player_body.global_position.y, scene.player_body.global_position.z], "note": "Frames encoded at 30 fps; screenshot readback affects runtime and is not an FPS measurement."}

func _outside_contact(scene: Node3D, camera: Camera3D) -> Dictionary:
	# Use validated player operations at an allowed point beyond the art radius.
	# Only the isolated test save is changed; restore the original edit afterward.
	var workshop = scene.workshop_runtime
	var saved_text := FileAccess.get_file_as_string(TEST_SAVE)
	for entity_id in workshop.path_nodes.keys():
		workshop._submit({"op": "remove_path", "action_id": workshop._new_action_id(), "expected_revision": workshop.state.revision, "entity_id": entity_id})
	for entity_id in workshop.platform_nodes.keys():
		workshop._submit({"op": "remove_platform", "action_id": workshop._new_action_id(), "expected_revision": workshop.state.revision, "entity_id": entity_id})
	workshop._submit({"op": "place_platform", "action_id": workshop._new_action_id(), "expected_revision": workshop.state.revision, "x_m": 48.0, "z_m": 350.0, "yaw_deg": 0.0, "width_m": 3.0, "depth_m": 2.0})
	if workshop.platform_nodes.size() != 1:
		_fail("Outside-patch contact probe edit failed: " + workshop.message)
		return {}
	camera.current = true
	_set_view(camera, scene, Vector2(48,344), Vector2(48,350), 4.0, 0.0)
	await _settle(scene)
	await _capture("07-outside-patch-contact.png")
	var with_contact := root.get_texture().get_image()
	scene.terrain_material.set_shader_parameter("edit_platform", Vector3.ZERO)
	await _settle(scene)
	await _capture("08-outside-patch-contact-disabled-control.png")
	var without_contact := root.get_texture().get_image()
	var changed := 0
	for y in range(with_contact.get_height()):
		for x in range(with_contact.get_width()):
			var a := with_contact.get_pixel(x,y)
			var b := without_contact.get_pixel(x,y)
			if absf(a.r-b.r) + absf(a.g-b.g) + absf(a.b-b.b) > 0.025:
				changed += 1
	var restored := FileAccess.open(TEST_SAVE, FileAccess.WRITE)
	restored.store_string(saved_text)
	restored.close()
	workshop._reload()
	if changed < 50:
		_fail("Outside-patch semantic soil mask had no visible rendered effect")
	return {"valid_edit_xz": [48.0,350.0], "art_radius_m": scene.terrain_material.get_shader_parameter("patch_radius"), "rendered_pixels_changed_by_contact_mask": changed}

func _fail(message: String) -> void:
	push_error("Painterly capture: " + message)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	quit(1)

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
