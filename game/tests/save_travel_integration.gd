extends SceneTree
const COMPILER = preload("res://scripts/creation_compiler.gd")
var checks := 0
var failures := 0
var viewer
var travel
var workshop

func _initialize() -> void:
	call_deferred("_run")

func check(value: bool, label: String) -> void:
	checks += 1
	if not value:
		failures += 1
		push_error("Save/travel: " + label)

func _dispatch(event: InputEvent) -> void:
	if DisplayServer.get_name() == "headless":
		root.push_input(event,true)
	else:
		Input.parse_input_event(event)

func _click(button: Button) -> void:
	await process_frame
	var point := button.get_global_rect().get_center()
	if button.get_window().is_embedded():
		point += Vector2(button.get_window().position)
	check(button.is_visible_in_tree() and not button.disabled and root.get_visible_rect().encloses(button.get_global_rect()),"action visible and available: " + str(button.name))
	var motion := InputEventMouseMotion.new()
	motion.position = point
	motion.global_position = point
	root.push_input(motion,true)
	for pressed in [true,false]:
		var event := InputEventMouseButton.new()
		event.position = point
		event.global_position = point
		event.button_index = MOUSE_BUTTON_LEFT
		event.pressed = pressed
		root.push_input(event,true)
		await process_frame
	for i in range(3):
		await process_frame

func _key(code: int) -> void:
	for pressed in [true,false]:
		var event := InputEventKey.new()
		event.keycode = code
		event.pressed = pressed
		_dispatch(event)
		await process_frame

func _capture(name: String) -> void:
	if DisplayServer.get_name() == "headless":
		return
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://../docs/images/save-travel-" + name + ".png")

func _place(name: String) -> void:
	var source: Dictionary = COMPILER.templates()[4].duplicate(true)
	source.name = name
	var result: Dictionary = workshop.commit_draft(source,"",{"x_m":12.0,"z_m":350.0,"yaw_deg":0.0},workshop.authority.revision,workshop.authority.permission_revision)
	check(result.ok,"durable creation " + name + ": " + str(result.get("message","")))

func _name() -> String:
	var snapshot: Dictionary = workshop.authority.snapshot("local_player")
	if not snapshot.get("ok",false) or snapshot.instances.size() != 1:
		return ""
	return snapshot.instances.values()[0].source.name

func _visit() -> void:
	travel.panel.open_panel()
	travel.panel.selected_world_id = "sandbox_local_player"
	travel.panel._refresh()
	await _click(travel.panel.visit_button)
	check(travel.panel.visit_dialog.visible,"explicit destination rules confirmation")
	await _key(KEY_ESCAPE)
	check(travel.panel_open and not travel.panel.visit_dialog.visible,"Escape dismisses confirmation before travel panel")
	await _click(travel.panel.visit_button)
	await _click(travel.panel.visit_dialog.get_ok_button())
	check(travel.connected and travel.world_id == "sandbox_local_player", "confirmed trip arrives: " + travel.message)
	check(is_equal_approx(viewer.player_body.world_physics.gravity_mps2,5.5),"quarter gravity applied to actual player")
	check(is_equal_approx(workshop.guest_gravity_scale,0.25),"quarter gravity applies to local consent fixture")
	check(workshop.fields.is_empty() and viewer.player_body.creation_velocity.is_zero_approx(),"old effects cannot travel between worlds")
	check(not workshop.authority.snapshot("local_player").get("consent",{}).get("local_player",true),"destination consent starts off")

func _run() -> void:
	var test_save := "user://tests/travel_inventions_%d.json" % OS.get_process_id()
	var legacy_save := "user://tests/travel_legacy_%d.json" % OS.get_process_id()
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE",legacy_save)
	# Build an older local path/platform fixture first: travel must leave it alone.
	var legacy = load("res://scenes/main.tscn").instantiate()
	root.add_child(legacy)
	await process_frame
	legacy._start_workshop_walk()
	legacy.workshop_runtime._place_in_front()
	check(legacy.workshop_runtime.platform_nodes.size() == 1,"legacy user platform fixture exists")
	var legacy_bytes := FileAccess.get_file_as_bytes(legacy_save)
	# Let Compatibility finish the fixture's first render before tearing down its
	# just-created textures. Freeing before that draw leaks two GLES allocations.
	if DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
	legacy.queue_free()
	await process_frame
	await process_frame
	OS.set_environment("ENFRACTAL_MANUAL_INVENTION","1")
	OS.set_environment("ENFRACTAL_START_WALK","1")
	OS.set_environment("ENFRACTAL_SAVE_TRAVEL","1")
	OS.set_environment("ENFRACTAL_INVENTION_SAVE",test_save)
	viewer = load("res://scenes/main.tscn").instantiate()
	root.add_child(viewer)
	travel = viewer.travel_runtime
	workshop = viewer.invention_runtime
	for frame in range(600):
		await process_frame
		if travel.connected and not viewer.startup_cover.visible:
			break
	check(travel.connected,"PostgreSQL startup: " + travel.message)
	if not travel.connected:
		viewer.queue_free()
		await process_frame
		quit(1)
		return
	check(travel.world_id == "home" and viewer.walking,"Home is initial authoritative world")
	check(viewer.workshop_runtime.platform_nodes.is_empty() and not viewer.workshop_runtime.edit_enabled,"older local fixtures are isolated from travel worlds")
	check(FileAccess.get_file_as_bytes(legacy_save) == legacy_bytes,"legacy save preserved unchanged")
	check(is_equal_approx(viewer.player_body.world_physics.gravity_mps2,22.0),"normal Home gravity")
	var resume := OS.get_environment("ENFRACTAL_TRAVEL_TEST_RESUME") == "1"
	if not resume:
		_place("Home lantern")
		await _key(KEY_T)
		check(travel.panel_open and viewer.player_body.editor_input_blocked,"T opens modal travel")
		await _click(travel.panel.create_button)
		check(travel._owned_sandbox() == "sandbox_local_player","one saved free sandbox created")
		travel.panel.selected_world_id = "sandbox_local_player"
		travel.panel._refresh()
		await _capture("destinations")
	else:
		check(_name() == "Home lantern","Home acknowledged source survives game and service restart")
	await _visit()
	if not resume:
		check(workshop.instances.is_empty(),"base-only fork contains no Home inventions")
		_place("Sandbox lantern")
		await _click(travel.panel.invite_button)
		check(travel._guest_invitations().size() == 1,"invitation persisted")
		travel.invite_id = ""
		await _click(travel.panel.revoke_button)
		check(travel._guest_invitations().is_empty(),"saved invitation revoked without transient ID")
	else:
		check(_name() == "Sandbox lantern","sandbox acknowledged source survives process restart separately")
	travel.panel.close_panel()
	await physics_frame
	await process_frame
	await _capture("sandbox")
	check(travel.reconnect().ok and _name() == "Sandbox lantern","same-world reconnect revalidates source and rules")
	travel.panel.open_panel()
	await _click(travel.panel.return_button)
	check(travel.world_id == "home" and _name() == "Home lantern","Home return restores only Home source")
	check(is_equal_approx(viewer.player_body.world_physics.gravity_mps2,22.0),"return restores normal gravity despite equal per-world rules revisions")
	check(travel.status.get("pending",{}).is_empty(),"arrival acknowledged after source teardown")
	check(not FileAccess.file_exists(test_save),"PostgreSQL adapter has no competing JSON save")
	travel.panel.close_panel()
	await physics_frame
	await process_frame
	await _capture("home")
	viewer.queue_free()
	await process_frame
	await process_frame
	DirAccess.remove_absolute(ProjectSettings.globalize_path(legacy_save))
	print("Save/travel actual map (%s): %d checks, %d failures" % ["restart" if resume else "first run",checks,failures])
	quit(0 if failures == 0 else 1)
