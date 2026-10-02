extends SceneTree
## Captures the actual local workshop at a 1.67 m eye height, with source edits.
## ENFRACTAL_WORKSHOP_ART_CAPTURE_PREFIX is an absolute path without extension.
## ENFRACTAL_ART_PROFILE=low selects the reduced near-field dressing.

const TEST_SAVE := "user://tests/workshop_art_capture.json"


func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var prefix := OS.get_environment("ENFRACTAL_WORKSHOP_ART_CAPTURE_PREFIX")
	if prefix.is_empty() or not prefix.is_absolute_path():
		push_error("Set ENFRACTAL_WORKSHOP_ART_CAPTURE_PREFIX to an absolute path without extension")
		quit(1)
		return
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", TEST_SAVE)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	scene._jump_to_view("pin")
	scene._set_walking_mode(true)
	var workshop = scene.workshop_runtime
	if workshop == null or not workshop.edit_enabled or workshop.art_dressing == null:
		_fail("Workshop or near-field art failed to initialize")
		return
	var eye := Vector2(-5.0, 337.0)
	var focus := Vector2(0.0, 344.0)
	var camera := Camera3D.new()
	camera.name = "ActualWalkingHeightReviewCamera"
	scene.add_child(camera)
	camera.global_position = Vector3(eye.x, scene.map_runtime.surface_height_at(eye.x, eye.y) + 1.67, eye.y)
	camera.look_at(Vector3(focus.x, scene.map_runtime.surface_height_at(focus.x, focus.y) + 0.8, focus.y), Vector3.UP)
	camera.fov = 58.0
	camera.near = 0.08
	camera.current = true
	# Capture clean review frames; semantic edit receipts remain in the console.
	scene.status_label.get_parent().visible = false
	workshop.hint.get_parent().visible = false
	await _settle(scene)
	if not await _write_png(scene, prefix + "_before.png"):
		return
	workshop._place_in_front()
	workshop._place_path_for_selected()
	workshop._move_path_endpoint()
	if workshop.state.revision != 3 or workshop.join_relations.size() != 1:
		_fail("Authored path/platform revision failed: " + workshop.message)
		return
	var stable_join_id: String = workshop.join_relations.keys()[0]
	await _settle(scene)
	if not await _write_png(scene, prefix + "_revised.png"):
		return
	workshop._reload()
	if workshop.state.revision != 3 or workshop.join_relations.size() != 1 or workshop.join_relations.keys()[0] != stable_join_id:
		_fail("Reload changed the saved revision or generated join")
		return
	await _settle(scene)
	if not await _write_png(scene, prefix + "_reloaded.png"):
		return
	print("Workshop art captures passed: profile=", workshop.art_dressing.profile, "; eye=1.67 m; revision=", workshop.state.revision, "; stable join=", stable_join_id, "; dressing=", workshop.art_dressing.stats)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	quit(0)


func _settle(scene: Node3D) -> void:
	for frame in range(240):
		await process_frame
		if frame >= 80 and scene.pending_lod.is_empty() and scene.lod_task_id < 0:
			break
	await RenderingServer.frame_post_draw


func _write_png(scene: Node3D, path: String) -> bool:
	await RenderingServer.frame_post_draw
	var result := scene.get_viewport().get_texture().get_image().save_png(path)
	if result != OK:
		_fail("Could not save review image: " + path)
		return false
	print("Workshop art frame: ", path)
	return true


func _fail(description: String) -> void:
	push_error(description)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	quit(1)
