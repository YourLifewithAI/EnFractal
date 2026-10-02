extends SceneTree
## Actual Barton map + real editor controls; isolated save, no personal save writes.
const COMPILER = preload("res://scripts/creation_compiler.gd")
var checks := 0
var failures := 0

func _initialize() -> void:
	call_deferred("_run")

func check(condition: bool, label: String) -> void:
	checks += 1
	if not condition:
		failures += 1
		push_error("Manual integration: " + label)

func _capture(path: String) -> void:
	if DisplayServer.get_name() == "headless":
		return
	await process_frame
	await RenderingServer.frame_post_draw
	var destination := path.replace(".png", "-low.png") if OS.get_environment("ENFRACTAL_ART_PROFILE") == "low" else path
	root.get_texture().get_image().save_png(destination)

func _click(control: Button) -> void:
	await process_frame
	await process_frame
	var rectangle := control.get_global_rect()
	check(root.get_visible_rect().encloses(rectangle) and control.is_visible_in_tree(), "required button visible: " + str(control.name))
	var point := rectangle.get_center()
	var motion := InputEventMouseMotion.new()
	motion.position = point
	motion.global_position = point
	_dispatch(motion)
	for pressed in [true,false]:
		var event := InputEventMouseButton.new()
		event.position = point
		event.global_position = point
		event.button_index = MOUSE_BUTTON_LEFT
		event.pressed = pressed
		_dispatch(event)
		await process_frame

func _dispatch(event: InputEvent) -> void:
	if DisplayServer.get_name() == "headless":
		root.push_input(event, true)
	else:
		Input.parse_input_event(event)

func _key(keycode: int) -> void:
	for pressed in [true,false]:
		var event := InputEventKey.new()
		event.keycode = keycode
		event.pressed = pressed
		_dispatch(event)
		await process_frame

func _drag_part(control: Control) -> void:
	await process_frame
	var start := control.get_global_rect().get_center()
	var finish := start + Vector2(42,12)
	var hover := InputEventMouseMotion.new()
	hover.position = start
	hover.global_position = start
	_dispatch(hover)
	var press := InputEventMouseButton.new()
	press.position = start
	press.global_position = start
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	press.shift_pressed = true
	_dispatch(press)
	await process_frame
	var motion := InputEventMouseMotion.new()
	motion.position = finish
	motion.global_position = finish
	motion.relative = finish-start
	motion.button_mask = MOUSE_BUTTON_MASK_LEFT
	motion.shift_pressed = true
	_dispatch(motion)
	await process_frame
	var release := InputEventMouseButton.new()
	release.position = finish
	release.global_position = finish
	release.button_index = MOUSE_BUTTON_LEFT
	release.shift_pressed = true
	_dispatch(release)
	await process_frame

func _run() -> void:
	var test_save := "user://tests/manual_map_%d.json" % OS.get_process_id()
	OS.set_environment("ENFRACTAL_MANUAL_INVENTION", "1")
	OS.set_environment("ENFRACTAL_START_WALK", "1")
	OS.set_environment("ENFRACTAL_INVENTION_SAVE", test_save)
	var viewer = load("res://scenes/main.tscn").instantiate()
	root.add_child(viewer)
	for frame in range(600):
		await physics_frame
		if viewer.walking and not viewer.startup_cover.visible:
			break
	check(viewer.walking and not viewer.startup_cover.visible, "real map entered walking mode")
	var runtime = viewer.invention_runtime
	check(runtime != null, "manual runtime attached")
	if runtime == null or runtime.editor == null:
		quit(1)
		return
	var editor = runtime.editor
	var ids: Array = []
	var locations := [Vector2(-5,343),Vector2(-8,350),Vector2(0,351),Vector2(12,351),Vector2(18,346)]
	var templates: Array = COMPILER.templates()
	for index in range(templates.size()):
		editor.open_editor()
		check(runtime.editor_open and viewer.player_body.editor_input_blocked, "opening blocks gameplay input")
		# Choose by visible label, rather than replacing source behind the controls.
		var selected := -1
		for option in range(editor.template_picker.item_count):
			if templates[index].name in editor.template_picker.get_item_text(option):
				selected = option
				break
		check(selected >= 0, "recipe visible in editor")
		if selected < 0:
			continue
		editor.template_picker.select(selected)
		editor.template_picker.item_selected.emit(selected)
		editor.name_input.text = "Field study " + str(index + 1)
		editor.name_input.text_submitted.emit(editor.name_input.text)
		if index == 0:
			var prior_position: Array = editor.current_source().parts[0].position_m.duplicate()
			await _drag_part(editor._preview_container)
			check(editor.current_source().parts[0].position_m != prior_position, "actual Shift-drag moves selected source part in the preview")
			await _click(editor.undo_button)
			check(editor.current_source().parts[0].position_m == prior_position, "one visible Undo restores the complete drag gesture")
		editor.placement_controls["x_m"].value = locations[index].x
		editor.placement_controls["z_m"].value = locations[index].y
		var before: int = runtime.authority.revision
		if index == 1:
			editor.placement_controls["x_m"].value = 0.0
			editor.placement_controls["z_m"].value = 390.0
			await _click(editor.ui.find_child("TestButton",true,false))
			check(editor.confirm_button.disabled and runtime.authority.revision == before, "Test preflights protected placement before confirmation without publishing")
			editor.placement_controls["x_m"].value = locations[index].x
			editor.placement_controls["z_m"].value = locations[index].y
		await _click(editor.ui.find_child("TestButton",true,false))
		check(runtime.authority.revision == before and runtime.fields.is_empty(), "private preview never publishes effects")
		check(not editor.confirm_button.disabled, "valid source is ready for explicit confirmation")
		if index == 0:
			await _capture("res://../docs/images/manual-invention-editor.png")
		await _click(editor.confirm_button)
		check(runtime.authority.revision == before + 1, "editor confirmation persisted recipe %d: %s" % [index,editor.status_label.text])
		var snapshot: Dictionary = runtime.authority.snapshot("local_player")
		var newest := ""
		for id in snapshot.instances:
			if id not in ids:
				newest = id
		check(not newest.is_empty(), "committed instance appeared")
		ids.append(newest)
		editor.close_editor()
		check(not runtime.editor_open and not viewer.player_body.editor_input_blocked, "closing restores gameplay")
		check(not editor.is_processing(), "closed preview simulation stops")
		await process_frame
	if ids.size() == 5 and not "" in ids:
		runtime.set_local_consent(true)
		check(not runtime.activate_creation(ids[4]).ok, "host rejects a distant ground activation even when called directly")
		runtime.set_guest_consent(true)
		check(not runtime.activate_creation(ids[0], "guest_player").ok, "host rejects another player's manually triggered wearable")
		runtime.set_guest_consent(false)
		check(viewer.player_body.spawn_at(-8,350,viewer.player_body.rotation.y) and runtime._spawn_clearance(viewer.player_body.global_position), "respawn searches around saved inventions instead of starting inside them")
		viewer.player_body.spawn_at(-8,346,viewer.player_body.rotation.y)
		viewer.terrain_colliders.update_center(viewer.player_body.global_position)
		for frame in range(4):
			await physics_frame
		check(runtime.nearest_creation() == ids[1] and runtime.equipped_creation() == ids[0], "worn design does not steal world-device selection")
		await _key(KEY_F)
		check(not runtime.fields.is_empty() and runtime.fields[0].instance_id == ids[1], "F uses ground invention while a wearable is equipped")
		await _key(KEY_V)
		check(editor.editing_instance_id == ids[1] and runtime.editor_open, "V reopens ground invention while wearing a glider")
		editor.close_editor()
		await _key(KEY_E)
		check(runtime.fields.any(func(field): return field.instance_id == ids[0]), "E uses the equipped design through gameplay input")
		await _key(KEY_Q)
		check(editor.editing_instance_id == ids[0] and runtime.editor_open, "Q reopens the equipped source through gameplay input")
		editor.close_editor()
		var original: Dictionary = runtime.authority.snapshot("local_player").instances[ids[1]].source.duplicate(true)
		editor.open_editor(ids[1])
		editor.name_input.text = "Revised river lift"
		editor.name_input.text_submitted.emit(editor.name_input.text)
		await _click(editor.ui.find_child("TestButton",true,false))
		await _click(editor.confirm_button)
		var revised: Dictionary = runtime.authority.snapshot("local_player").instances[ids[1]]
		check(revised.source.name == "Revised river lift" and revised.revision == 2, "saved source reopened and revised")
		check(revised.source.nodes == original.nodes and revised.source.edges == original.edges, "revision preserves behavioral intent")
		editor.close_editor()
		var activation: Dictionary = runtime.activate_creation(ids[1])
		check(activation.ok and runtime.fields.size() > 0, "real map invention emits a live bounded wind field")
		var wind_field: Dictionary = runtime.fields[0]
		var point: Vector3 = wind_field.part.global_position
		check(runtime.effect_at("local_player", point).acceleration.length() > 0.0, "consenting player can receive field")
		check(runtime.effect_at("guest_player", point).acceleration.is_zero_approx(), "nonconsenting test guest receives no field")
		runtime.set_third_person(true)
		for frame in range(3):
			await physics_frame
		check(runtime.assemblies[ids[0]].visible, "equipped design is visible in inspection camera")
		await _capture("res://../docs/images/manual-invention-play.png")
		runtime.set_local_consent(false)
		check(runtime.fields.is_empty() and viewer.player_body.creation_velocity.is_zero_approx(), "revocation clears active forces and carried motion")
		var prior: Dictionary = runtime.authority.snapshot("local_player")
		var loaded: Dictionary = runtime.authority.load_saved()
		runtime._refresh()
		check(loaded.ok and runtime.authority.snapshot("local_player").instances.size() == 5, "saved inventions recompiled and reloaded")
		check(runtime.authority.snapshot("local_player").instances[ids[1]].source == prior.instances[ids[1]].source, "reload preserves revised structured source")
	viewer.queue_free()
	await process_frame
	await process_frame
	DirAccess.remove_absolute(ProjectSettings.globalize_path(test_save))
	print("Manual map/editor integration: %d checks, %d failures" % [checks, failures])
	quit(0 if failures == 0 else 1)
