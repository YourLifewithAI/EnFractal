extends SceneTree

const TEST_SAVE := "user://tests/terrain_loading_input_smoke.json"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", TEST_SAVE)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	var workshop = scene.workshop_runtime
	if scene.coarse_ready_tiles >= scene.coarse_total_tiles or not scene.startup_cover.visible:
		_fail("The base phase completed before the loading gate could be checked")
		return
	if workshop.process_mode != Node.PROCESS_MODE_INHERIT or not workshop.edit_enabled:
		_fail("Workshop physics or local state was disabled while loading")
		return
	# Force walking through the internal test seam so F5 would otherwise be an
	# eligible workshop shortcut. The public Tab event remains blocked by cover.
	scene._set_walking_mode(true)
	if not scene.walking or scene.terrain_colliders.active_patches.is_empty():
		_fail("Source-grid walking collision was unavailable during base loading")
		return
	_send_f5()
	await process_frame
	if FileAccess.file_exists(TEST_SAVE):
		_fail("Workshop save shortcut wrote state behind the loading cover")
		return
	var deadline := Time.get_ticks_msec() + 10000
	while scene.coarse_ready_tiles < scene.coarse_total_tiles and Time.get_ticks_msec() < deadline:
		await process_frame
	if scene.coarse_ready_tiles != 64 or scene.startup_cover.visible or workshop.process_mode != Node.PROCESS_MODE_INHERIT:
		_fail("Coarse terrain was not revealed with workshop physics still enabled")
		return
	if FileAccess.file_exists(TEST_SAVE):
		_fail("A buffered save shortcut ran after the loading cover disappeared")
		return
	_send_f5()
	await process_frame
	if not FileAccess.file_exists(TEST_SAVE):
		_fail("Workshop save shortcut did not work after loading")
		return
	scene._set_walking_mode(false)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	print("Terrain loading input smoke passed; F5 blocked during coarse phase, saved after reveal, collision independent")
	quit(0)


func _send_f5() -> void:
	var event := InputEventKey.new()
	event.keycode = KEY_F5
	event.pressed = true
	Input.parse_input_event(event)


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
