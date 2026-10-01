extends SceneTree

const TEST_SAVE := "user://tests/workshop_integration.json"

var failures := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", TEST_SAVE)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	var workshop = scene.workshop_runtime
	_expect(workshop != null and workshop.edit_enabled, "local workshop initialized")
	_expect(scene.map_runtime.is_loaded(), "validated map runtime connected")
	if workshop == null or not workshop.edit_enabled:
		quit(1)
		return

	scene._jump_to_view("pin")
	scene._set_walking_mode(true)
	await physics_frame
	_expect(scene.walking and scene.terrain_colliders.active_patches.size() > 0, "walking uses source-grid colliders")
	var player: CharacterBody3D = scene.player_body
	_expect(player.global_position.y > scene.map_runtime.height_at(player.global_position.x, player.global_position.z), "player spawned above terrain")

	workshop._place_in_front()
	_expect(workshop.state.revision == 1 and workshop.platform_nodes.size() == 1, "platform placed in live scene")
	var entity_id: String = workshop.selected_entity_id
	_expect(entity_id.begins_with("edit:") and FileAccess.file_exists(TEST_SAVE), "platform has stable ID and saved delta")
	var first: Dictionary = workshop.state.get_entity(entity_id)
	_expect(first.get("material_role") == "rock" and first.get("provenance", {}).get("classification") == "player_change", "platform uses approved grounded-painterly role")
	workshop._revise_selected()
	_expect(workshop.state.revision == 2 and workshop.state.get_entity(entity_id)["params"]["width_m"] == 4.0, "platform remains editable")
	workshop._reload()
	_expect(workshop.state.revision == 2 and workshop.platform_nodes.has(entity_id), "saved platform reloads with same ID")
	workshop._remove_selected()
	_expect(workshop.state.revision == 3 and workshop.platform_nodes.is_empty(), "platform removal updates scene and save")
	scene._set_walking_mode(false)
	_expect(not scene.walking and scene.terrain_colliders.active_patches.is_empty(), "fly mode releases collision ring")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	print("Workshop integration: ", "PASS" if failures == 0 else "%d failure(s)" % failures)
	quit(0 if failures == 0 else 1)


func _expect(condition: bool, description: String) -> void:
	if condition:
		return
	failures += 1
	push_error("Workshop integration failed: " + description)
