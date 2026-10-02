extends SceneTree
## The same editable source parts survive both illustrative graphics profiles.

const TEST_SAVE := "user://tests/workshop_art_smoke.json"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var original_profile := OS.get_environment("ENFRACTAL_ART_PROFILE")
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE", TEST_SAVE)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	OS.set_environment("ENFRACTAL_ART_PROFILE", "standard")
	var scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	if not _check(scene.workshop_runtime != null and scene.workshop_runtime.art_dressing != null, "standard dressing missing"):
		return
	var workshop = scene.workshop_runtime
	var standard_stats: Dictionary = workshop.art_dressing.stats.duplicate(true)
	if not _check(workshop.art_dressing.profile == "standard" and int(standard_stats["triangle_upper_bound"]) <= 14000, "standard dressing exceeds bounded geometry: " + str(standard_stats)):
		return
	scene._jump_to_view("pin")
	scene._set_walking_mode(true)
	workshop._place_in_front()
	workshop._place_path_for_selected()
	workshop._move_path_endpoint()
	if not _check(workshop.state.revision == 3 and workshop.platform_nodes.size() == 1 and workshop.path_nodes.size() == 1 and workshop.join_nodes.size() == 1, "source edit or join failed"):
		return
	var saved_join_id: String = workshop.join_relations.keys()[0]
	var saved_path_id: String = workshop.path_nodes.keys()[0]
	var saved_platform_id: String = workshop.platform_nodes.keys()[0]
	if not _check(_has_art_parts(workshop.path_nodes[saved_path_id]) and _has_art_parts(workshop.join_nodes[saved_join_id]) and _has_art_parts(workshop.platform_nodes[saved_platform_id]), "standard source path, generated join or platform lacks shared art contact parts"):
		return
	var source_snapshot: Dictionary = workshop.state.get_snapshot()
	scene._set_walking_mode(false)
	root.remove_child(scene)
	scene.queue_free()
	await process_frame
	OS.set_environment("ENFRACTAL_ART_PROFILE", "low")
	var low_scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(low_scene)
	await process_frame
	var low_workshop = low_scene.workshop_runtime
	if not _check(low_workshop != null and low_workshop.art_dressing != null and low_workshop.art_dressing.profile == "low", "low dressing missing"):
		return
	var low_stats: Dictionary = low_workshop.art_dressing.stats
	if not _check(int(low_stats["tree_centers"]) == int(standard_stats["tree_centers"]) and int(low_stats["canopy_lobes"]) >= 20 and low_workshop.art_dressing.get_node_or_null("AuthoredBranches") != null and int(low_stats["triangle_upper_bound"]) < int(standard_stats["triangle_upper_bound"]) and int(low_stats["triangle_upper_bound"]) <= 6000, "low profile lost a branch silhouette or failed geometry budget: " + str(low_stats)):
		return
	if not _check(_same_semantics(low_workshop.state.get_snapshot(), source_snapshot), "low profile changed the saved source edit"):
		return
	if not _check(low_workshop.path_nodes.has(saved_path_id) and low_workshop.join_nodes.has(saved_join_id) and low_workshop.platform_nodes.has(saved_platform_id), "low profile lost the path, join or platform visual"):
		return
	if not _check(_has_art_parts(low_workshop.path_nodes[saved_path_id]) and _has_art_parts(low_workshop.join_nodes[saved_join_id]) and _has_art_parts(low_workshop.platform_nodes[saved_platform_id]), "low profile lost coherent source/join/platform art pieces"):
		return
	await physics_frame
	await physics_frame
	var relation: Dictionary = low_workshop.join_relations[saved_join_id]
	var start: Dictionary = relation["params"]["start"]
	var end: Dictionary = relation["params"]["end"]
	var midpoint := Vector3((float(start["x_m"]) + float(end["x_m"])) * 0.5, (float(start["y_m"]) + float(end["y_m"])) * 0.5, (float(start["z_m"]) + float(end["z_m"])) * 0.5)
	var ray := PhysicsRayQueryParameters3D.create(midpoint + Vector3.UP * 2.0, midpoint - Vector3.UP * 2.0)
	ray.exclude = [low_scene.player_body.get_rid()]
	var hit: Dictionary = low_scene.get_world_3d().direct_space_state.intersect_ray(ray)
	if not _check(hit.get("collider") == low_workshop.join_nodes[saved_join_id], "low profile lost join collision; hit=" + str(hit.get("collider")) + "; expected=" + str(low_workshop.join_nodes[saved_join_id])):
		return
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	OS.set_environment("ENFRACTAL_ART_PROFILE", original_profile)
	print("Workshop art smoke passed: same saved edit and walkable join; standard triangles=", standard_stats["triangle_upper_bound"], "; low triangles=", low_stats["triangle_upper_bound"])
	quit(0)


func _has_art_parts(path_body: StaticBody3D) -> bool:
	return path_body.get_node_or_null("IllustrativeSoilShoulder") != null and path_body.get_node_or_null("StoneCourseJoints") != null


func _check(condition: bool, description: String) -> bool:
	if condition:
		return true
	push_error("Workshop art smoke: " + description)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	quit(1)
	return false


func _same_semantics(left: Variant, right: Variant) -> bool:
	if left is Dictionary and right is Dictionary:
		if left.size() != right.size():
			return false
		for key in left:
			if not right.has(key) or not _same_semantics(left[key], right[key]):
				return false
		return true
	if left is Array and right is Array:
		if left.size() != right.size():
			return false
		for index in range(left.size()):
			if not _same_semantics(left[index], right[index]):
				return false
		return true
	if (typeof(left) in [TYPE_FLOAT, TYPE_INT]) and (typeof(right) in [TYPE_FLOAT, TYPE_INT]):
		return is_finite(float(left)) and is_finite(float(right)) and absf(float(left) - float(right)) <= 0.000001
	return left == right
