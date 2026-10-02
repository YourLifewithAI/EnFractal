extends RefCounted
## Generates CPU-only terrain arrays. A worker may call run(); the active scene
## and ArrayMesh upload stay on the main thread. One instance has one owner.

var source
var map_side_m: int
var sample_spacing_m: int
var tile_side_m: int
var height_origin_m: float
var height_min_m: float
var height_max_m: float
var arrays: Array = []
var build_us := 0


static func packed_array_bytes(tile_arrays: Array) -> int:
	# A reproducible vertex/index payload proxy, not a GPU allocation query.
	# Godot may retain copies and allocate additional driver-side memory.
	var vertices: PackedVector3Array = tile_arrays[Mesh.ARRAY_VERTEX]
	var normals: PackedVector3Array = tile_arrays[Mesh.ARRAY_NORMAL]
	var colors: PackedColorArray = tile_arrays[Mesh.ARRAY_COLOR]
	var uvs: PackedVector2Array = tile_arrays[Mesh.ARRAY_TEX_UV]
	var indices: PackedInt32Array = tile_arrays[Mesh.ARRAY_INDEX]
	return vertices.size() * 12 + normals.size() * 12 + colors.size() * 16 + uvs.size() * 8 + indices.size() * 4


func configure(map_source, side_m: int, spacing_m: int, tile_m: int, origin_m: float, min_m: float, max_m: float) -> void:
	source = map_source
	map_side_m = side_m
	sample_spacing_m = spacing_m
	tile_side_m = tile_m
	height_origin_m = origin_m
	height_min_m = min_m
	height_max_m = max_m


func run(tile_row: int, tile_column: int, step_m: int) -> void:
	var started := Time.get_ticks_usec()
	arrays = _build_arrays(tile_row, tile_column, step_m)
	build_us = Time.get_ticks_usec() - started


func _height_at_grid(row: int, column: int) -> float:
	return source.height_at_grid(row, column)


func _build_arrays(tile_row: int, tile_column: int, step_m: int) -> Array:
	var samples := tile_side_m / step_m
	var origin_col := tile_column * tile_side_m / sample_spacing_m
	var origin_row := tile_row * tile_side_m / sample_spacing_m
	var stride := step_m / sample_spacing_m
	var half: float = map_side_m * 0.5
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var uvs := PackedVector2Array()
	var indices := PackedInt32Array()
	var coarse_vertex_count := (samples + 1) * (samples + 1)
	vertices.resize(coarse_vertex_count)
	normals.resize(coarse_vertex_count)
	colors.resize(coarse_vertex_count)
	uvs.resize(coarse_vertex_count)
	# The fine tile reuses each source sample for height and four slope taps.
	# Cache one-sample halo once rather than decoding the same grid point in
	# five GDScript calls per vertex. Coarser tiles keep their tiny direct path.
	var cached_heights := PackedFloat32Array()
	var cache_side := samples + 3
	if step_m == sample_spacing_m:
		cached_heights.resize(cache_side * cache_side)
		for cache_row in range(cache_side):
			for cache_column in range(cache_side):
				cached_heights[cache_row * cache_side + cache_column] = _height_at_grid(origin_row + cache_row - 1, origin_col + cache_column - 1)
	var interior_cells := samples * samples if step_m == sample_spacing_m else maxi(0, samples - 2) * maxi(0, samples - 2)
	indices.resize(interior_cells * 6)
	var index_cursor := 0
	for row in range(samples + 1):
		for column in range(samples + 1):
			var grid_row := origin_row + row * stride
			var grid_column := origin_col + column * stride
			var x: float = -half + grid_column * sample_spacing_m
			var z: float = -half + grid_row * sample_spacing_m
			var y: float
			var dx: float
			var dz: float
			if not cached_heights.is_empty():
				var cache_index := (row + 1) * cache_side + column + 1
				y = cached_heights[cache_index]
				dx = (cached_heights[cache_index + 1] - cached_heights[cache_index - 1]) / float(sample_spacing_m * 2)
				dz = (cached_heights[cache_index + cache_side] - cached_heights[cache_index - cache_side]) / float(sample_spacing_m * 2)
			else:
				y = _height_at_grid(grid_row, grid_column)
				dx = (_height_at_grid(grid_row, grid_column + 1) - _height_at_grid(grid_row, grid_column - 1)) / float(sample_spacing_m * 2)
				dz = (_height_at_grid(grid_row + 1, grid_column) - _height_at_grid(grid_row - 1, grid_column)) / float(sample_spacing_m * 2)
			var slope := sqrt(dx * dx + dz * dz)
			var absolute_m := y + height_origin_m
			var relief: float = clampf((absolute_m - height_min_m) / maxf(1.0, height_max_m - height_min_m), 0.0, 1.0)
			var steepness := clampf((slope - 0.55) / 1.5, 0.0, 0.8)
			var vertex_index := row * (samples + 1) + column
			vertices[vertex_index] = Vector3(x, y, z)
			normals[vertex_index] = Vector3(-dx, 1.0, -dz).normalized()
			# Store source-derived measures; the style recipe chooses their colors.
			colors[vertex_index] = Color(relief, steepness, 0.0, 1.0)
			uvs[vertex_index] = Vector2((x + half) / map_side_m, (z + half) / map_side_m)
	for row in range(samples):
		for column in range(samples):
			if step_m > sample_spacing_m and (row == 0 or column == 0 or row == samples - 1 or column == samples - 1):
				continue # A narrow stitched ring replaces the coarse outer cells below.
			var a := row * (samples + 1) + column
			var b := a + 1
			var c := a + samples + 1
			var d := c + 1
			indices[index_cursor] = a
			indices[index_cursor + 1] = b
			indices[index_cursor + 2] = c
			indices[index_cursor + 3] = b
			indices[index_cursor + 4] = d
			indices[index_cursor + 5] = c
			index_cursor += 6
	if step_m > sample_spacing_m:
		# Every tile exposes the same 2 m source-height samples on its perimeter.
		# Only its outermost coarse-cell ring is refined; the interior keeps its LOD.
		for cell_row in range(samples):
			for cell_column in range(samples):
				if cell_row != 0 and cell_column != 0 and cell_row != samples - 1 and cell_column != samples - 1:
					continue
				var x0: float = -half + tile_column * tile_side_m + cell_column * step_m
				var z0: float = -half + tile_row * tile_side_m + cell_row * step_m
				var x1 := x0 + step_m
				var z1 := z0 + step_m
				# The perimeter is counterclockwise as seen from above; reverse each
				# fan triangle below for Godot's clockwise front faces. Subdivide
				# only sides touching the tile perimeter; inner edges remain coarse.
				var corners := PackedVector2Array([
					Vector2(x0, z0), Vector2(x0, z1),
					Vector2(x1, z1), Vector2(x1, z0),
				])
				var subdivisions := PackedInt32Array([
					stride if cell_column == 0 else 1,
					stride if cell_row == samples - 1 else 1,
					stride if cell_column == samples - 1 else 1,
					stride if cell_row == 0 else 1,
				])
				var perimeter := PackedVector2Array()
				for side in range(4):
					for section in range(subdivisions[side]):
						perimeter.push_back(corners[side].lerp(corners[(side + 1) % 4], float(section) / subdivisions[side]))
				var vertex_start := vertices.size()
				perimeter.push_back(Vector2((x0 + x1) * 0.5, (z0 + z1) * 0.5))
				for location in perimeter:
					var grid_column := int(round((location.x + half) / sample_spacing_m))
					var grid_row := int(round((location.y + half) / sample_spacing_m))
					var y := _height_at_grid(grid_row, grid_column)
					var dx := (_height_at_grid(grid_row, grid_column + 1) - _height_at_grid(grid_row, grid_column - 1)) / float(sample_spacing_m * 2)
					var dz := (_height_at_grid(grid_row + 1, grid_column) - _height_at_grid(grid_row - 1, grid_column)) / float(sample_spacing_m * 2)
					var slope := sqrt(dx * dx + dz * dz)
					var absolute_m := y + height_origin_m
					var relief: float = clampf((absolute_m - height_min_m) / maxf(1.0, height_max_m - height_min_m), 0.0, 1.0)
					var steepness := clampf((slope - 0.55) / 1.5, 0.0, 0.8)
					vertices.push_back(Vector3(location.x, y, location.y))
					normals.push_back(Vector3(-dx, 1.0, -dz).normalized())
					colors.push_back(Color(relief, steepness, 0.0, 1.0))
					uvs.push_back(Vector2((location.x + half) / map_side_m, (location.y + half) / map_side_m))
				var center_index := vertices.size() - 1
				var edge_count := perimeter.size() - 1
				for edge in range(edge_count):
					indices.append_array(PackedInt32Array([center_index, vertex_start + (edge + 1) % edge_count, vertex_start + edge]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	return arrays
