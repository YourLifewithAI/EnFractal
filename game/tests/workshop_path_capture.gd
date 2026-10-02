extends SceneTree
## Repeatable local image of the authored path, regenerated join and platform.
## Run with graphics enabled and ENFRACTAL_WORKSHOP_PATH_CAPTURE set to a PNG path.

const TEST_SAVE := "user://tests/workshop_path_capture.json"


func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var output_path := OS.get_environment("ENFRACTAL_WORKSHOP_PATH_CAPTURE")
	if output_path.is_empty():
		push_error("Set ENFRACTAL_WORKSHOP_PATH_CAPTURE to an output PNG path")
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
	workshop._place_in_front()
	workshop._place_path_for_selected()
	workshop._move_path_endpoint()
	if workshop.state.revision != 3 or workshop.join_relations.size() != 1:
		push_error("Workshop path capture failed: " + workshop.message)
		quit(1)
		return
	var relation: Dictionary = workshop.join_relations.values()[0]
	var start: Dictionary = relation["params"]["start"]
	var end: Dictionary = relation["params"]["end"]
	var focus := Vector3((float(start["x_m"]) + float(end["x_m"])) * 0.5, (float(start["y_m"]) + float(end["y_m"])) * 0.5, (float(start["z_m"]) + float(end["z_m"])) * 0.5)
	var camera := Camera3D.new()
	camera.name = "PathPlatformReferenceCamera"
	scene.add_child(camera)
	camera.global_position = focus + Vector3(-5.0, 3.2, -6.5)
	camera.look_at(focus + Vector3(0.0, 0.4, 0.0), Vector3.UP)
	camera.current = true
	var settled := false
	for frame in range(240):
		await process_frame
		if frame >= 80 and scene.pending_lod.is_empty() and scene.lod_task_id < 0:
			settled = true
			break
	await RenderingServer.frame_post_draw
	var result := scene.get_viewport().get_texture().get_image().save_png(output_path)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	print("Workshop path capture saved: ", output_path, " (", result, "); visual LOD settled: ", settled)
	quit(0 if result == OK and settled else 1)
