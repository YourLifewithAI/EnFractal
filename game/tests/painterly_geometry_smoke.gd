extends SceneTree
## Regression for the audited known fixtures, not arbitrary custom normals.

var failures: Array[String] = []
var triangle_count := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var stock_plane := PlaneMesh.new()
	_check_normals(stock_plane, "Godot stock plane control")
	_check_normals(BoxMesh.new(), "Godot stock box control")
	var scene = load("res://scenes/art_reference.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	for label in ["USGSHeightPatch", "CoarseUSGSBackdrop", "MappedTrailSoil", "CreekBank", "CreekWater", "IllustrativeCreekRiffles", "IllustrativeLookoutTrailJoin"]:
		_check_normals(scene.content.get_node(label).mesh, label)
	_check_normals(scene._trunk_mesh(), "reference trunk")
	_check_rock_outward(scene._rock_mesh(), "reference rock")
	for variant in range(3):
		_check_normals(scene._foliage_mesh(variant), "reference canopy %d" % variant)
		_check_normals(scene._leaf_cluster_mesh(variant), "reference leaf cluster %d" % variant)
	var dressing = load("res://scripts/workshop_art_dressing.gd").new()
	_check_normals(dressing._crown_mesh(false), "workshop oak")
	_check_normals(dressing._crown_mesh(true), "workshop juniper")
	_check_rock_outward(dressing._rock_mesh(), "workshop rock")
	dressing.free()
	var source = scene.map_runtime
	var manifest: Dictionary = source.manifest
	var job = load("res://scripts/terrain_mesh_job.gd").new()
	job.configure(source, int(manifest["side_m"]), int(manifest["sample_spacing_m"]), int(manifest["tile_side_m"]), float(manifest["height_origin_m"]), float(manifest["height_min_m"]), float(manifest["height_max_m"]))
	for step in [2, 8, 16, 32]:
		job.run(3, 3, step)
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, job.arrays)
		_check_normals(mesh, "map terrain spacing %d" % step)
		# Index corrections must not displace the map surface or its normals.
		var vertices: PackedVector3Array = job.arrays[Mesh.ARRAY_VERTEX]
		for vertex in vertices:
			if absf(vertex.y - source.surface_height_at(vertex.x, vertex.z)) > 0.002:
				failures.append("Terrain vertex changed source height at spacing %d" % step)
				break
	for role in scene.materials:
		var expected := BaseMaterial3D.CULL_DISABLED if role == "grass" else BaseMaterial3D.CULL_BACK
		if scene.materials[role].cull_mode != expected:
			failures.append("Unexpected face culling for " + role)
	if not failures.is_empty():
		for failure in failures:
			push_error(failure)
		quit(1)
		return
	print("Painterly geometry regression passed: %d triangles; stock controls, terrain LOD/stitching, source heights, ribbons, trunk/rock surfaces and preserved canopy fronts" % triangle_count)
	quit(0)


func _check_normals(mesh: Mesh, label: String) -> void:
	var arrays := mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	for offset in range(0, indices.size(), 3):
		var a := indices[offset]
		var b := indices[offset + 1]
		var c := indices[offset + 2]
		var cross := (vertices[b] - vertices[a]).cross(vertices[c] - vertices[a])
		var expected_normal := normals[a] + normals[b] + normals[c]
		triangle_count += 1
		if cross.dot(expected_normal) >= -0.000001:
			failures.append("%s has non-clockwise/degenerate triangle %d" % [label, offset / 3])
			return


func _check_rock_outward(mesh: Mesh, label: String) -> void:
	var arrays := mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	var cap_center := vertices.size() - 1
	for offset in range(0, indices.size(), 3):
		var a := indices[offset]
		var b := indices[offset + 1]
		var c := indices[offset + 2]
		var cross := (vertices[b] - vertices[a]).cross(vertices[c] - vertices[a])
		var center := (vertices[a] + vertices[b] + vertices[c]) / 3.0
		# Closed ring sides face radially out, cap faces up. The workshop's
		# artistic shading normals lean upward and aren't a topology oracle.
		var outward := Vector3.UP if cap_center in [a, b, c] else Vector3(center.x, 0.0, center.z)
		triangle_count += 1
		if cross.dot(outward) >= -0.000001:
			failures.append("%s has inward/degenerate surface triangle %d" % [label, offset / 3])
			return
