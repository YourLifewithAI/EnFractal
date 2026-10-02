extends SceneTree
## Bounded, reproducible source/art split and two graphics profiles.


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var scene: Node3D = load("res://scenes/art_reference.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	if scene.map_runtime.manifest.get("map_id") != "barton_creek_v0":
		_fail("Art fixture did not load the pinned source map")
		return
	if scene.content.get_node_or_null("USGSHeightPatch") == null or scene.content.get_node_or_null("MappedTrailSoil") == null or scene.content.get_node_or_null("CreekWater") == null:
		_fail("Art fixture lost source terrain, trail, or creek")
		return
	if scene.content.get_node_or_null("illustrative_trail_lookout_v1") == null or scene.content.get_node_or_null("IllustrativeLookoutTrailJoin") == null:
		_fail("Authored lookout or its separate join is missing")
		return
	var creek_bank: MeshInstance3D = scene.content.get_node("CreekBank")
	var bank_arrays: Array = creek_bank.mesh.surface_get_arrays(0)
	if bank_arrays[Mesh.ARRAY_COLOR].size() != bank_arrays[Mesh.ARRAY_VERTEX].size():
		_fail("Mapped creek bank lost its cross-stream color variation")
		return
	if scene.stats["terrain_triangles"] > int(scene.recipe["budget"]["terrain_triangles_standard_max"]) or scene.stats["distant_terrain_triangles"] > int(scene.recipe["budget"]["distant_terrain_triangles_max"]):
		_fail("Standard terrain exceeds its declared geometry budget")
		return
	if scene.stats["triangle_upper_bound"] > int(scene.recipe["budget"]["triangle_upper_bound_standard_max"]):
		_fail("Standard scene exceeds its declared total geometry budget")
		return
	if scene.materials.size() > int(scene.recipe["budget"]["shared_materials_max"]):
		_fail("Art fixture exceeded its shared-material budget")
		return
	if absf(scene.camera.position.y - scene.map_runtime.surface_height_at(scene.camera.position.x, scene.camera.position.z) - 1.67) > 0.01:
		_fail("Art camera is no longer at walking height")
		return
	var standard_trees: int = scene.stats["trees"]
	var standard_leaf_clusters: int = scene.stats["leaf_clusters"]
	var standard_grass: int = scene.stats["grass_tufts"]
	scene.set_profile("low")
	if scene.stats["terrain_triangles"] > int(scene.recipe["budget"]["terrain_triangles_low_max"]):
		_fail("Low terrain exceeds its declared geometry budget")
		return
	if scene.stats["triangle_upper_bound"] > int(scene.recipe["budget"]["triangle_upper_bound_low_max"]):
		_fail("Low scene exceeds its declared total geometry budget")
		return
	if scene.stats["trees"] != standard_trees or scene.stats["leaf_clusters"] >= standard_leaf_clusters or scene.stats["grass_tufts"] >= standard_grass or scene.sunlight.shadow_enabled:
		_fail("Low profile changed scene identity or did not reduce optional detail")
		return
	if scene.content.get_node_or_null("illustrative_trail_lookout_v1") == null:
		_fail("Low profile lost the semantic lookout silhouette")
		return
	print("Art reference smoke passed: source anchors, walking camera, semantic lookout, and bounded low profile")
	quit(0)


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
