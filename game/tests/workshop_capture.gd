extends SceneTree
## Render a repeatable walking-height image of the local workshop.
## Run with Godot (graphics enabled) and ENFRACTAL_WORKSHOP_CAPTURE set to a PNG path.

const TEST_SAVE := "user://tests/workshop_capture.json"


func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var output_path := OS.get_environment("ENFRACTAL_WORKSHOP_CAPTURE")
	if output_path.is_empty():
		push_error("Set ENFRACTAL_WORKSHOP_CAPTURE to an output PNG path")
		quit(1)
		return
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", TEST_SAVE)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	scene._jump_to_view("pin")
	scene.yaw = 1.1 # Look west toward the Greenbelt while keeping the test plot in view.
	scene.camera.rotation.y = scene.yaw
	scene._set_walking_mode(true)
	scene.walk_camera.rotation.x = -0.27
	scene.workshop_runtime._place_in_front()
	if scene.workshop_runtime.state.revision != 1:
		push_error("Workshop capture could not place its platform: " + scene.workshop_runtime.message)
		quit(1)
		return
	var settled := false
	for frame in range(240):
		await process_frame
		if frame >= 80 and scene.pending_lod.is_empty():
			settled = true
			break
	await RenderingServer.frame_post_draw
	var result := scene.get_viewport().get_texture().get_image().save_png(output_path)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	print("Workshop capture saved: ", output_path, " (", result, "); visual LOD settled: ", settled)
	quit(0 if result == OK and settled else 1)
