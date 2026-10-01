extends SceneTree

const Viewer = preload("res://scripts/map_viewer.gd")
const Runtime = preload("res://scripts/map_runtime.gd")


func _initialize() -> void:
	var source = Runtime.new()
	if not source.load_package("res://maps/barton_creek", "barton_creek_v0"):
		_fail(source.last_error)
		return
	# Keep this Node outside the scene tree: testing mesh construction should not
	# start the viewer, populate objects, or require a graphics context.
	var viewer = Viewer.new()
	viewer.map_runtime = source
	viewer.manifest = source.manifest
	viewer.grid_side = int(source.manifest["grid_side"])
	viewer.map_side_m = int(source.manifest["side_m"])
	viewer.sample_spacing_m = int(source.manifest["sample_spacing_m"])
	viewer.tile_side_m = int(source.manifest["tile_side_m"])
	viewer.height_origin_m = float(source.manifest["height_origin_m"])
	viewer.height_min_m = float(source.manifest["height_min_m"])
	viewer.height_max_m = float(source.manifest["height_max_m"])
	var start_us := Time.get_ticks_usec()
	var fine: ArrayMesh = viewer._make_tile_mesh(0, 0, 2)
	var fine_ms := float(Time.get_ticks_usec() - start_us) / 1000.0
	start_us = Time.get_ticks_usec()
	var medium: ArrayMesh = viewer._make_tile_mesh(1, 0, 8)
	var medium_ms := float(Time.get_ticks_usec() - start_us) / 1000.0
	start_us = Time.get_ticks_usec()
	var coarse: ArrayMesh = viewer._make_tile_mesh(0, 1, 32)
	var coarse_ms := float(Time.get_ticks_usec() - start_us) / 1000.0
	if not _edges_match(fine, coarse, "x", -1536.0):
		_fail("2 m/32 m east-west tile seam differs")
		return
	if not _edges_match(fine, medium, "z", -1536.0):
		_fail("2 m/8 m north-south tile seam differs")
		return
	var diagonal: ArrayMesh = viewer._make_tile_mesh(1, 1, 32)
	if not _edges_match(medium, diagonal, "x", -1536.0):
		_fail("8 m/32 m east-west tile seam differs")
		return
	if not _edges_match(coarse, diagonal, "z", -1536.0):
		_fail("32 m/32 m north-south tile seam differs")
		return
	var fine_triangles := _triangle_count(fine)
	var medium_triangles := _triangle_count(medium)
	var coarse_triangles := _triangle_count(coarse)
	if fine_triangles != 131072 or medium_triangles >= fine_triangles or coarse_triangles >= medium_triangles:
		_fail("Stitching accidentally erased interior LOD savings")
		return
	if not _triangles_face_up(medium) or not _triangles_face_up(coarse):
		_fail("Stitched triangles have flipped winding")
		return
	print("Terrain seam checks passed; triangles 2m/8m/32m: %d/%d/%d; vertices: %d/%d/%d; build ms: %.1f/%.1f/%.1f" % [fine_triangles, medium_triangles, coarse_triangles, _vertex_count(fine), _vertex_count(medium), _vertex_count(coarse), fine_ms, medium_ms, coarse_ms])
	viewer.free()
	quit(0)


func _triangle_count(mesh: ArrayMesh) -> int:
	return mesh.surface_get_arrays(0)[Mesh.ARRAY_INDEX].size() / 3


func _vertex_count(mesh: ArrayMesh) -> int:
	return mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX].size()


func _triangles_face_up(mesh: ArrayMesh) -> bool:
	var arrays := mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	for triangle in range(0, indices.size(), 3):
		var a := vertices[indices[triangle]]
		var b := vertices[indices[triangle + 1]]
		var c := vertices[indices[triangle + 2]]
		if (b - a).cross(c - a).y <= 0.0:
			return false
	return true


func _edges_match(a: ArrayMesh, b: ArrayMesh, axis: String, at: float) -> bool:
	var edge_a := _edge_samples(a, axis, at)
	var edge_b := _edge_samples(b, axis, at)
	if edge_a.size() != 257 or edge_b.size() != 257:
		return false
	for key in edge_a:
		if not edge_b.has(key) or absf(edge_a[key] - edge_b[key]) > 0.001:
			return false
	return true


func _edge_samples(mesh: ArrayMesh, axis: String, at: float) -> Dictionary:
	var arrays := mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	var samples := {}
	for index in indices:
		var vertex := vertices[index]
		var coordinate: float = vertex.x if axis == "x" else vertex.z
		if absf(coordinate - at) > 0.001:
			continue
		var along: float = vertex.z if axis == "x" else vertex.x
		var key := roundi(along * 1000.0)
		if samples.has(key) and absf(samples[key] - vertex.y) > 0.001:
			return {}
		samples[key] = vertex.y
	return samples


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
