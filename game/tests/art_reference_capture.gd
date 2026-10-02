extends SceneTree
## ENFRACTAL_ART_CAPTURE is a PNG path; optional ENFRACTAL_ART_PROFILE=low.


func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var output := OS.get_environment("ENFRACTAL_ART_CAPTURE")
	if output.is_empty():
		push_error("Set ENFRACTAL_ART_CAPTURE to an output PNG path")
		quit(1)
		return
	var scene: Node3D = load("res://scenes/art_reference.tscn").instantiate()
	var build_started := Time.get_ticks_msec()
	root.add_child(scene)
	var build_ms := Time.get_ticks_msec() - build_started
	for frame in range(12):
		await process_frame
	await RenderingServer.frame_post_draw
	var image := scene.get_viewport().get_texture().get_image()
	var result := image.save_png(output)
	print("Art reference capture: ", output, "; profile=", scene.profile, "; view=", OS.get_environment("ENFRACTAL_ART_VIEW"), "; build_ms=", build_ms, "; stats=", scene.stats, "; result=", result)
	quit(0 if result == OK else 1)
