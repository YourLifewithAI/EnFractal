extends SceneTree
## The same editable source parts survive both illustrative graphics profiles.

const TEST_SAVE := "user://tests/workshop_art_smoke.json"


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var original_profile := OS.get_environment("ENFRACTAL_ART_PROFILE")
	var original_start_walk := OS.get_environment("ENFRACTAL_START_WALK")
	OS.set_environment("ENFRACTAL_START_WALK", "")
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
	if not _check(workshop.art_dressing.profile == "standard" and int(standard_stats["tree_centers"]) >= 8 and int(standard_stats["tree_centers"]) <= 12 and int(standard_stats["triangle_upper_bound"]) < 250000, "standard asset composition is outside its provisional scope: " + str(standard_stats)):
		return
	if not _check(standard_stats["source_terrain_modified"] == false and standard_stats["seasonal_rill_is_mapped_creek"] == false, "composition misstates source geography"):
		return
	if not _check(workshop.art_dressing.get_node_or_null("IllustrativeTree_canopy_left") != null and workshop.art_dressing.get_node_or_null("IllustrativeTree_juniper_left") != null and workshop.art_dressing.get_node_or_null("PaintedGroundcoverClusters") != null and workshop.art_dressing.get_node_or_null("IllustrativeSeasonalRainGarden") != null, "composition lacks authored tree, understory or rain garden"):
		return
	if not _check(_water_has_upward_front(workshop.art_dressing), "seasonal rill water has a culled or downward-facing top surface"):
		return
	if not _check(scene.terrain_material.shader.resource_path == "res://shaders/painterly_ground.gdshader" and scene.sunlight.shadow_enabled and scene.get_viewport().msaa_3d == Viewport.MSAA_4X, "painted terrain, contact lighting or standard MSAA missing"):
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
	if not _check(_has_art_parts(workshop.path_nodes[saved_path_id]) and _has_art_parts(workshop.join_nodes[saved_join_id]) and _has_art_parts(workshop.platform_nodes[saved_platform_id]), "standard source path, generated join or platform lacks painted surface or terrain contact"):
		return
	if not _check(float(scene.terrain_material.get_shader_parameter("edit_path_width")) > 0.0 and float(scene.terrain_material.get_shader_parameter("edit_platform").z) > 0.0, "edit/revision did not refresh geographic terrain masks"):
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
	if not _check(int(low_stats["tree_centers"]) == int(standard_stats["tree_centers"]) and int(low_stats["limestone_stones"]) == int(standard_stats["limestone_stones"]) and int(low_stats["triangle_upper_bound"]) < int(standard_stats["triangle_upper_bound"]) and int(low_stats["groundcover_cards"]) < int(standard_stats["groundcover_cards"]), "low profile changed major composition or failed to reduce optional plants: " + str(low_stats)):
		return
	if not _check(_water_has_upward_front(low_workshop.art_dressing) and int(low_stats["bank_pebbles"]) == int(standard_stats["bank_pebbles"]) and int(low_stats["bank_grass_cards"]) < int(standard_stats["bank_grass_cards"]), "low profile lost a visible seasonal rill or bank detail"):
		return
	if not _check(low_scene.sunlight.shadow_enabled and low_scene.sunlight.directional_shadow_max_distance < 52.0 and low_scene.get_viewport().msaa_3d == Viewport.MSAA_2X, "low profile removed all shadows or kept standard antialiasing"):
		return
	if not _check(_same_semantics(low_workshop.state.get_snapshot(), source_snapshot), "low profile changed the saved source edit"):
		return
	if not _check(low_workshop.path_nodes.has(saved_path_id) and low_workshop.join_nodes.has(saved_join_id) and low_workshop.platform_nodes.has(saved_platform_id), "low profile lost the path, join or platform visual"):
		return
	if not _check(_has_art_parts(low_workshop.path_nodes[saved_path_id]) and _has_art_parts(low_workshop.join_nodes[saved_join_id]) and _has_art_parts(low_workshop.platform_nodes[saved_platform_id]), "low profile lost painted source/join/platform surfaces"):
		return
	if not _check(float(low_scene.terrain_material.get_shader_parameter("edit_path_width")) > 0.0, "low reload lost terrain edit mask"):
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
	root.remove_child(low_scene)
	low_scene.queue_free()
	await process_frame
	OS.set_environment("ENFRACTAL_START_WALK", "1")
	var launch_scene: Node3D = load("res://scenes/main.tscn").instantiate()
	root.add_child(launch_scene)
	await process_frame
	await physics_frame
	if not _check(launch_scene.walking and absf(launch_scene.player_body.global_position.x + 5.0) < 0.1 and absf(launch_scene.player_body.global_position.z - 337.0) < 0.1, "launch did not enter the real walking controller at the workshop approach"):
		return
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SAVE))
	OS.set_environment("ENFRACTAL_ART_PROFILE", original_profile)
	OS.set_environment("ENFRACTAL_START_WALK", original_start_walk)
	print("Workshop art smoke passed: same saved edit and walkable join; standard triangles=", standard_stats["triangle_upper_bound"], "; low triangles=", low_stats["triangle_upper_bound"])
	quit(0)


func _has_art_parts(path_body: StaticBody3D) -> bool:
	var shoulder: MeshInstance3D = path_body.get_node_or_null("IllustrativeSoilShoulder")
	var mesh: MeshInstance3D
	for child in path_body.get_children():
		if child is MeshInstance3D and child.name != "IllustrativeSoilShoulder":
			mesh = child
			break
	return shoulder != null and not shoulder.visible and path_body.get_node_or_null("StoneCourseJoints") != null and mesh != null and mesh.material_override is ShaderMaterial and mesh.material_override.shader.resource_path == "res://shaders/painterly_surface.gdshader"


func _water_has_upward_front(dressing: Node3D) -> bool:
	var water: MeshInstance3D = dressing.get_node_or_null("IllustrativeSeasonalRainGarden/IllustrativeShallowSeasonalWater")
	if water == null or not water.material_override is StandardMaterial3D or water.material_override.cull_mode != BaseMaterial3D.CULL_BACK:
		return false
	var arrays: Array = water.mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	if indices.size() < 3:
		return false
	# Godot's stock plane supplies the convention: clockwise front has a
	# negative conventional cross-product Y, despite its upward normals.
	var control: Array = PlaneMesh.new().get_mesh_arrays()
	var control_vertices: PackedVector3Array = control[Mesh.ARRAY_VERTEX]
	var control_indices: PackedInt32Array = control[Mesh.ARRAY_INDEX]
	var ca := control_vertices[control_indices[0]]
	var cb := control_vertices[control_indices[1]]
	var cc := control_vertices[control_indices[2]]
	if (cb - ca).cross(cc - ca).y >= 0.0:
		return false
	for index in range(0, indices.size(), 3):
		var a: Vector3 = vertices[indices[index]]
		var b: Vector3 = vertices[indices[index + 1]]
		var c: Vector3 = vertices[indices[index + 2]]
		if (b - a).cross(c - a).normalized().y >= -0.5:
			return false
	return true


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
