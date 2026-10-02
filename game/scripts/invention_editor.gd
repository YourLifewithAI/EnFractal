extends CanvasLayer
## Manual, source-first invention editor. The only publication path is the
## runtime's host-owned command service; this layer never mutates authority.

const COMPILER = preload("res://scripts/creation_compiler.gd")
const VISUALS = preload("res://scripts/creation_visuals.gd")
const EXECUTION = preload("res://scripts/creation_execution.gd")
const MAX_HISTORY := 32
const PINE := Color("172e2a")
const DEEP := Color("0d2024")
const PANEL := Color("203c38")
const PANEL_LIGHT := Color("2b4b45")
const STONE := Color("e9e6d8")
const SAGE := Color("acc8b3")
const MUTED := Color("9cb4ae")
const COPPER := Color("d9a879")
const ERROR := Color("f4b6a7")

var runtime
var draft: Dictionary = {}
var editing_instance_id := ""
var expected_revision := -1
var expected_permission_revision := -1
var _snapshot: Dictionary = {}
var _undo: Array = []
var _redo: Array = []
var _tested_fingerprint := ""
var _tested_artifact: Dictionary = {}
var _selected_part := 0
var _selected_node := 0
var _selected_edge := 0
var _building_ui := false
var _preview_root: Node3D
var _preview_assembly: Node3D
var _preview_dummy: Node3D
var _preview_active := false
var _preview_effects: Array = []
var _preview_plan_ids: Array = []
var _preview_dummy_velocity := Vector3.ZERO
var _preview_glide_speed := 0.0
var _preview_outline: Node3D
var _preview_container: SubViewportContainer
var _orbit_yaw_deg := 40.0
var _orbit_pitch_deg := 24.0
var _orbit_distance := 5.0
var _orbit_min_distance := 1.5
var _orbit_max_distance := 16.0
var _orbit_focus := Vector3.ZERO
var _orbit_user_modified := false
var _orbit_dragging := false
var _move_mode := false
var _move_dragging := false
var _move_part_index := -1
var _move_offset := Vector2.ZERO
var _move_changed := false
var _move_button: CheckButton
var _suspended_new_source: Dictionary = {}
var _suspended_new_placement: Dictionary = {}
var _suspended_undo: Array = []
var _suspended_redo: Array = []
var _suspended_selection := [0, 0, 0]
var _discard_new_draft_on_close := false

var ui: Control
var name_input: LineEdit
var mount_picker: OptionButton
var template_picker: OptionButton
var part_list: ItemList
var node_list: ItemList
var edge_list: ItemList
var part_form: VBoxContainer
var node_form: VBoxContainer
var wire_form: VBoxContainer
var access_form: VBoxContainer
var preview_viewport: SubViewport
var preview_caption: Label
var status_label: Label
var confirm_button: Button
var remove_button: Button
var undo_button: Button
var redo_button: Button
var placement_controls: Dictionary = {}
var import_dialog: FileDialog
var export_dialog: FileDialog
var remove_dialog: ConfirmationDialog


func _ready() -> void:
	layer = 40
	_build_ui()
	ui.visible = false
	set_process(false)


func open_editor(instance_id := "") -> bool:
	if runtime == null or runtime.authority == null:
		return false
	var snapshot: Dictionary = runtime.authority.snapshot("local_player")
	if not snapshot.get("ok", false):
		_show_status(str(snapshot.get("message", "Workshop source is unavailable.")), true)
		return false
	_snapshot = snapshot.duplicate(true)
	expected_revision = int(snapshot["revision"])
	expected_permission_revision = int(snapshot["permission_revision"])
	editing_instance_id = instance_id
	if not instance_id.is_empty():
		if not snapshot["instances"].has(instance_id):
			_show_status("That invention is no longer available. Reopen the workshop.", true)
			return false
		var live: Dictionary = snapshot["instances"][instance_id]
		draft = live["source"].duplicate(true)
		_set_placement({"x_m": float(live["position_m"][0]), "z_m": float(live["position_m"][2]), "yaw_deg": float(live["yaw_deg"])})
		_undo.clear()
		_redo.clear()
		_selected_part = 0
		_selected_node = 0
		_selected_edge = 0
	else:
		if not _suspended_new_source.is_empty():
			draft = _suspended_new_source.duplicate(true)
			_set_placement(_suspended_new_placement)
			_undo = _suspended_undo.duplicate(true)
			_redo = _suspended_redo.duplicate(true)
			_selected_part = int(_suspended_selection[0])
			_selected_node = int(_suspended_selection[1])
			_selected_edge = int(_suspended_selection[2])
		else:
			var templates: Array = COMPILER.templates()
			draft = templates[0].duplicate(true) if not templates.is_empty() else _blank_source()
			_set_placement(runtime.default_placement())
			_undo.clear()
			_redo.clear()
			_selected_part = 0
			_selected_node = 0
			_selected_edge = 0
	_tested_fingerprint = ""
	_tested_artifact.clear()
	_preview_active = false
	_orbit_user_modified = false
	_orbit_dragging = false
	_move_mode = false
	_move_dragging = false
	if _move_button != null:
		_move_button.set_pressed_no_signal(false)
	ui.visible = true
	preview_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	runtime.set_editor_open(true)
	_refresh_ui()
	_refresh_preview()
	_show_status("Edit the source, run Test, then confirm it in the world.")
	set_process(true)
	return true


func close_editor() -> void:
	if ui == null or not ui.visible:
		return
	if editing_instance_id.is_empty() and not _discard_new_draft_on_close:
		_suspended_new_source = draft.duplicate(true)
		_suspended_new_placement = _placement()
		_suspended_undo = _undo.duplicate(true)
		_suspended_redo = _redo.duplicate(true)
		_suspended_selection = [_selected_part, _selected_node, _selected_edge]
	elif _discard_new_draft_on_close:
		_suspended_new_source.clear()
		_suspended_new_placement.clear()
		_suspended_undo.clear()
		_suspended_redo.clear()
	_discard_new_draft_on_close = false
	ui.visible = false
	preview_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	_clear_preview()
	set_process(false)
	if runtime != null:
		runtime.set_editor_open(false)


func current_source() -> Dictionary:
	return draft.duplicate(true)


func validate_draft() -> Dictionary:
	return COMPILER.compile(current_source())


func _unhandled_input(event: InputEvent) -> void:
	if ui == null or not ui.visible or not event is InputEventKey or not event.pressed:
		return
	if event.keycode == KEY_ESCAPE:
		close_editor()
	elif event.ctrl_pressed and event.keycode == KEY_Z:
		if event.shift_pressed:
			_redo_draft()
		else:
			_undo_draft()
	elif event.ctrl_pressed and event.keycode == KEY_Y:
		_redo_draft()
	else:
		return
	get_viewport().set_input_as_handled()


func _process(delta: float) -> void:
	if not _preview_active or _preview_assembly == null:
		return
	var upward_wind := false
	for index in range(_preview_effects.size() - 1, -1, -1):
		var effect: Dictionary = _preview_effects[index]
		var active_step: float = minf(delta, float(effect["remaining"]))
		var remaining: float = maxf(0.0, float(effect["remaining"]) - active_step)
		effect["remaining"] = remaining
		match effect["op"]:
			"spin":
				var part: Node3D = effect["part"]
				if is_instance_valid(part):
					effect["angle"] = float(effect["angle"]) + float(effect["speed_rpm"]) * TAU / 60.0 * active_step
					var authored: Basis = part.get_meta("authored_basis")
					part.basis = authored * Basis(Vector3.UP, float(effect["angle"]))
			"wind":
				var direction: Vector3 = VISUALS.vector(effect["direction"])
				upward_wind = upward_wind or direction.y > 0.0
				_preview_dummy_velocity += direction * float(effect["acceleration_mps2"]) * active_step
				_preview_dummy_velocity = _preview_dummy_velocity.limit_length(7.0)
		if remaining <= 0.0:
			if effect["op"] == "light" and is_instance_valid(effect["lamp"]):
				(effect["lamp"] as OmniLight3D).light_energy = 0.0
			_preview_effects.remove_at(index)
	if _preview_dummy != null:
		if not upward_wind:
			_preview_dummy_velocity.y -= 9.8 * delta
		if _preview_glide_speed > 0.0:
			_preview_dummy_velocity.y = maxf(_preview_dummy_velocity.y, -_preview_glide_speed)
		_preview_dummy.position += _preview_dummy_velocity * delta
		_preview_dummy.position.x = clampf(_preview_dummy.position.x, -3.0, 3.0)
		_preview_dummy.position.z = clampf(_preview_dummy.position.z, -3.0, 3.0)
		if _preview_dummy.position.y > 3.5:
			_preview_dummy.position.y = 3.5
			_preview_dummy_velocity.y = 0.0
		if _preview_dummy.position.y < 0.12:
			_preview_dummy.position.y = 0.12
			_preview_dummy_velocity.y = 0.0
	if _preview_effects.is_empty() and (_preview_dummy == null or _preview_dummy.position.y <= 0.13):
		_preview_active = false


func _build_ui() -> void:
	ui = Control.new()
	ui.name = "InventionEditor"
	ui.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	ui.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(ui)
	var backdrop := ColorRect.new()
	backdrop.color = Color(0.035, 0.085, 0.09, 0.96)
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	ui.add_child(backdrop)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for edge in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + edge, 16)
	ui.add_child(margin)
	var shell := VBoxContainer.new()
	shell.add_theme_constant_override("separation", 11)
	margin.add_child(shell)
	_build_header(shell)
	_build_toolbar(shell)
	var main_scroll := ScrollContainer.new()
	main_scroll.name = "EditorBodyScroll"
	main_scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	main_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	main_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	main_scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	shell.add_child(main_scroll)
	var main := HBoxContainer.new()
	main.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	main.custom_minimum_size.y = 400
	main.add_theme_constant_override("separation", 10)
	main_scroll.add_child(main)
	_build_catalog(main)
	_build_preview(main)
	_build_inspector(main)
	_build_footer(shell)
	_build_dialogs()


func _build_header(shell: VBoxContainer) -> void:
	var row := HBoxContainer.new()
	row.custom_minimum_size.y = 47
	shell.add_child(row)
	var titles := VBoxContainer.new()
	titles.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(titles)
	titles.add_child(_label("Invention workshop", 26, STONE))
	titles.add_child(_label("Shape a place, test its behavior, then bring it into the world.", 13, MUTED))
	var close := _button("Close", false)
	close.name = "CloseButton"
	close.pressed.connect(close_editor)
	row.add_child(close)


func _build_toolbar(shell: VBoxContainer) -> void:
	var row := HFlowContainer.new()
	row.add_theme_constant_override("separation", 9)
	shell.add_child(row)
	row.add_child(_label("Start from", 13, MUTED))
	template_picker = _option()
	template_picker.name = "TemplatePicker"
	template_picker.custom_minimum_size.x = 170
	template_picker.add_item("Blank design")
	for source in COMPILER.templates():
		template_picker.add_item(str(source["name"]))
	template_picker.item_selected.connect(_on_template_chosen)
	row.add_child(template_picker)
	row.add_child(_label("Name", 13, MUTED))
	name_input = _line("NameInput", 190)
	name_input.max_length = 64
	name_input.text_submitted.connect(func(_text: String): _commit_name())
	name_input.focus_exited.connect(_commit_name)
	row.add_child(name_input)
	row.add_child(_label("Mount", 13, MUTED))
	mount_picker = _option()
	mount_picker.name = "MountPicker"
	mount_picker.custom_minimum_size.x = 105
	for mount in COMPILER.registry()["mounts"]:
		mount_picker.add_item(str(mount).capitalize())
	mount_picker.item_selected.connect(_on_mount_chosen)
	row.add_child(mount_picker)
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(spacer)
	undo_button = _button("Undo", false)
	undo_button.name = "UndoButton"
	undo_button.pressed.connect(_undo_draft)
	row.add_child(undo_button)
	redo_button = _button("Redo", false)
	redo_button.name = "RedoButton"
	redo_button.pressed.connect(_redo_draft)
	row.add_child(redo_button)
	var import_button := _button("Import", false)
	import_button.name = "ImportButton"
	import_button.pressed.connect(func(): import_dialog.popup_centered_ratio(0.62))
	row.add_child(import_button)
	var export_button := _button("Export", false)
	export_button.name = "ExportButton"
	export_button.pressed.connect(_request_export)
	row.add_child(export_button)


func _build_catalog(main: HBoxContainer) -> void:
	var card := _card()
	card.custom_minimum_size.x = 255
	main.add_child(card)
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 7)
	card.add_child(body)
	body.add_child(_label("Parts", 18, STONE))
	part_list = _item_list("PartList", 74)
	part_list.item_selected.connect(_select_part)
	body.add_child(part_list)
	body.add_child(_pair_buttons("Add part", "Remove", _add_part, _remove_part))
	body.add_child(_rule())
	body.add_child(_label("Behaviors", 18, STONE))
	node_list = _item_list("BehaviorList", 74)
	node_list.item_selected.connect(_select_node)
	body.add_child(node_list)
	body.add_child(_pair_buttons("Add behavior", "Remove", _add_node, _remove_node))
	body.add_child(_rule())
	body.add_child(_label("Wires", 18, STONE))
	edge_list = _item_list("WireList", 58)
	edge_list.item_selected.connect(func(index: int): _selected_edge = index; _render_wire_form())
	body.add_child(edge_list)
	body.add_child(_pair_buttons("Add wire", "Remove", _add_edge, _remove_edge))


func _build_preview(main: HBoxContainer) -> void:
	var card := _card()
	card.custom_minimum_size.x = 290
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	main.add_child(card)
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 9)
	card.add_child(body)
	var preview_heading := HBoxContainer.new()
	body.add_child(preview_heading)
	var heading := _label("Design study", 18, STONE)
	heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	preview_heading.add_child(heading)
	_move_button = CheckButton.new()
	_move_button.name = "MovePartMode"
	_move_button.text = "Move part"
	_move_button.add_theme_color_override("font_color", SAGE)
	_move_button.toggled.connect(func(enabled: bool): _move_mode = enabled)
	preview_heading.add_child(_move_button)
	var frame := PanelContainer.new()
	frame.size_flags_vertical = Control.SIZE_EXPAND_FILL
	frame.add_theme_stylebox_override("panel", _style(DEEP, Color("365956"), 1, 9))
	body.add_child(frame)
	var container := SubViewportContainer.new()
	_preview_container = container
	container.stretch = true
	container.mouse_filter = Control.MOUSE_FILTER_STOP
	container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	container.size_flags_vertical = Control.SIZE_EXPAND_FILL
	container.gui_input.connect(_on_preview_input)
	container.mouse_exited.connect(func():
		_orbit_dragging = false
		_end_part_move())
	frame.add_child(container)
	preview_viewport = SubViewport.new()
	preview_viewport.name = "IsolatedPreview"
	preview_viewport.size = Vector2i(320, 240)
	preview_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	preview_viewport.own_world_3d = true
	container.add_child(preview_viewport)
	_setup_preview_world()
	preview_caption = _label("Private study. No world effect until you confirm.", 12, MUTED)
	preview_caption.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.add_child(preview_caption)
	body.add_child(_label("Placement", 15, STONE))
	var placement := HBoxContainer.new()
	placement.add_theme_constant_override("separation", 6)
	body.add_child(placement)
	for key in ["x_m", "z_m", "yaw_deg"]:
		var text := "X" if key == "x_m" else "Z" if key == "z_m" else "Turn"
		placement.add_child(_label(text, 12, MUTED))
		var spin := _spin(-60.0 if key == "x_m" else 290.0 if key == "z_m" else -180.0,
			60.0 if key == "x_m" else 410.0 if key == "z_m" else 180.0, 0.5 if key != "yaw_deg" else 5.0)
		spin.name = "Placement" + text
		spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		spin.value_changed.connect(func(_value: float): _placement_changed())
		placement_controls[key] = spin
		placement.add_child(spin)


func _build_inspector(main: HBoxContainer) -> void:
	var card := _card()
	card.custom_minimum_size.x = 360
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	main.add_child(card)
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 8)
	card.add_child(body)
	body.add_child(_label("Details", 18, STONE))
	var tabs := TabContainer.new()
	tabs.name = "DetailTabs"
	tabs.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_child(tabs)
	part_form = _scroll_form(tabs, "Part")
	node_form = _scroll_form(tabs, "Behavior")
	wire_form = _scroll_form(tabs, "Wire")
	access_form = _scroll_form(tabs, "Access")


func _build_footer(shell: VBoxContainer) -> void:
	var row := HBoxContainer.new()
	row.custom_minimum_size.y = 48
	row.add_theme_constant_override("separation", 9)
	shell.add_child(row)
	status_label = _label("", 13, SAGE)
	status_label.name = "EditorStatus"
	status_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	row.add_child(status_label)
	var test := _button("Test in private study", true)
	test.name = "TestButton"
	test.pressed.connect(_test_draft)
	row.add_child(test)
	confirm_button = _button("Place in world", true)
	confirm_button.name = "ConfirmButton"
	confirm_button.pressed.connect(_confirm_draft)
	row.add_child(confirm_button)
	remove_button = _button("Remove invention", false)
	remove_button.name = "RemoveButton"
	remove_button.pressed.connect(func(): remove_dialog.popup_centered())
	row.add_child(remove_button)


func _build_dialogs() -> void:
	import_dialog = FileDialog.new()
	import_dialog.name = "ImportDialog"
	import_dialog.access = FileDialog.ACCESS_FILESYSTEM
	import_dialog.file_mode = FileDialog.FILE_MODE_OPEN_FILE
	import_dialog.filters = PackedStringArray(["*.json ; EnFractal invention"])
	import_dialog.file_selected.connect(_import_file)
	ui.add_child(import_dialog)
	export_dialog = FileDialog.new()
	export_dialog.name = "ExportDialog"
	export_dialog.access = FileDialog.ACCESS_FILESYSTEM
	export_dialog.file_mode = FileDialog.FILE_MODE_SAVE_FILE
	export_dialog.filters = PackedStringArray(["*.json ; EnFractal invention"])
	export_dialog.file_selected.connect(_export_file)
	ui.add_child(export_dialog)
	remove_dialog = ConfirmationDialog.new()
	remove_dialog.name = "RemoveDialog"
	remove_dialog.title = "Remove this invention?"
	remove_dialog.dialog_text = "This removes the placed invention from the local world. Your exported JSON, if any, stays on disk."
	remove_dialog.confirmed.connect(_confirm_remove)
	ui.add_child(remove_dialog)


func _style(color: Color, border := Color.TRANSPARENT, border_width := 0, radius := 7) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = color
	box.border_color = border
	box.set_border_width_all(border_width)
	box.set_corner_radius_all(radius)
	box.content_margin_left = 9
	box.content_margin_right = 9
	box.content_margin_top = 7
	box.content_margin_bottom = 7
	return box


func _card() -> PanelContainer:
	var card := PanelContainer.new()
	card.add_theme_stylebox_override("panel", _style(PINE, Color("34564e"), 1, 10))
	return card


func _label(text: String, size := 14, color := STONE) -> Label:
	var result := Label.new()
	result.text = text
	result.add_theme_font_size_override("font_size", size)
	result.add_theme_color_override("font_color", color)
	return result


func _button(text: String, emphasis: bool) -> Button:
	var result := Button.new()
	result.text = text
	result.custom_minimum_size.y = 34
	result.add_theme_color_override("font_color", DEEP if emphasis else STONE)
	result.add_theme_color_override("font_hover_color", DEEP if emphasis else STONE)
	result.add_theme_stylebox_override("normal", _style(COPPER if emphasis else PANEL_LIGHT, Color("638277"), 1))
	result.add_theme_stylebox_override("hover", _style(Color("e8bc8e") if emphasis else Color("3a6058"), COPPER, 1))
	result.add_theme_stylebox_override("pressed", _style(Color("ba855c") if emphasis else Color("1a3431"), COPPER, 1))
	result.add_theme_stylebox_override("focus", _style(Color.TRANSPARENT, COPPER, 2))
	return result


func _line(node_name: String, width := 0) -> LineEdit:
	var result := LineEdit.new()
	result.name = node_name
	result.custom_minimum_size = Vector2(width, 32)
	result.add_theme_color_override("font_color", STONE)
	result.add_theme_color_override("font_placeholder_color", MUTED)
	result.add_theme_stylebox_override("normal", _style(DEEP, Color("44645f"), 1, 5))
	result.add_theme_stylebox_override("focus", _style(DEEP, COPPER, 2, 5))
	return result


func _option() -> OptionButton:
	var result := OptionButton.new()
	result.custom_minimum_size.y = 32
	result.add_theme_color_override("font_color", STONE)
	result.add_theme_stylebox_override("normal", _style(DEEP, Color("44645f"), 1, 5))
	result.add_theme_stylebox_override("hover", _style(PANEL_LIGHT, COPPER, 1, 5))
	result.add_theme_stylebox_override("focus", _style(Color.TRANSPARENT, COPPER, 2, 5))
	return result


func _spin(minimum: float, maximum: float, step: float) -> SpinBox:
	var result := SpinBox.new()
	result.min_value = minimum
	result.max_value = maximum
	result.step = step
	result.custom_minimum_size = Vector2(62, 31)
	result.get_line_edit().add_theme_color_override("font_color", STONE)
	result.get_line_edit().add_theme_stylebox_override("normal", _style(DEEP, Color("44645f"), 1, 5))
	result.get_line_edit().add_theme_stylebox_override("focus", _style(DEEP, COPPER, 2, 5))
	return result


func _item_list(node_name: String, height: float) -> ItemList:
	var result := ItemList.new()
	result.name = node_name
	result.custom_minimum_size.y = height
	result.size_flags_vertical = Control.SIZE_EXPAND_FILL
	result.add_theme_stylebox_override("panel", _style(DEEP, Color("365956"), 1, 5))
	result.add_theme_color_override("font_color", STONE)
	result.add_theme_color_override("font_selected_color", STONE)
	result.add_theme_stylebox_override("selected", _style(PANEL_LIGHT, COPPER, 1, 3))
	result.add_theme_stylebox_override("selected_focus", _style(PANEL_LIGHT, COPPER, 1, 3))
	return result


func _pair_buttons(add_text: String, remove_text: String, add_action: Callable, remove_action: Callable) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var add := _button(add_text, false)
	add.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add.pressed.connect(add_action)
	row.add_child(add)
	var remove := _button(remove_text, false)
	remove.pressed.connect(remove_action)
	row.add_child(remove)
	return row


func _rule() -> HSeparator:
	var separator := HSeparator.new()
	separator.add_theme_stylebox_override("separator", _style(Color("44645b"), Color.TRANSPARENT, 0, 0))
	return separator


func _scroll_form(tabs: TabContainer, title: String) -> VBoxContainer:
	var scroll := ScrollContainer.new()
	scroll.name = title
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	tabs.add_child(scroll)
	var margin := MarginContainer.new()
	margin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for edge in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + edge, 8)
	scroll.add_child(margin)
	var form := VBoxContainer.new()
	form.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	form.add_theme_constant_override("separation", 9)
	margin.add_child(form)
	return form


func _clear_form(form: VBoxContainer) -> void:
	for child in form.get_children():
		form.remove_child(child)
		child.queue_free()


func _form_text(form: VBoxContainer, label_text: String, value: String, commit: Callable, node_name := "") -> LineEdit:
	form.add_child(_label(label_text, 12, MUTED))
	var field := _line(node_name)
	field.text = value
	field.text_submitted.connect(func(_text: String): commit.call(field.text))
	field.focus_exited.connect(func(): commit.call(field.text))
	form.add_child(field)
	return field


func _form_option(form: VBoxContainer, label_text: String, choices: Array, value: String, commit: Callable, node_name := "") -> OptionButton:
	form.add_child(_label(label_text, 12, MUTED))
	var field := _option()
	field.name = node_name
	for choice in choices:
		field.add_item(str(choice).replace("_", " ").capitalize())
		field.set_item_metadata(field.item_count - 1, choice)
		if choice == value:
			field.select(field.item_count - 1)
	field.item_selected.connect(func(index: int): commit.call(field.get_item_metadata(index)))
	form.add_child(field)
	return field


func _form_number(form: VBoxContainer, label_text: String, value: float, minimum: float, maximum: float, step: float, commit: Callable, node_name := "") -> SpinBox:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	var title := _label(label_text, 12, MUTED)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(title)
	var field := _spin(minimum, maximum, step)
	field.name = node_name
	field.value = value
	field.value_changed.connect(func(number: float): commit.call(number))
	row.add_child(field)
	form.add_child(row)
	return field


func _form_vector(form: VBoxContainer, label_text: String, values: Array, minimum: float, maximum: float, step: float, commit: Callable) -> void:
	form.add_child(_label(label_text, 12, MUTED))
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	form.add_child(row)
	for axis in range(3):
		row.add_child(_label(["X", "Y", "Z"][axis], 12, SAGE))
		var field := _spin(minimum, maximum, step)
		field.name = "%s_%s" % [label_text.replace(" ", ""), ["X", "Y", "Z"][axis]]
		field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		field.value = float(values[axis])
		field.value_changed.connect(func(number: float): commit.call(axis, number))
		row.add_child(field)


func _refresh_ui() -> void:
	_building_ui = true
	name_input.text = str(draft.get("name", ""))
	mount_picker.select(0 if draft.get("mount") == "ground" else 1)
	confirm_button.text = "Save revision" if not editing_instance_id.is_empty() else "Place in world"
	confirm_button.disabled = _tested_fingerprint.is_empty()
	remove_button.visible = not editing_instance_id.is_empty()
	undo_button.disabled = _undo.is_empty()
	redo_button.disabled = _redo.is_empty()
	part_list.clear()
	for part in draft.get("parts", []):
		part_list.add_item("%s  ·  %s" % [part["id"], part["shape"]])
	if part_list.item_count > 0:
		_selected_part = clampi(_selected_part, 0, part_list.item_count - 1)
		part_list.select(_selected_part)
	node_list.clear()
	for node in draft.get("nodes", []):
		var definition: Dictionary = COMPILER.registry()["operations"].get(node["op"], {})
		node_list.add_item("%s  ·  %s" % [node["id"], definition.get("label", node["op"])])
	if node_list.item_count > 0:
		_selected_node = clampi(_selected_node, 0, node_list.item_count - 1)
		node_list.select(_selected_node)
	edge_list.clear()
	for edge in draft.get("edges", []):
		edge_list.add_item("%s  →  %s" % [edge["from"], edge["to"]])
	if edge_list.item_count > 0:
		_selected_edge = clampi(_selected_edge, 0, edge_list.item_count - 1)
		edge_list.select(_selected_edge)
	_render_part_form()
	_render_node_form()
	_render_wire_form()
	_render_access_form()
	_building_ui = false


func _render_part_form() -> void:
	_clear_form(part_form)
	if _selected_part < 0 or _selected_part >= draft.get("parts", []).size():
		part_form.add_child(_label("Add a part to give the invention a form.", 13, MUTED))
		return
	var index := _selected_part
	var part: Dictionary = draft["parts"][index]
	part_form.add_child(_label("Part %d of %d" % [index + 1, draft["parts"].size()], 15, STONE))
	_form_text(part_form, "Part ID", str(part["id"]), func(value: String): _rename_part(index, value), "PartIdInput")
	_form_option(part_form, "Shape", COMPILER.registry()["shapes"], str(part["shape"]), func(value: String): _set_part_field(index, "shape", value), "ShapePicker")
	_form_option(part_form, "Material", COMPILER.registry()["materials"], str(part["material"]), func(value: String): _set_part_field(index, "material", value), "MaterialPicker")
	_form_vector(part_form, "Position m", part["position_m"], -4.0, 4.0, 0.05, func(axis: int, value: float): _set_part_vector(index, "position_m", axis, value))
	_form_vector(part_form, "Rotation degrees", part["rotation_deg"], -180.0, 180.0, 1.0, func(axis: int, value: float): _set_part_vector(index, "rotation_deg", axis, value))
	_form_vector(part_form, "Size m", part["size_m"], 0.1, 4.0, 0.05, func(axis: int, value: float): _set_part_vector(index, "size_m", axis, value))
	var hint := _label("Ground parts must stay above their origin, even when rotated.", 12, MUTED)
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	part_form.add_child(hint)


func _select_part(index: int) -> void:
	_selected_part = index
	_render_part_form()
	_update_selection_outline()


func _select_node(index: int) -> void:
	_selected_node = index
	_render_node_form()
	if index < 0 or index >= draft.get("nodes", []).size():
		return
	var target_id: String = draft["nodes"][index]["part_id"]
	for part_index in range(draft["parts"].size()):
		if draft["parts"][part_index]["id"] == target_id:
			_selected_part = part_index
			part_list.select(part_index)
			_render_part_form()
			_update_selection_outline()
			break


func _render_node_form() -> void:
	_clear_form(node_form)
	if _selected_node < 0 or _selected_node >= draft.get("nodes", []).size():
		node_form.add_child(_label("Add a behavior to make the invention respond.", 13, MUTED))
		return
	var index := _selected_node
	var node: Dictionary = draft["nodes"][index]
	var operations: Dictionary = COMPILER.registry()["operations"]
	node_form.add_child(_label("Behavior %d of %d" % [index + 1, draft["nodes"].size()], 15, STONE))
	_form_text(node_form, "Behavior ID", str(node["id"]), func(value: String): _rename_node(index, value), "BehaviorIdInput")
	_form_option(node_form, "Operation", operations.keys(), str(node["op"]), func(value: String): _change_node_operation(index, value), "OperationPicker")
	var part_ids: Array = []
	for part in draft["parts"]:
		part_ids.append(part["id"])
	_form_option(node_form, "Attached to", part_ids, str(node["part_id"]), func(value: String): _set_node_field(index, "part_id", value), "BehaviorPartPicker")
	var definition: Dictionary = operations.get(node["op"], {})
	if definition.get("trigger", false):
		node_form.add_child(_label("This starts a behavior chain.", 12, SAGE))
	elif node["op"] == "glide":
		node_form.add_child(_label("Passive while equipped by a consenting avatar.", 12, SAGE))
	else:
		node_form.add_child(_label("Connect a trigger with a wire to run this action.", 12, SAGE))
	for key in definition.get("params", {}):
		var rule: Dictionary = definition["params"][key]
		var value: Variant = node["params"].get(key, rule["default"])
		if rule["type"] == "vector3":
			_form_vector(node_form, str(key).replace("_", " ").capitalize(), value, float(rule["min"]), float(rule["max"]), float(rule["step"]), func(axis: int, number: float): _set_node_vector(index, key, axis, number))
			var note := _label("Direction must have length 1. Test will check it.", 12, MUTED)
			node_form.add_child(note)
			if rule.get("normalized", false):
				var row := HBoxContainer.new()
				var normalize := _button("Normalize direction", false)
				normalize.name = "NormalizeDirectionButton"
				normalize.pressed.connect(func(): _normalize_node_vector(index, key))
				row.add_child(normalize)
				var up := _button("Point up", false)
				up.name = "PointUpButton"
				up.pressed.connect(func(): _set_node_direction_up(index, key))
				row.add_child(up)
				node_form.add_child(row)
		else:
			_form_number(node_form, str(key).replace("_", " ").capitalize() + (" (" + str(rule["unit"]) + ")" if not str(rule["unit"]).is_empty() else ""), float(value), float(rule["min"]), float(rule["max"]), float(rule["step"]), func(number: float): _set_node_param(index, key, number), "Param_" + str(key))


func _render_wire_form() -> void:
	_clear_form(wire_form)
	var nodes: Array = draft.get("nodes", [])
	if nodes.size() < 2:
		wire_form.add_child(_label("Add two behaviors before wiring them together.", 13, MUTED))
		return
	wire_form.add_child(_label("Connect an event to an action", 15, STONE))
	var node_ids: Array = []
	for node in nodes:
		node_ids.append(node["id"])
	var from_id: String = str(nodes[0]["id"])
	var to_id: String = str(nodes[1]["id"])
	if _selected_edge < draft["edges"].size():
		from_id = str(draft["edges"][_selected_edge]["from"])
		to_id = str(draft["edges"][_selected_edge]["to"])
	_form_option(wire_form, "From", node_ids, from_id, func(_value: String): pass, "WireFromPicker")
	_form_option(wire_form, "To", node_ids, to_id, func(_value: String): pass, "WireToPicker")
	var guidance := _label("Triggers cannot receive wires. Wires cannot form a cycle.", 12, MUTED)
	guidance.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	wire_form.add_child(guidance)


func _render_access_form() -> void:
	_clear_form(access_form)
	access_form.add_child(_label("Local workshop access", 15, STONE))
	var note := _label("These controls affect the live local workshop. Preview effects never bypass consent or plot rules.", 12, MUTED)
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	access_form.add_child(note)
	var roles: Dictionary = _snapshot.get("roles", {})
	var consent: Dictionary = _snapshot.get("consent", {})
	var local := CheckButton.new()
	local.text = "My avatar can receive effects"
	local.set_pressed_no_signal(bool(consent.get("local_player", true)))
	local.toggled.connect(func(enabled: bool): _change_access("local_consent", enabled))
	access_form.add_child(local)
	var guest := CheckButton.new()
	guest.text = "Local guest mannequin can receive effects"
	guest.set_pressed_no_signal(bool(consent.get("guest_player", false)))
	guest.toggled.connect(func(enabled: bool): _change_access("guest_consent", enabled))
	access_form.add_child(guest)
	var builder := CheckButton.new()
	builder.text = "Guest may build their own inventions"
	builder.set_pressed_no_signal(roles.get("guest_player", "visitor") == "editor")
	builder.toggled.connect(func(enabled: bool): _change_access("guest_builder", enabled))
	access_form.add_child(builder)
	access_form.add_child(_rule())
	access_form.add_child(_label("World revision %d  ·  permissions %d" % [expected_revision, expected_permission_revision], 12, SAGE))
	access_form.add_child(_label("Permission changes require another Test before placing.", 12, MUTED))
	var refresh := _button("Review current world", false)
	refresh.name = "RefreshWorldButton"
	refresh.pressed.connect(refresh_world)
	access_form.add_child(refresh)


func _blank_source() -> Dictionary:
	return {"schema": "enfractal.creation", "version": 1, "name": "New invention", "seed": 1, "mount": "ground",
		"parts": [{"id": "base", "shape": "box", "position_m": [0.0, 0.5, 0.0], "rotation_deg": [0.0, 0.0, 0.0], "size_m": [1.0, 1.0, 1.0], "material": "stone"}],
		"nodes": [], "edges": []}


func _remember() -> void:
	_undo.append(draft.duplicate(true))
	if _undo.size() > MAX_HISTORY:
		_undo.pop_front()
	_redo.clear()


func _changed(rebuild: bool) -> void:
	_tested_fingerprint = ""
	_tested_artifact.clear()
	_preview_active = false
	confirm_button.disabled = true
	if rebuild:
		_refresh_ui()
	else:
		undo_button.disabled = false
		redo_button.disabled = true
	_refresh_preview()
	var result: Dictionary = validate_draft()
	if result.get("ok", false):
		_show_status("Draft changed. Run Test before confirming.")
	else:
		_show_status("%s: %s" % [result.get("path", "$"), result.get("message", "Review this draft.")], true)


func _undo_draft() -> void:
	if _undo.is_empty():
		return
	_redo.append(draft.duplicate(true))
	draft = _undo.pop_back()
	_changed(true)


func _redo_draft() -> void:
	if _redo.is_empty():
		return
	_undo.append(draft.duplicate(true))
	draft = _redo.pop_back()
	_changed(true)


func _new_id(prefix: String, items: Array) -> String:
	var used: Dictionary = {}
	for item in items:
		used[item["id"]] = true
	for suffix in range(1, 1000):
		var candidate := prefix + str(suffix)
		if not used.has(candidate):
			return candidate
	return prefix + "new"


func _on_template_chosen(index: int) -> void:
	if _building_ui:
		return
	var replacement: Dictionary
	if index == 0:
		replacement = _blank_source()
	else:
		var templates: Array = COMPILER.templates()
		if index - 1 >= templates.size():
			return
		replacement = templates[index - 1].duplicate(true)
	_remember()
	draft = replacement
	_selected_part = 0
	_selected_node = 0
	_selected_edge = 0
	_changed(true)


func _commit_name() -> void:
	if _building_ui or draft.is_empty() or name_input.text == draft["name"]:
		return
	_remember()
	draft["name"] = name_input.text
	_changed(false)


func _on_mount_chosen(index: int) -> void:
	if _building_ui:
		return
	var choice: String = COMPILER.registry()["mounts"][index]
	if draft["mount"] == choice:
		return
	_remember()
	draft["mount"] = choice
	_changed(false)


func _add_part() -> void:
	if draft["parts"].size() >= int(COMPILER.registry()["limits"]["parts"]):
		_show_status("This design already has 24 parts.", true)
		return
	_remember()
	var identifier := _new_id("part", draft["parts"])
	draft["parts"].append({"id": identifier, "shape": "box", "position_m": [0.0, 0.5, 0.0], "rotation_deg": [0.0, 0.0, 0.0], "size_m": [1.0, 1.0, 1.0], "material": "wood"})
	_selected_part = draft["parts"].size() - 1
	_changed(true)


func _remove_part() -> void:
	if draft["parts"].size() <= 1 or _selected_part >= draft["parts"].size():
		_show_status("Keep at least one part in the design.", true)
		return
	_remember()
	var identifier: String = draft["parts"][_selected_part]["id"]
	draft["parts"].remove_at(_selected_part)
	var removed_nodes: Dictionary = {}
	var kept_nodes: Array = []
	for node in draft["nodes"]:
		if node["part_id"] == identifier:
			removed_nodes[node["id"]] = true
		else:
			kept_nodes.append(node)
	draft["nodes"] = kept_nodes
	var kept_edges: Array = []
	for edge in draft["edges"]:
		if not removed_nodes.has(edge["from"]) and not removed_nodes.has(edge["to"]):
			kept_edges.append(edge)
	draft["edges"] = kept_edges
	_selected_part = maxi(0, _selected_part - 1)
	_selected_node = mini(_selected_node, maxi(0, kept_nodes.size() - 1))
	_changed(true)


func _rename_part(index: int, value: String) -> void:
	if index >= draft["parts"].size() or draft["parts"][index]["id"] == value:
		return
	_remember()
	var previous: String = draft["parts"][index]["id"]
	draft["parts"][index]["id"] = value
	for node in draft["nodes"]:
		if node["part_id"] == previous:
			node["part_id"] = value
	_changed(true)


func _set_part_field(index: int, key: String, value: Variant) -> void:
	if index >= draft["parts"].size() or draft["parts"][index][key] == value:
		return
	_remember()
	draft["parts"][index][key] = value
	_changed(key == "shape")


func _set_part_vector(index: int, key: String, axis: int, value: float) -> void:
	if index >= draft["parts"].size() or is_equal_approx(float(draft["parts"][index][key][axis]), value):
		return
	_remember()
	draft["parts"][index][key][axis] = value
	_changed(false)


func _default_params(op: String) -> Dictionary:
	var params: Dictionary = {}
	var definition: Dictionary = COMPILER.registry()["operations"][op]
	for key in definition["params"]:
		params[key] = definition["params"][key]["default"]
	return params


func _add_node() -> void:
	if draft["nodes"].size() >= int(COMPILER.registry()["limits"]["nodes"]):
		_show_status("This design already has 16 behaviors.", true)
		return
	_remember()
	var op := "light" if draft["nodes"].any(func(item: Dictionary): return item["op"] == "interact") else "interact"
	draft["nodes"].append({"id": _new_id("action", draft["nodes"]), "op": op, "part_id": draft["parts"][0]["id"], "params": _default_params(op)})
	_selected_node = draft["nodes"].size() - 1
	_changed(true)


func _remove_node() -> void:
	if _selected_node >= draft["nodes"].size():
		return
	_remember()
	var identifier: String = draft["nodes"][_selected_node]["id"]
	draft["nodes"].remove_at(_selected_node)
	var kept: Array = []
	for edge in draft["edges"]:
		if edge["from"] != identifier and edge["to"] != identifier:
			kept.append(edge)
	draft["edges"] = kept
	_selected_node = maxi(0, _selected_node - 1)
	_selected_edge = mini(_selected_edge, maxi(0, kept.size() - 1))
	_changed(true)


func _rename_node(index: int, value: String) -> void:
	if index >= draft["nodes"].size() or draft["nodes"][index]["id"] == value:
		return
	_remember()
	var previous: String = draft["nodes"][index]["id"]
	draft["nodes"][index]["id"] = value
	for edge in draft["edges"]:
		if edge["from"] == previous:
			edge["from"] = value
		if edge["to"] == previous:
			edge["to"] = value
	_changed(true)


func _change_node_operation(index: int, value: String) -> void:
	if index >= draft["nodes"].size() or draft["nodes"][index]["op"] == value:
		return
	_remember()
	draft["nodes"][index]["op"] = value
	draft["nodes"][index]["params"] = _default_params(value)
	_changed(true)


func _set_node_field(index: int, key: String, value: Variant) -> void:
	if index >= draft["nodes"].size() or draft["nodes"][index][key] == value:
		return
	_remember()
	draft["nodes"][index][key] = value
	_changed(false)
	if key == "part_id":
		_select_node(index)


func _set_node_param(index: int, key: String, value: float) -> void:
	if index >= draft["nodes"].size() or is_equal_approx(float(draft["nodes"][index]["params"][key]), value):
		return
	_remember()
	draft["nodes"][index]["params"][key] = value
	_changed(false)


func _set_node_vector(index: int, key: String, axis: int, value: float) -> void:
	if index >= draft["nodes"].size() or is_equal_approx(float(draft["nodes"][index]["params"][key][axis]), value):
		return
	_remember()
	draft["nodes"][index]["params"][key][axis] = value
	_changed(false)


func _normalize_node_vector(index: int, key: String) -> void:
	if index >= draft["nodes"].size():
		return
	var direction: Vector3 = VISUALS.vector(draft["nodes"][index]["params"][key])
	if direction.length_squared() < 0.000001:
		_show_status("Choose a nonzero direction before normalizing.", true)
		return
	_remember()
	direction = direction.normalized()
	draft["nodes"][index]["params"][key] = [direction.x, direction.y, direction.z]
	_changed(true)


func _set_node_direction_up(index: int, key: String) -> void:
	if index >= draft["nodes"].size():
		return
	_remember()
	draft["nodes"][index]["params"][key] = [0.0, 1.0, 0.0]
	_changed(true)


func _add_edge() -> void:
	if draft["edges"].size() >= int(COMPILER.registry()["limits"]["edges"]):
		_show_status("This design already has 32 wires.", true)
		return
	var from_picker: OptionButton = wire_form.get_node_or_null("WireFromPicker")
	var to_picker: OptionButton = wire_form.get_node_or_null("WireToPicker")
	if from_picker == null or to_picker == null:
		_show_status("Add two behaviors before adding a wire.", true)
		return
	var from_id: String = from_picker.get_item_metadata(from_picker.selected)
	var to_id: String = to_picker.get_item_metadata(to_picker.selected)
	if from_id == to_id:
		_show_status("A wire needs two different behaviors.", true)
		return
	for edge in draft["edges"]:
		if edge["from"] == from_id and edge["to"] == to_id:
			_show_status("That wire already exists.", true)
			return
	_remember()
	draft["edges"].append({"from": from_id, "to": to_id})
	_selected_edge = draft["edges"].size() - 1
	_changed(true)


func _remove_edge() -> void:
	if _selected_edge >= draft["edges"].size():
		return
	_remember()
	draft["edges"].remove_at(_selected_edge)
	_selected_edge = maxi(0, _selected_edge - 1)
	_changed(true)


func _set_placement(placement: Dictionary) -> void:
	_building_ui = true
	for key in ["x_m", "z_m", "yaw_deg"]:
		if placement_controls.has(key):
			placement_controls[key].value = float(placement.get(key, 0.0))
	_building_ui = false


func _placement() -> Dictionary:
	return {"x_m": float(placement_controls["x_m"].value), "z_m": float(placement_controls["z_m"].value), "yaw_deg": float(placement_controls["yaw_deg"].value)}


func _placement_changed() -> void:
	if _building_ui:
		return
	_tested_fingerprint = ""
	_tested_artifact.clear()
	_preview_active = false
	confirm_button.disabled = true
	_show_status("Placement changed. Run Test again before confirming.")


func _fingerprint() -> String:
	return COMPILER.canonical_json({"source": current_source(), "placement": _placement()}).sha256_text()


func _test_draft() -> void:
	_tested_fingerprint = ""
	_tested_artifact.clear()
	_preview_active = false
	confirm_button.disabled = true
	var result: Dictionary = validate_draft()
	if not result.get("ok", false):
		_show_status("%s: %s" % [result.get("path", "$"), result.get("message", "Review this draft.")], true)
		return
	var live: Dictionary = runtime.authority.snapshot("local_player")
	if not live.get("ok", false):
		_show_status(str(live.get("message", "The workshop is unavailable.")), true)
		return
	if int(live["revision"]) != expected_revision or int(live["permission_revision"]) != expected_permission_revision:
		_show_status("The world or its permissions changed. Keep or export this draft, then reopen before confirming.", true)
		return
	var preflight: Dictionary = runtime.preflight_draft(current_source(), editing_instance_id, _placement())
	if not preflight.get("ok", false):
		_show_status("Placement review — %s: %s" % [preflight.get("path", "World"), preflight.get("message", "Choose another place and Test again.")], true)
		return
	if int(preflight.get("revision", -1)) != expected_revision or int(preflight.get("permission_revision", -1)) != expected_permission_revision:
		_show_status("The world or its permissions changed during Test. Review the current world before confirming.", true)
		return
	_tested_artifact = result["artifact"].duplicate(true)
	_tested_fingerprint = _fingerprint()
	_refresh_preview()
	_apply_preview_effects()
	_preview_active = true
	confirm_button.disabled = false
	_show_status("Private test ready. Confirm to publish through the local workshop rules.")


func _confirm_draft() -> void:
	if _tested_fingerprint.is_empty() or _tested_fingerprint != _fingerprint():
		_show_status("This draft changed after Test. Run Test again.", true)
		return
	var compiled: Dictionary = validate_draft()
	if not compiled.get("ok", false) or compiled["artifact"]["hash"] != _tested_artifact.get("hash", ""):
		_show_status("This draft no longer matches the tested design. Run Test again.", true)
		return
	var result: Dictionary = runtime.commit_draft(current_source(), editing_instance_id, _placement(), expected_revision, expected_permission_revision)
	if not result.get("ok", false):
		_show_status("%s: %s" % [result.get("path", "World"), result.get("message", "The change was not saved.")], true)
		return
	var message := "Invention revised in the local world." if not editing_instance_id.is_empty() else "Invention placed in the local world."
	runtime.notice(message)
	_discard_new_draft_on_close = editing_instance_id.is_empty()
	close_editor()


func _confirm_remove() -> void:
	if editing_instance_id.is_empty():
		return
	var result: Dictionary = runtime.remove_creation(editing_instance_id, expected_revision, expected_permission_revision)
	if not result.get("ok", false):
		_show_status("%s: %s" % [result.get("path", "World"), result.get("message", "The invention was not removed.")], true)
		return
	runtime.notice("Invention removed from the local world.")
	close_editor()


func _change_access(kind: String, enabled: bool) -> void:
	var result: Dictionary
	match kind:
		"local_consent":
			result = runtime.set_local_consent(enabled)
		"guest_consent":
			result = runtime.set_guest_consent(enabled)
		"guest_builder":
			result = runtime.set_guest_builder(enabled)
	if not result.get("ok", false):
		_show_status(str(result.get("message", "Access did not change.")), true)
		_render_access_form()
		return
	if refresh_world():
		_show_status("Access updated. Run Test again before confirming this design.")


func refresh_world() -> bool:
	# Explicit recache preserves the draft. A changed live source must be
	# reopened so this editor cannot overwrite it by silently rebasing.
	var refreshed: Dictionary = runtime.authority.snapshot("local_player")
	if not refreshed.get("ok", false):
		_show_status(str(refreshed.get("message", "The workshop is unavailable.")), true)
		return false
	if not editing_instance_id.is_empty():
		var old_instances: Dictionary = _snapshot.get("instances", {})
		var new_instances: Dictionary = refreshed.get("instances", {})
		if not old_instances.has(editing_instance_id) or not new_instances.has(editing_instance_id) or int(old_instances[editing_instance_id]["revision"]) != int(new_instances[editing_instance_id]["revision"]):
			_show_status("This placed invention changed. Export your draft, then reopen its current source before revising.", true)
			return false
	_snapshot = refreshed.duplicate(true)
	expected_revision = int(refreshed["revision"])
	expected_permission_revision = int(refreshed["permission_revision"])
	_tested_fingerprint = ""
	_tested_artifact.clear()
	_preview_active = false
	confirm_button.disabled = true
	_render_access_form()
	_show_status("World review updated. Your draft is unchanged; run Test again.")
	return true


func _request_export() -> void:
	var result: Dictionary = validate_draft()
	if not result.get("ok", false):
		_show_status("Fix %s before exporting: %s" % [result.get("path", "$"), result.get("message", "Invalid source.")], true)
		return
	export_dialog.current_file = str(draft["name"]).to_snake_case() + ".json"
	export_dialog.popup_centered_ratio(0.62)


func _import_file(path: String) -> void:
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		_show_status("Could not open that JSON file.", true)
		return
	if file.get_length() > int(COMPILER.registry()["limits"]["json_bytes"]):
		file.close()
		_show_status("JSON exceeds the 32 KiB invention limit.", true)
		return
	var text := file.get_as_text()
	file.close()
	if text.to_utf8_buffer().size() > int(COMPILER.registry()["limits"]["json_bytes"]):
		_show_status("JSON exceeds the 32 KiB invention limit.", true)
		return
	var parser := JSON.new()
	if parser.parse(text) != OK:
		_show_status("JSON line %d: %s" % [parser.get_error_line(), parser.get_error_message()], true)
		return
	var result: Dictionary = COMPILER.compile(parser.data)
	if not result.get("ok", false):
		_show_status("%s: %s" % [result.get("path", "$"), result.get("message", "Invalid invention.")], true)
		return
	_remember()
	draft = result["artifact"]["source"].duplicate(true)
	_selected_part = 0
	_selected_node = 0
	_selected_edge = 0
	_changed(true)
	_show_status("Imported a valid invention. Run Test before confirming.")


func _export_file(path: String) -> void:
	var result: Dictionary = validate_draft()
	if not result.get("ok", false):
		_show_status("Fix the draft before exporting.", true)
		return
	var text := JSON.stringify(result["artifact"]["source"], "\t") + "\n"
	if text.to_utf8_buffer().size() > int(COMPILER.registry()["limits"]["json_bytes"]):
		_show_status("JSON exceeds the 32 KiB invention limit.", true)
		return
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		_show_status("Could not save JSON at that location.", true)
		return
	file.store_string(text)
	file.close()
	_show_status("Exported portable invention JSON.")


func _show_status(message: String, is_error := false) -> void:
	if status_label == null:
		return
	status_label.text = message
	status_label.add_theme_color_override("font_color", ERROR if is_error else SAGE)


func _setup_preview_world() -> void:
	if _preview_root != null and is_instance_valid(_preview_root):
		return
	_preview_root = Node3D.new()
	_preview_root.name = "PrivateWorld"
	preview_viewport.add_child(_preview_root)
	var world := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("153135")
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("b5c5bd")
	environment.ambient_light_energy = 0.65
	world.environment = environment
	_preview_root.add_child(world)
	var light := DirectionalLight3D.new()
	light.rotation = Vector3(-0.75, -0.5, 0.0)
	light.light_color = Color("ffe2b6")
	light.light_energy = 0.75
	light.shadow_enabled = false
	_preview_root.add_child(light)
	var floor := MeshInstance3D.new()
	floor.name = "StudyFloor"
	var floor_mesh := CylinderMesh.new()
	floor_mesh.top_radius = 4.2
	floor_mesh.bottom_radius = 4.2
	floor_mesh.height = 0.07
	floor.mesh = floor_mesh
	floor.position.y = -0.08
	var floor_material := StandardMaterial3D.new()
	floor_material.albedo_color = Color("587163")
	floor_material.roughness = 1.0
	floor.material_override = floor_material
	_preview_root.add_child(floor)
	var camera := Camera3D.new()
	camera.name = "StudyCamera"
	camera.current = true
	camera.fov = 45.0
	camera.position = Vector3(5.2, 3.9, 6.1)
	_preview_root.add_child(camera)
	camera.look_at(Vector3(0, 0.9, 0))


func _clear_preview() -> void:
	_preview_active = false
	_preview_effects.clear()
	_preview_plan_ids.clear()
	_preview_dummy_velocity = Vector3.ZERO
	_preview_glide_speed = 0.0
	_preview_assembly = null
	_preview_dummy = null
	_preview_outline = null
	if _preview_root != null and is_instance_valid(_preview_root):
		preview_viewport.remove_child(_preview_root)
		_preview_root.queue_free()
	_preview_root = null


func _refresh_preview() -> void:
	_setup_preview_world()
	if _preview_dummy != null and is_instance_valid(_preview_dummy):
		_preview_root.remove_child(_preview_dummy)
		_preview_dummy.queue_free()
	if _preview_assembly != null and is_instance_valid(_preview_assembly):
		_preview_root.remove_child(_preview_assembly)
		_preview_assembly.queue_free()
	_preview_assembly = null
	_preview_dummy = null
	_preview_outline = null
	_preview_effects.clear()
	_preview_plan_ids.clear()
	_preview_dummy_velocity = Vector3.ZERO
	_preview_glide_speed = 0.0
	_preview_active = false
	var result: Dictionary = validate_draft()
	if not result.get("ok", false):
		preview_caption.text = "Draft needs work: " + str(result.get("message", "Check the controls."))
		return
	_preview_assembly = VISUALS.build(result["artifact"], false)
	_preview_root.add_child(_preview_assembly)
	var bounds: Dictionary = result["artifact"]["bounds"]
	var min_corner: Vector3 = VISUALS.vector(bounds["min"])
	var max_corner: Vector3 = VISUALS.vector(bounds["max"])
	var center := (min_corner + max_corner) * 0.5
	var dimensions := max_corner - min_corner
	var span := maxf(1.8, maxf(dimensions.x, maxf(dimensions.y, dimensions.z)))
	var floor: MeshInstance3D = _preview_root.get_node("StudyFloor")
	var disk: CylinderMesh = floor.mesh
	var floor_radius := clampf(span * 0.65, 1.8, 3.5)
	disk.top_radius = floor_radius
	disk.bottom_radius = floor_radius
	_orbit_focus = center
	_orbit_min_distance = maxf(1.1, span * 0.52)
	_orbit_max_distance = maxf(12.0, span * 4.5)
	if not _orbit_user_modified:
		_orbit_distance = span * 0.95 + 1.2
	_orbit_distance = clampf(_orbit_distance, _orbit_min_distance, _orbit_max_distance)
	_apply_orbit_camera()
	_update_selection_outline()
	preview_caption.text = "Drag to orbit; Shift-drag or Move part to position the selection; wheel to zoom."


func _on_preview_input(event: InputEvent) -> void:
	if ui == null or not ui.visible:
		return
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT:
			if event.pressed:
				if event.shift_pressed or _move_mode:
					_begin_part_move(event.position)
				else:
					_orbit_dragging = true
			else:
				_orbit_dragging = false
				_end_part_move()
		elif event.pressed and event.button_index in [MOUSE_BUTTON_WHEEL_UP, MOUSE_BUTTON_WHEEL_DOWN]:
			_orbit_user_modified = true
			_orbit_distance *= 0.88 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 1.14
			_orbit_distance = clampf(_orbit_distance, _orbit_min_distance, _orbit_max_distance)
			_apply_orbit_camera()
	elif event is InputEventMouseMotion:
		if _move_dragging:
			_move_part_at(event.position)
		elif _orbit_dragging:
			_orbit_user_modified = true
			_orbit_yaw_deg = wrapf(_orbit_yaw_deg + event.relative.x * 0.42, -180.0, 180.0)
			_orbit_pitch_deg = clampf(_orbit_pitch_deg - event.relative.y * 0.32, -12.0, 78.0)
			_apply_orbit_camera()


func _preview_part(index: int) -> Node3D:
	if _preview_assembly == null or not is_instance_valid(_preview_assembly) or index < 0 or index >= draft.get("parts", []).size():
		return null
	var parts: Dictionary = _preview_assembly.get_meta("parts", {})
	return parts.get(draft["parts"][index]["id"]) as Node3D


func _preview_plane_hit(pointer: Vector2, height: float) -> Variant:
	if _preview_root == null or _preview_container.size.x <= 0.0 or _preview_container.size.y <= 0.0:
		return null
	var camera: Camera3D = _preview_root.get_node_or_null("StudyCamera")
	if camera == null:
		return null
	var uv := pointer / _preview_container.size
	var pixel := uv * Vector2(preview_viewport.size)
	var ray_origin := camera.project_ray_origin(pixel)
	var ray_direction := camera.project_ray_normal(pixel)
	return Plane(Vector3.UP, height).intersects_ray(ray_origin, ray_direction)


func _begin_part_move(pointer: Vector2) -> void:
	var part := _preview_part(_selected_part)
	if part == null:
		return
	var hit: Variant = _preview_plane_hit(pointer, part.global_position.y)
	if hit == null:
		return
	_orbit_dragging = false
	_move_dragging = true
	_move_changed = false
	_move_part_index = _selected_part
	_move_offset = Vector2(part.position.x - hit.x, part.position.z - hit.z)
	_tested_fingerprint = ""
	_tested_artifact.clear()
	confirm_button.disabled = true
	_stop_preview_cues()
	_show_status("Moving selected part in the private study. Release to finish; run Test again.")


func _move_part_at(pointer: Vector2) -> void:
	var part := _preview_part(_move_part_index)
	if part == null:
		_end_part_move()
		return
	var hit: Variant = _preview_plane_hit(pointer, part.global_position.y)
	if hit == null:
		return
	var x := clampf(snappedf(hit.x + _move_offset.x, 0.05), -4.0, 4.0)
	var z := clampf(snappedf(hit.z + _move_offset.y, 0.05), -4.0, 4.0)
	if is_equal_approx(x, part.position.x) and is_equal_approx(z, part.position.z):
		return
	if not _move_changed:
		_remember()
		_move_changed = true
	var definition: Dictionary = draft["parts"][_move_part_index]
	var position: Array = definition["position_m"]
	position[0] = x
	position[2] = z
	part.position.x = x
	part.position.z = z
	undo_button.disabled = false
	redo_button.disabled = true


func _end_part_move() -> void:
	if not _move_dragging:
		return
	_move_dragging = false
	_move_part_index = -1
	if _move_changed:
		_render_part_form()
		var result: Dictionary = validate_draft()
		if result.get("ok", false):
			_show_status("Part moved. Run Test to review this design and its placement.")
		else:
			_show_status("%s: %s" % [result.get("path", "$"), result.get("message", "Review this draft.")], true)
	_move_changed = false


func _stop_preview_cues() -> void:
	_preview_active = false
	for effect in _preview_effects:
		if effect.get("op") == "light" and is_instance_valid(effect.get("lamp")):
			effect["lamp"].light_energy = 0.0
		elif effect.get("op") == "spin" and is_instance_valid(effect.get("part")):
			var rotor: Node3D = effect["part"]
			rotor.basis = rotor.get_meta("authored_basis", Basis.IDENTITY)
	_preview_effects.clear()
	_preview_plan_ids.clear()
	_preview_dummy_velocity = Vector3.ZERO
	_preview_glide_speed = 0.0
	if _preview_dummy != null and is_instance_valid(_preview_dummy):
		_preview_dummy.get_parent().remove_child(_preview_dummy)
		_preview_dummy.queue_free()
	_preview_dummy = null
	preview_caption.text = "Shift-drag or Move part to position the selection; drag to orbit. Run Test again."


func _apply_orbit_camera() -> void:
	if _preview_root == null or not is_instance_valid(_preview_root):
		return
	var camera: Camera3D = _preview_root.get_node_or_null("StudyCamera")
	if camera == null:
		return
	var yaw := deg_to_rad(_orbit_yaw_deg)
	var pitch := deg_to_rad(_orbit_pitch_deg)
	var offset := Vector3(sin(yaw) * cos(pitch), sin(pitch), cos(yaw) * cos(pitch)) * _orbit_distance
	camera.position = _orbit_focus + offset
	camera.look_at(_orbit_focus)


func _update_selection_outline() -> void:
	if _preview_outline != null and is_instance_valid(_preview_outline):
		_preview_outline.get_parent().remove_child(_preview_outline)
		_preview_outline.queue_free()
	_preview_outline = null
	if _preview_assembly == null or not is_instance_valid(_preview_assembly) or _selected_part < 0 or _selected_part >= draft.get("parts", []).size():
		return
	var definition: Dictionary = draft["parts"][_selected_part]
	var parts: Dictionary = _preview_assembly.get_meta("parts", {})
	if not parts.has(definition["id"]):
		return
	var part: Node3D = parts[definition["id"]]
	var dimensions: Vector3 = VISUALS.vector(definition["size_m"]) + Vector3.ONE * 0.06
	var half := dimensions * 0.5
	var corners := [
		Vector3(-half.x, -half.y, -half.z), Vector3(half.x, -half.y, -half.z),
		Vector3(-half.x, half.y, -half.z), Vector3(half.x, half.y, -half.z),
		Vector3(-half.x, -half.y, half.z), Vector3(half.x, -half.y, half.z),
		Vector3(-half.x, half.y, half.z), Vector3(half.x, half.y, half.z),
	]
	var material := StandardMaterial3D.new()
	material.resource_name = "Selected part guide — private preview only"
	material.albedo_color = COPPER
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.no_depth_test = true
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	var lines := ImmediateMesh.new()
	lines.surface_begin(Mesh.PRIMITIVE_LINES, material)
	for pair in [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]]:
		lines.surface_add_vertex(corners[pair[0]])
		lines.surface_add_vertex(corners[pair[1]])
	lines.surface_end()
	var guide := Node3D.new()
	guide.name = "SelectedPartGuide"
	guide.set_meta("preview_only", true)
	var wire := MeshInstance3D.new()
	wire.name = "WireOutline"
	wire.mesh = lines
	guide.add_child(wire)
	var dot_mesh := BoxMesh.new()
	dot_mesh.size = Vector3.ONE * 0.065
	var dots := MultiMesh.new()
	dots.transform_format = MultiMesh.TRANSFORM_3D
	dots.mesh = dot_mesh
	dots.instance_count = corners.size()
	for index in range(corners.size()):
		dots.set_instance_transform(index, Transform3D(Basis.IDENTITY, corners[index]))
	var endpoints := MultiMeshInstance3D.new()
	endpoints.name = "OutlineCorners"
	endpoints.multimesh = dots
	endpoints.material_override = material
	guide.add_child(endpoints)
	part.add_child(guide)
	_preview_outline = guide


func _apply_preview_effects() -> void:
	if _preview_assembly == null:
		return
	var parts: Dictionary = _preview_assembly.get_meta("parts", {})
	var needs_dummy := false
	var planned: Array = []
	var seen: Dictionary = {}
	for node in _tested_artifact["source"]["nodes"]:
		if node["op"] not in ["interact", "timer", "proximity"]:
			continue
		for reached in EXECUTION.plan(_tested_artifact, str(node["id"])):
			if not seen.has(reached["id"]):
				seen[reached["id"]] = true
				planned.append(reached)
	for node in _tested_artifact["source"]["nodes"]:
		if node["op"] == "glide" and _tested_artifact["source"]["mount"] == "avatar":
			_preview_glide_speed = float(node["params"]["fall_speed_mps"])
			needs_dummy = true
	for node in planned:
		_preview_plan_ids.append(node["id"])
		match node["op"]:
			"light":
				var light: OmniLight3D = parts[node["part_id"]].get_node_or_null("Light_" + str(node["id"]))
				if light != null:
					light.light_energy = float(node["params"]["intensity"]) * 0.75
					_preview_effects.append({"op": "light", "lamp": light, "remaining": float(node["params"]["duration_s"])})
			"spin":
				_preview_effects.append({"op": "spin", "part": parts[node["part_id"]], "speed_rpm": float(node["params"]["speed_rpm"]), "remaining": float(node["params"]["duration_s"]), "angle": 0.0})
			"wind":
				needs_dummy = true
				_preview_effects.append({"op": "wind", "direction": node["params"]["direction"], "acceleration_mps2": float(node["params"]["acceleration_mps2"]), "remaining": float(node["params"]["duration_s"])})
	if needs_dummy:
		_preview_dummy = Node3D.new()
		_preview_dummy.name = "ConsentDemoOnly"
		_preview_dummy.position = Vector3(2.1, 1.2, 0.0)
		var body := MeshInstance3D.new()
		var capsule := CapsuleMesh.new()
		capsule.radius = 0.18
		capsule.height = 0.75
		body.mesh = capsule
		body.position.y = 0.57
		var blue := StandardMaterial3D.new()
		blue.albedo_color = Color("87c5c8")
		blue.roughness = 0.95
		body.material_override = blue
		_preview_dummy.add_child(body)
		_preview_root.add_child(_preview_dummy)
	preview_caption.text = "Private cue: %d reachable behaviors. Drag to orbit; Shift-drag or Move part to position; wheel to zoom." % planned.size()


func tested_plan_ids() -> Array:
	return _preview_plan_ids.duplicate()
