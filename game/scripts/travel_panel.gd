extends CanvasLayer
## Local travel presentation only. Runtime owns identity, persistence and transfer.

const DEEP := Color("0d2024")
const PINE := Color("172e2a")
const PANEL := Color("203c38")
const STONE := Color("e9e6d8")
const SAGE := Color("acc8b3")
const MUTED := Color("9cb4ae")
const COPPER := Color("d9a879")
const ERROR := Color("f4b6a7")

var runtime
var ui: Control
var destination_list: ItemList
var current_label: Label
var destination_title: Label
var rule_label: Label
var ownership_label: Label
var occupant_label: Label
var status_label: Label
var save_label: Label
var create_button: Button
var visit_button: Button
var return_button: Button
var invite_button: Button
var revoke_button: Button
var close_button: Button
var visit_dialog: ConfirmationDialog
var selected_world_id := ""
var _snapshot: Dictionary = {}
var _worlds: Array = []
var _busy := false
var _reviewed_destination := ""
var _reviewed_rules := ""
var _poll_elapsed := 0.0
var _result_message := ""
var _result_error := false


func _ready() -> void:
	layer = 45
	_build_ui()
	ui.visible = false
	set_process(false)


func open_panel() -> bool:
	if runtime == null:
		return false
	ui.visible = true
	runtime.set_panel_open(true)
	_refresh()
	set_process(true)
	close_button.grab_focus()
	return true


func close_panel() -> void:
	if ui == null or not ui.visible or _busy:
		return
	visit_dialog.hide()
	ui.visible = false
	set_process(false)
	if runtime != null:
		runtime.set_panel_open(false)


func _unhandled_input(event: InputEvent) -> void:
	if ui == null or not ui.visible or not event is InputEventKey or not event.pressed or event.echo:
		return
	if event.keycode == KEY_ESCAPE:
		if visit_dialog.visible:
			visit_dialog.hide()
		else:
			close_panel()
		get_viewport().set_input_as_handled()


func _process(delta: float) -> void:
	_poll_elapsed += delta
	if _poll_elapsed >= 0.4 and not _busy:
		_poll_elapsed = 0.0
		_refresh()


func _refresh() -> void:
	if runtime == null:
		return
	_snapshot = runtime.travel_status().duplicate(true)
	_worlds = _snapshot.get("worlds", []).duplicate(true)
	var current: Dictionary = _snapshot.get("current_world", {})
	current_label.text = "You are in %s" % str(current.get("name", "an unavailable world"))
	var presence: Dictionary = _snapshot.get("presence", {})
	var pending: Dictionary = _snapshot.get("pending", {})
	if not pending.is_empty():
		save_label.text = "A journey needs recovery. Return Home can safely finish it."
		save_label.add_theme_color_override("font_color", COPPER)
	elif not _snapshot.get("ok", false):
		save_label.text = "Saved travel state is unavailable. No new journey will start."
		save_label.add_theme_color_override("font_color", ERROR)
	elif str(presence.get("world_id", "")) != str(current.get("id", "")):
		save_label.text = "Confirming your current world. Return Home remains available."
		save_label.add_theme_color_override("font_color", COPPER)
	else:
		save_label.text = "Your place and world changes are saved before departure."
		save_label.add_theme_color_override("font_color", MUTED)
	destination_list.clear()
	var selected_index := -1
	for index in range(_worlds.size()):
		var world: Dictionary = _worlds[index]
		var identifier := str(world.get("id", ""))
		var title := str(world.get("name", "Unnamed world"))
		if identifier == str(current.get("id", "")):
			title += "  ·  Here"
		elif not world.get("can_visit", false):
			title += "  ·  Unavailable"
		destination_list.add_item(title)
		destination_list.set_item_metadata(index, identifier)
		if identifier == selected_world_id:
			selected_index = index
	if selected_index < 0 and not _worlds.is_empty():
		# First open prefers the first other world; failed visits retain their ID.
		selected_index = 0
		for index in range(_worlds.size()):
			if str(_worlds[index].get("id", "")) != str(current.get("id", "")):
				selected_index = index
				break
		selected_world_id = str(_worlds[selected_index].get("id", ""))
	if selected_index >= 0:
		destination_list.select(selected_index)
	_render_destination()
	_set_controls()
	if not _busy:
		var message := _result_message
		var failed := _result_error
		if message.is_empty():
			message = str(_snapshot.get("message", "Choose a destination to review its rules."))
			failed = not _snapshot.get("ok", false)
		status_label.text = message
		status_label.add_theme_color_override("font_color", ERROR if failed else SAGE)


func _selected_world() -> Dictionary:
	for world in _worlds:
		if str(world.get("id", "")) == selected_world_id:
			return world
	return {}


func _render_destination() -> void:
	var world := _selected_world()
	if world.is_empty():
		destination_title.text = "A place of your own"
		rule_label.text = "Create your free sandbox to explore a different set of rules."
		ownership_label.text = "Your Home Earth creations stay at Home."
		occupant_label.text = "No destination selected."
		return
	destination_title.text = str(world.get("name", "Unnamed world"))
	var gravity := float(world.get("gravity_scale", 1.0))
	var gravity_text := "Earth gravity" if is_equal_approx(gravity, 1.0) else "Quarter gravity" if is_equal_approx(gravity, 0.25) else "%s× Earth gravity" % str(gravity)
	rule_label.text = "%s\n%s" % [gravity_text, "Falling is gentler here. Movement and inventions use this world's rules." if gravity < 1.0 else "Movement and inventions follow the shared Home rules."]
	var is_home := _is_home(world)
	ownership_label.text = "Shared Home Earth\nYour Home builds remain saved here while you visit." if is_home else "Your free sandbox\nA fresh geographic base with its own builds, rules and saved changes."
	var occupants = world.get("occupants", 0)
	var count: int = occupants.size() if occupants is Array else int(occupants)
	var state := str(world.get("status", "available")).replace("_", " ")
	occupant_label.text = "%s  ·  %d present" % [state.capitalize(), count]
	if not world.get("can_visit", false):
		occupant_label.text += "\nThis destination cannot admit you right now."


func _is_home(world: Dictionary) -> bool:
	return str(world.get("id", "")) == "home"


func _has_sandbox() -> bool:
	for world in _worlds:
		if not _is_home(world) and str(world.get("owner_id", "")) == "local_player":
			return true
	return false


func _set_controls() -> void:
	var unavailable: bool = _busy or not _snapshot.get("ok", false) or not _snapshot.get("pending", {}).is_empty()
	var selected := _selected_world()
	var current: Dictionary = _snapshot.get("current_world", {})
	create_button.disabled = unavailable or _has_sandbox()
	create_button.text = "Sandbox created" if _has_sandbox() else "Create free sandbox"
	visit_button.disabled = unavailable or selected.is_empty() or not selected.get("can_visit", false) or selected_world_id == str(current.get("id", ""))
	visit_button.text = "Review visit"
	# Recovery never depends on a working portal, selected world, or invitation.
	return_button.disabled = _busy
	invite_button.disabled = unavailable or not _has_sandbox()
	revoke_button.disabled = unavailable or not _has_sandbox()
	destination_list.mouse_filter = Control.MOUSE_FILTER_IGNORE if _busy else Control.MOUSE_FILTER_STOP
	close_button.disabled = _busy
	visit_dialog.get_ok_button().disabled = _busy


func _select_destination(index: int) -> void:
	if _busy or index < 0 or index >= _worlds.size():
		return
	selected_world_id = str(_worlds[index].get("id", ""))
	_result_message = ""
	_result_error = false
	_render_destination()
	_set_controls()


func _rules_fingerprint(world: Dictionary) -> String:
	# Presentation guard only; runtime must independently bind authoritative rules.
	return JSON.stringify([world.get("id", ""), world.get("name", ""), world.get("gravity_scale", 1.0), world.get("owner_id", ""), world.get("status", ""), world.get("can_visit", false)])


func _review_visit() -> void:
	if _busy:
		return
	_refresh()
	if visit_button.disabled:
		return
	var world := _selected_world()
	_reviewed_destination = selected_world_id
	_reviewed_rules = _rules_fingerprint(world)
	visit_dialog.title = "Visit %s?" % str(world.get("name", "this world"))
	visit_dialog.dialog_text = "%s\n\nYour current place is saved before departure.\n\nEach world keeps its own creations and equipment. Home possessions are not copied into the sandbox; sandbox powers do not become Home powers.\n\nReturn Home is always available in this travel menu." % rule_label.text
	visit_dialog.popup_centered(Vector2i(540, 340))


func _confirm_visit() -> void:
	if _busy:
		return
	_refresh()
	var world := _selected_world()
	if selected_world_id != _reviewed_destination or _rules_fingerprint(world) != _reviewed_rules or visit_button.disabled:
		_result_message = "The destination changed. Review its current rules before visiting."
		_result_error = true
		_refresh()
		return
	_begin_action("visit_world", [selected_world_id])


func _begin_action(method: String, arguments: Array = []) -> void:
	if _busy:
		return
	_busy = true
	_result_message = ""
	_result_error = false
	status_label.text = "Saving travel state…"
	status_label.add_theme_color_override("font_color", COPPER)
	_set_controls()
	_perform_action.call_deferred(method, arguments)


func _perform_action(method: String, arguments: Array) -> void:
	# Let the saving state draw before a bounded synchronous local-host call.
	await get_tree().process_frame
	var result: Dictionary = await runtime.callv(method, arguments)
	_busy = false
	_result_error = not result.get("ok", false)
	_result_message = str(result.get("message", "Journey complete." if not _result_error else "The journey could not finish. You can retry or Return Home."))
	_refresh()


func _build_ui() -> void:
	ui = Control.new()
	ui.name = "TravelPanel"
	ui.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(ui)
	var background := ColorRect.new()
	background.color = Color(0.025, 0.06, 0.065, 0.94)
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	ui.add_child(background)
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "top", "right", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 22)
	ui.add_child(margin)
	var shell := VBoxContainer.new()
	shell.add_theme_constant_override("separation", 12)
	margin.add_child(shell)
	var header := HBoxContainer.new()
	shell.add_child(header)
	var heading := VBoxContainer.new()
	heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(heading)
	heading.add_child(_label("Worlds & journeys", 27, STONE))
	heading.add_child(_label("One Home. Room to experiment.", 14, MUTED))
	close_button = _button("Close", false)
	close_button.name = "CloseTravelButton"
	close_button.pressed.connect(close_panel)
	header.add_child(close_button)
	current_label = _label("", 17, SAGE)
	current_label.name = "CurrentWorld"
	shell.add_child(current_label)
	var main := HBoxContainer.new()
	main.add_theme_constant_override("separation", 18)
	main.size_flags_vertical = Control.SIZE_EXPAND_FILL
	shell.add_child(main)
	var left := VBoxContainer.new()
	left.custom_minimum_size.x = 245
	left.add_theme_constant_override("separation", 10)
	main.add_child(left)
	left.add_child(_label("Destinations", 16, STONE))
	destination_list = ItemList.new()
	destination_list.name = "DestinationList"
	destination_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	destination_list.add_theme_color_override("font_color", STONE)
	destination_list.add_theme_color_override("font_selected_color", DEEP)
	destination_list.add_theme_constant_override("v_separation", 14)
	destination_list.add_theme_stylebox_override("panel", _style(PINE))
	destination_list.add_theme_stylebox_override("selected", _style(SAGE))
	destination_list.add_theme_stylebox_override("selected_focus", _style(SAGE, COPPER, 2))
	destination_list.item_selected.connect(_select_destination)
	left.add_child(destination_list)
	create_button = _button("Create free sandbox", false)
	create_button.name = "CreateSandboxButton"
	create_button.pressed.connect(func(): _begin_action("create_sandbox"))
	left.add_child(create_button)
	var card := PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.add_theme_stylebox_override("panel", _style(PINE, Color("34564e"), 1))
	main.add_child(card)
	var scroll := ScrollContainer.new()
	scroll.name = "DestinationDetailScroll"
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	card.add_child(scroll)
	var detail := VBoxContainer.new()
	detail.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	detail.add_theme_constant_override("separation", 15)
	scroll.add_child(detail)
	destination_title = _label("", 24, STONE)
	destination_title.name = "DestinationTitle"
	detail.add_child(destination_title)
	ownership_label = _label("", 14, MUTED)
	detail.add_child(ownership_label)
	rule_label = _label("", 16, SAGE)
	rule_label.name = "DestinationRules"
	detail.add_child(rule_label)
	var inventory := _label("Separate builds. Separate equipment.\nHome possessions stay at Home. Each destination validates its own inventions.", 14, STONE)
	inventory.name = "InventorySeparation"
	detail.add_child(inventory)
	occupant_label = _label("", 13, MUTED)
	detail.add_child(occupant_label)
	detail.add_child(HSeparator.new())
	detail.add_child(_label("Sandbox invitations", 17, STONE))
	var guest_note := _label("Invite a local test identity into your free sandbox. This guest stays on this computer.\nRemote accounts and online invitations are not connected yet.", 13, MUTED)
	guest_note.name = "LocalGuestDisclosure"
	detail.add_child(guest_note)
	var guests := HBoxContainer.new()
	guests.add_theme_constant_override("separation", 10)
	detail.add_child(guests)
	invite_button = _button("Invite test guest", false)
	invite_button.name = "InviteTestGuestButton"
	invite_button.pressed.connect(func(): _begin_action("invite_guest"))
	guests.add_child(invite_button)
	revoke_button = _button("Revoke invitation", false)
	revoke_button.name = "RevokeTestGuestButton"
	revoke_button.pressed.connect(func(): _begin_action("revoke_guest"))
	guests.add_child(revoke_button)
	save_label = _label("", 13, MUTED)
	save_label.name = "DurableTravelStatus"
	shell.add_child(save_label)
	var footer := HBoxContainer.new()
	footer.custom_minimum_size.y = 56
	footer.add_theme_constant_override("separation", 12)
	shell.add_child(footer)
	status_label = _label("", 13, SAGE)
	status_label.name = "TravelActionStatus"
	status_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	footer.add_child(status_label)
	return_button = _button("Return Home", false)
	return_button.name = "ReturnHomeButton"
	return_button.pressed.connect(func(): _begin_action("return_home"))
	footer.add_child(return_button)
	visit_button = _button("Review visit", true)
	visit_button.name = "ReviewVisitButton"
	visit_button.pressed.connect(_review_visit)
	footer.add_child(visit_button)
	visit_dialog = ConfirmationDialog.new()
	visit_dialog.name = "VisitConfirmation"
	visit_dialog.ok_button_text = "Visit world"
	visit_dialog.cancel_button_text = "Keep exploring here"
	visit_dialog.dialog_autowrap = true
	visit_dialog.confirmed.connect(_confirm_visit)
	ui.add_child(visit_dialog)


func _label(text: String, font_size: int, color: Color) -> Label:
	var result := Label.new()
	result.text = text
	result.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	result.add_theme_color_override("font_color", color)
	result.add_theme_font_size_override("font_size", font_size)
	return result


func _style(color: Color, border := Color.TRANSPARENT, width := 0) -> StyleBoxFlat:
	var result := StyleBoxFlat.new()
	result.bg_color = color
	result.border_color = border
	result.set_border_width_all(width)
	result.set_corner_radius_all(7)
	result.content_margin_left = 14
	result.content_margin_right = 14
	result.content_margin_top = 12
	result.content_margin_bottom = 12
	return result


func _button(text: String, emphasis: bool) -> Button:
	var result := Button.new()
	result.text = text
	result.custom_minimum_size.y = 40
	result.add_theme_font_size_override("font_size", 14)
	result.add_theme_color_override("font_color", DEEP if emphasis else STONE)
	result.add_theme_color_override("font_hover_color", DEEP if emphasis else STONE)
	result.add_theme_color_override("font_pressed_color", DEEP if emphasis else STONE)
	result.add_theme_color_override("font_disabled_color", MUTED)
	result.add_theme_stylebox_override("normal", _style(COPPER if emphasis else PANEL, Color("638277"), 1))
	result.add_theme_stylebox_override("hover", _style(Color("e8bc8e") if emphasis else Color("3a6058"), COPPER, 1))
	result.add_theme_stylebox_override("pressed", _style(Color("ba855c") if emphasis else Color("1a3431"), COPPER, 1))
	result.add_theme_stylebox_override("disabled", _style(PINE, Color("34564e"), 1))
	result.add_theme_stylebox_override("focus", _style(Color.TRANSPARENT, COPPER, 2))
	return result
