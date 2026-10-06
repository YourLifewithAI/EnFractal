extends SceneTree
## Real editor controls and isolated preview against the local host boundary.
## The host is a flat-floor fixture (the same shape as invention_runtime_smoke);
## the runtime, authority, editor and legacy fixture body are real.
const COMPILER = preload("res://scripts/creation_compiler.gd")
const Runtime = preload("res://scripts/invention_runtime.gd")
const Player = preload("res://scripts/player_controller.gd")

class FlatMap extends RefCounted:
	func surface_height_at(_x: float, _z: float) -> float:
		return 0.0

class TestViewer extends Node3D:
	var manifest := {"id": "editor-smoke-fixture", "source": "flat-pinned-physics-floor"}
	var map_runtime = FlatMap.new()
	var player_body
	var walking := true
	var walk_camera: Camera3D
	var invention_runtime
const SAVE := "user://tests/invention_editor_smoke_world.json"
const EXPORT := "user://tests/invention_editor_smoke_export.json"
const OVERSIZE := "user://tests/invention_editor_smoke_oversize.json"
var checks := 0
var failures := 0

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var previous_manual := OS.get_environment("ENFRACTAL_MANUAL_INVENTION")
	var previous_save := OS.get_environment("ENFRACTAL_INVENTION_SAVE")
	OS.set_environment("ENFRACTAL_MANUAL_INVENTION", "1")
	OS.set_environment("ENFRACTAL_INVENTION_SAVE", SAVE)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("user://tests"))
	for path in [SAVE, EXPORT, OVERSIZE]:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	var scene := TestViewer.new()
	root.add_child(scene)
	var floor := StaticBody3D.new()
	var floor_shape := CollisionShape3D.new()
	var floor_box := BoxShape3D.new()
	floor_box.size = Vector3(150, 1, 150)
	floor_shape.shape = floor_box
	floor.position = Vector3(0, -0.5, 350)
	floor.add_child(floor_shape)
	scene.add_child(floor)
	var player = Player.new()
	player.name = "FixturePlayer"
	var player_shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.3
	capsule.height = 1.7
	player_shape.shape = capsule
	player.add_child(player_shape)
	scene.walk_camera = Camera3D.new()
	player.add_child(scene.walk_camera)
	scene.player_body = player
	scene.add_child(player)
	player.configure(Callable(scene.map_runtime, "surface_height_at"), 1000.0)
	player.spawn_at(0, 350, 0)
	var host_runtime = Runtime.new()
	host_runtime.save_path = SAVE
	host_runtime.configure(scene)
	scene.add_child(host_runtime)
	scene.invention_runtime = host_runtime
	await process_frame
	var runtime = scene.invention_runtime
	_check(runtime != null and runtime.editor != null, "real local runtime constructs editor")
	if runtime == null or runtime.editor == null:
		_finish(previous_manual, previous_save)
		return
	var editor = runtime.editor
	_check(editor.open_editor(), "open editor from host snapshot")
	_check(editor.ui.visible and runtime.editor_open, "modal editor gates gameplay")
	_check(editor.preview_viewport.world_3d != scene.get_world_3d(), "draft preview owns a separate World3D")
	_check(editor.preview_viewport.render_target_update_mode == SubViewport.UPDATE_ALWAYS, "open preview renders")
	_check(editor._preview_outline != null and editor._preview_outline.get_parent().name == "harness", "selected part has a private outline tied to its transform")
	editor.part_list.select(1)
	editor.part_list.item_selected.emit(1)
	_check(editor._preview_outline.get_parent().name == "left_wing", "part selection follows selected geometry")
	editor.node_list.select(1)
	editor.node_list.item_selected.emit(1)
	_check(editor._preview_outline.get_parent().name == "harness", "behavior selection follows attached part")
	await process_frame
	editor.placement_controls["x_m"].value = -11.0
	editor.placement_controls["z_m"].value = 345.0
	editor._test_draft()
	_check(not editor.confirm_button.disabled, "valid draft is Test-approved before manipulation")
	var original_part_position: Array = editor.current_source()["parts"][0]["position_m"].duplicate()
	var original_undo_count: int = editor._undo.size()
	var assembly_before_move: Node3D = editor._preview_assembly
	var selected_preview_part: Node3D = editor._preview_outline.get_parent()
	var pointer_center: Vector2 = editor._preview_container.size * 0.5
	var shift_down := InputEventMouseButton.new()
	shift_down.button_index = MOUSE_BUTTON_LEFT
	shift_down.pressed = true
	shift_down.shift_pressed = true
	shift_down.position = pointer_center
	editor._on_preview_input(shift_down)
	var move_one := InputEventMouseMotion.new()
	move_one.position = pointer_center + Vector2(36, 16)
	editor._on_preview_input(move_one)
	var move_two := InputEventMouseMotion.new()
	move_two.position = pointer_center + Vector2(72, 32)
	editor._on_preview_input(move_two)
	var shift_up := InputEventMouseButton.new()
	shift_up.button_index = MOUSE_BUTTON_LEFT
	shift_up.pressed = false
	shift_up.position = move_two.position
	editor._on_preview_input(shift_up)
	var moved: Array = editor.current_source()["parts"][0]["position_m"]
	_check(not is_equal_approx(float(moved[0]), float(original_part_position[0])) or not is_equal_approx(float(moved[2]), float(original_part_position[2])), "Shift-drag moves selected source part in XZ")
	_check(editor._undo.size() == original_undo_count + 1, "one direct manipulation gesture creates one undo step")
	_check(editor._preview_assembly == assembly_before_move and editor._preview_outline.get_parent() == selected_preview_part and is_equal_approx(selected_preview_part.position.x, float(moved[0])) and is_equal_approx(selected_preview_part.position.z, float(moved[2])), "private selected geometry and guide move without per-motion rebuilding")
	_check(editor.confirm_button.disabled and editor._tested_fingerprint.is_empty(), "direct manipulation requires fresh Test")
	editor._undo_draft()
	_check(editor.current_source()["parts"][0]["position_m"] == original_part_position, "gesture undo restores source part position")
	var study_camera: Camera3D = editor._preview_root.get_node("StudyCamera")
	var old_camera_position := study_camera.position
	var mouse_down := InputEventMouseButton.new()
	mouse_down.button_index = MOUSE_BUTTON_LEFT
	mouse_down.pressed = true
	editor._on_preview_input(mouse_down)
	var drag := InputEventMouseMotion.new()
	drag.relative = Vector2(80, -40)
	editor._on_preview_input(drag)
	_check(study_camera.position.distance_to(old_camera_position) > 0.5, "drag orbits isolated study camera")
	var wheel := InputEventMouseButton.new()
	wheel.button_index = MOUSE_BUTTON_WHEEL_UP
	wheel.pressed = true
	for turn in range(40):
		editor._on_preview_input(wheel)
	_check(editor._orbit_distance >= editor._orbit_min_distance and editor._orbit_distance <= editor._orbit_max_distance, "zoom clamps to useful study range")
	var mouse_up := InputEventMouseButton.new()
	mouse_up.button_index = MOUSE_BUTTON_LEFT
	mouse_up.pressed = false
	editor._on_preview_input(mouse_up)
	var original_window_size := root.size
	root.size = Vector2i(900, 600)
	await process_frame
	await process_frame
	var test_button: Button = editor.ui.find_child("TestButton", true, false)
	_check(root.get_visible_rect().encloses(test_button.get_global_rect()) and root.get_visible_rect().encloses(editor.confirm_button.get_global_rect()), "Test and Confirm remain visible at 900×600")
	root.size = original_window_size
	await process_frame
	editor._on_template_chosen(2) # Updraft totem: interact -> spin -> wind -> light.
	_check(editor.current_source()["name"] == "Updraft totem", "source template is editable data")
	var suspended_source := COMPILER.canonical_json(editor.current_source())
	editor.placement_controls["x_m"].value = -11.0
	editor.close_editor()
	_check(editor.open_editor() and COMPILER.canonical_json(editor.current_source()) == suspended_source and is_equal_approx(editor.placement_controls["x_m"].value, -11.0) and editor.confirm_button.disabled, "closing and reopening resumes unsaved new draft but requires fresh Test")
	editor._on_template_chosen(0)
	_check(editor.current_source()["name"] == "New invention" and editor.current_source()["parts"].size() == 1, "explicit Blank design resets resumed draft")
	editor._on_template_chosen(2)
	var before_parts: int = editor.current_source()["parts"].size()
	editor._add_part()
	_check(editor.current_source()["parts"].size() == before_parts + 1, "add part uses controls")
	editor._undo_draft()
	_check(editor.current_source()["parts"].size() == before_parts, "draft undo restores source")
	editor._redo_draft()
	_check(editor.current_source()["parts"].size() == before_parts + 1, "draft redo restores source")
	editor._undo_draft()
	editor._set_node_vector(2, "direction", 0, 0.7)
	editor._set_node_vector(2, "direction", 1, 0.7)
	_check(not editor.validate_draft().get("ok", false), "typed non-unit wind is rejected without discarding draft")
	editor._normalize_node_vector(2, "direction")
	_check(editor.validate_draft().get("ok", false), "Normalize direction makes typed wind valid")
	var source_before: String = COMPILER.canonical_json(editor.current_source())
	var file := FileAccess.open(OVERSIZE, FileAccess.WRITE)
	file.store_string("x".repeat(32769))
	file.close()
	editor._import_file(ProjectSettings.globalize_path(OVERSIZE))
	_check(COMPILER.canonical_json(editor.current_source()) == source_before, "oversize import preserves draft")
	editor._export_file(ProjectSettings.globalize_path(EXPORT))
	_check(FileAccess.file_exists(EXPORT) and COMPILER.compile(JSON.parse_string(FileAccess.get_file_as_string(EXPORT))).get("ok", false), "portable export is valid compiled source")
	var revision_before_preflight: int = runtime.authority.revision
	var permission_before_preflight: int = runtime.authority.permission_revision
	var source_before_preflight: String = COMPILER.canonical_json(editor.current_source())
	editor.placement_controls["x_m"].value = 0.0
	editor.placement_controls["z_m"].value = 395.0
	editor._test_draft()
	_check(editor.confirm_button.disabled and "protected" in editor.status_label.text.to_lower(), "Test reports protected placement before Confirm")
	_check(runtime.authority.revision == revision_before_preflight and runtime.authority.permission_revision == permission_before_preflight and COMPILER.canonical_json(editor.current_source()) == source_before_preflight, "preflight rejection preserves draft and authority revisions")
	editor.placement_controls["x_m"].value = -11.0
	editor.placement_controls["z_m"].value = 345.0
	editor._test_draft()
	_check(not editor.confirm_button.disabled and editor.tested_plan_ids() == ["use", "turn", "lift", "signal"], "Test uses reachable shared behavior plan")
	_check(editor._preview_dummy != null and editor._preview_effects.size() == 3, "private Test creates bounded visual effects")
	editor._process(6.0)
	_check(editor._preview_effects.is_empty(), "private effects expire by source duration")
	editor._add_node() # An unwired light is valid but not reached by Test.
	editor._test_draft()
	_check(not "action1" in editor.tested_plan_ids(), "unwired behavior does not execute in preview")
	var source_after: String = COMPILER.canonical_json(editor.current_source())
	_check(runtime.set_local_consent(true).get("ok", false), "external access mutation succeeds")
	editor._test_draft()
	_check(editor.confirm_button.disabled and "changed" in editor.status_label.text.to_lower(), "stale permission revision blocks Test without rebasing")
	_check(editor.refresh_world() and COMPILER.canonical_json(editor.current_source()) == source_after, "explicit world refresh preserves draft")
	editor._test_draft()
	_check(not editor.confirm_button.disabled, "reviewed permission revision may be tested")
	editor.close_editor()
	_check(not runtime.editor_open and not editor.is_processing(), "closing releases modal input and simulation")
	_check(editor._preview_root == null and editor.preview_viewport.render_target_update_mode == SubViewport.UPDATE_DISABLED, "closing releases private scene and stops rendering")
	_finish(previous_manual, previous_save)

func _check(condition: bool, description: String) -> void:
	checks += 1
	if condition:
		return
	failures += 1
	push_error("Invention editor smoke: " + description)

func _finish(previous_manual: String, previous_save: String) -> void:
	OS.set_environment("ENFRACTAL_MANUAL_INVENTION", previous_manual)
	OS.set_environment("ENFRACTAL_INVENTION_SAVE", previous_save)
	for path in [SAVE, EXPORT, OVERSIZE]:
		DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	print("Invention editor smoke: %d checks, %d failures" % [checks, failures])
	quit(0 if failures == 0 else 1)
