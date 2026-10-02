extends SceneTree
## UI contract fixture. Durable travel and real scene transfers have separate tests.

const PANEL = preload("res://scripts/travel_panel.gd")
var checks := 0
var failures := 0

class TravelFixture extends Node:
	var panel_open := false
	var calls: Array = []
	var fail_visit := false
	var fail_return := false
	var status: Dictionary = {
		"ok": true,
		"current_world": {"id": "home", "name": "Home Earth", "gravity_scale": 1.0, "owner_id": "operator"},
		"worlds": [{"id": "home", "name": "Home Earth", "gravity_scale": 1.0, "owner_id": "operator", "status": "awake", "can_visit": true, "occupants": ["local_player"]}],
		"presence": {"world_id": "home", "epoch": 1}, "pending": {},
		"message": "Saved locally. Choose a destination."
	}

	func set_panel_open(value: bool) -> void:
		panel_open = value

	func travel_status() -> Dictionary:
		return status.duplicate(true)

	func create_sandbox() -> Dictionary:
		calls.append("create")
		status["worlds"].append({"id": "sandbox-local", "name": "Your free sandbox", "gravity_scale": 0.25, "owner_id": "local_player", "status": "sleeping", "can_visit": true, "occupants": 0})
		return {"ok": true, "message": "Sandbox saved. Its changes are separate from Home."}

	func visit_world(identifier: String) -> Dictionary:
		calls.append("visit:" + identifier)
		await get_tree().process_frame
		if fail_visit:
			return {"ok": false, "message": "Destination is full. Your Home presence is unchanged. Retry when a place opens."}
		for world in status["worlds"]:
			if world["id"] == identifier:
				status["current_world"] = world.duplicate(true)
		status["presence"] = {"world_id": identifier, "epoch": int(status["presence"]["epoch"]) + 1}
		return {"ok": true, "message": "Arrival saved. Welcome to your sandbox."}

	func return_home() -> Dictionary:
		calls.append("return")
		await get_tree().process_frame
		if fail_return:
			status["pending"] = {"destination": "home", "status": "awaiting_save"}
			return {"ok": false, "message": "Home save is temporarily unavailable. Your journey is retained; retry Return Home."}
		status["current_world"] = status["worlds"][0].duplicate(true)
		status["presence"] = {"world_id": "home", "epoch": int(status["presence"]["epoch"]) + 1}
		status["pending"] = {}
		return {"ok": true, "message": "Returned Home and saved."}

	func invite_guest() -> Dictionary:
		calls.append("invite")
		return {"ok": true, "message": "Local test guest invitation saved."}

	func revoke_guest() -> Dictionary:
		calls.append("revoke")
		return {"ok": true, "message": "Local test guest invitation revoked and saved."}


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	# Exercise actual layout sizes rather than shrinking the 1280×720 canvas.
	root.content_scale_size = Vector2i.ZERO
	var fixture := TravelFixture.new()
	root.add_child(fixture)
	var panel = PANEL.new()
	panel.runtime = fixture
	root.add_child(panel)
	await process_frame
	_check(not panel.ui.visible and not panel.is_processing(), "closed panel performs no polling")
	_check(panel.open_panel() and fixture.panel_open, "opening travel gates gameplay through runtime")
	await process_frame
	_check(panel.current_label.text == "You are in Home Earth", "current world is stated plainly")
	_check(not panel.create_button.disabled and panel.visit_button.disabled, "Home-only state offers sandbox creation without self-travel")
	_check(panel.ui.find_child("LocalGuestDisclosure", true, false).text.contains("local test identity") and panel.ui.find_child("LocalGuestDisclosure", true, false).text.contains("Remote accounts"), "guest fixture is distinguished from remote accounts")
	for dimensions in [Vector2i(1280, 720), Vector2i(900, 600)]:
		root.size = dimensions
		await process_frame
		await process_frame
		_check(root.get_visible_rect().encloses(panel.return_button.get_global_rect()) and root.get_visible_rect().encloses(panel.visit_button.get_global_rect()), "pinned travel actions fit %s" % str(dimensions))
		_check(root.get_visible_rect().encloses(panel.create_button.get_global_rect()) and root.get_visible_rect().encloses(panel.status_label.get_global_rect()), "creation and save feedback fit %s" % str(dimensions))
	panel.create_button.pressed.emit()
	panel.create_button.pressed.emit()
	_check(panel._busy and panel.create_button.disabled and panel.return_button.disabled, "busy gates repeated actions immediately")
	await process_frame
	await process_frame
	_check(fixture.calls.count("create") == 1 and fixture.status["worlds"].size() == 2, "double click creates one sandbox request")
	_check(panel.create_button.disabled and panel.create_button.text == "Sandbox created", "one free sandbox is clear")
	panel.destination_list.select(1)
	panel.destination_list.item_selected.emit(1)
	_check(panel.selected_world_id == "sandbox-local" and panel.rule_label.text.contains("Quarter gravity"), "destination selection shows quarter gravity")
	_check(panel.ownership_label.text.contains("own builds") and panel.ui.find_child("InventorySeparation", true, false).text.contains("Home possessions stay at Home"), "world changes and inventory separation are explicit")
	_check(not panel.visit_button.disabled and not panel.invite_button.disabled, "sandbox offers review and local invitation")
	panel.visit_button.pressed.emit()
	await process_frame
	_check(panel.visit_dialog.visible and panel.visit_dialog.dialog_text.contains("saved before departure") and panel.visit_dialog.dialog_text.contains("powers do not become Home powers"), "visit requires visible saving and adaptation review")
	_check(fixture.calls.size() == 1, "opening review performs no transfer")
	fixture.status["worlds"][1]["gravity_scale"] = 0.5
	panel.visit_dialog.confirmed.emit()
	panel.visit_dialog.hide()
	await process_frame
	_check(not panel._busy and fixture.calls.size() == 1 and panel.status_label.text.contains("destination changed"), "changed rules require fresh confirmation")
	fixture.status["worlds"][1]["gravity_scale"] = 0.25
	panel.visit_button.pressed.emit()
	fixture.fail_visit = true
	panel.visit_dialog.confirmed.emit()
	panel.visit_dialog.confirmed.emit()
	panel.visit_dialog.hide()
	_check(panel._busy and panel.status_label.text.contains("Saving"), "accepted confirmation exposes saving state")
	await process_frame
	await process_frame
	await process_frame
	_check(fixture.calls.count("visit:sandbox-local") == 1, "double confirmation cannot duplicate travel")
	_check(panel.selected_world_id == "sandbox-local" and panel.status_label.text.contains("Destination is full") and not panel.return_button.disabled, "failed visit preserves intent and offers Return Home")
	panel._process(0.5)
	_check(panel.status_label.text.contains("Destination is full"), "polling does not erase actionable failure")
	fixture.fail_visit = false
	panel.visit_button.pressed.emit()
	panel.visit_dialog.confirmed.emit()
	panel.visit_dialog.hide()
	await process_frame
	await process_frame
	await process_frame
	_check(panel.current_label.text == "You are in Your free sandbox" and panel.visit_button.disabled, "arrival updates current-world identity and prevents self-travel")
	panel.invite_button.pressed.emit()
	await process_frame
	await process_frame
	_check(fixture.calls.count("invite") == 1 and panel.status_label.text.contains("invitation saved"), "invitation delegates to runtime with durable result")
	panel.revoke_button.pressed.emit()
	await process_frame
	await process_frame
	_check(fixture.calls.count("revoke") == 1 and panel.status_label.text.contains("revoked"), "revocation delegates independently")
	fixture.fail_return = true
	panel.return_button.pressed.emit()
	await process_frame
	await process_frame
	await process_frame
	_check(not panel.return_button.disabled and panel.visit_button.disabled and panel.create_button.disabled, "interrupted transfer blocks new journeys but allows recovery")
	_check(panel.status_label.text.contains("retry Return Home") and panel.save_label.text.contains("needs recovery"), "return failure states retained journey and recovery action")
	fixture.status["ok"] = false
	panel._refresh()
	_check(not panel.return_button.disabled, "Return Home remains available even when regular status is unavailable")
	fixture.status["ok"] = true
	fixture.fail_return = false
	panel.return_button.pressed.emit()
	await process_frame
	await process_frame
	await process_frame
	_check(fixture.status["pending"].is_empty() and panel.current_label.text == "You are in Home Earth", "Return Home retries pending journey successfully")
	panel.destination_list.select(1)
	panel.destination_list.item_selected.emit(1)
	panel.visit_button.pressed.emit()
	var escape := InputEventKey.new()
	escape.keycode = KEY_ESCAPE
	escape.pressed = true
	panel._unhandled_input(escape)
	_check(not panel.visit_dialog.visible and panel.ui.visible, "Escape cancels visit review before closing menu")
	panel._unhandled_input(escape)
	_check(not panel.ui.visible and not fixture.panel_open and not panel.is_processing(), "Escape closes menu and restores runtime input")
	panel.open_panel()
	_check(panel.selected_world_id == "sandbox-local", "reopening retains selected destination")
	await process_frame
	await process_frame
	if DisplayServer.get_name() != "headless":
		root.get_texture().get_image().save_png("res://../.cache/travel-panel-900.png")
		root.size = Vector2i(1280, 720)
		await process_frame
		await process_frame
		root.get_texture().get_image().save_png("res://../.cache/travel-panel-1280.png")
	panel.close_panel()
	panel.queue_free()
	fixture.queue_free()
	await process_frame
	print("TRAVEL_PANEL_SMOKE %d checks, %d failures" % [checks, failures])
	quit(0 if failures == 0 else 1)


func _check(passed: bool, message: String) -> void:
	checks += 1
	if not passed:
		failures += 1
		push_error(message)
