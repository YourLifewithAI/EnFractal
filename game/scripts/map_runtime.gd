class_name MapRuntime
extends RefCounted
## Read-only access to one offline EnFractal map package. Local coordinates are
## metres: +X east, +Z south, and Y relative to the package's height origin.
## Feature queries are AABB broad-phase queries, not exact geometry tests.

const FORMAT := "enfractal-map-v0"
const FEATURE_KINDS := [
	"roads", "trails", "waterways", "water_areas", "vegetation_areas",
	"developed_areas", "buildings", "trees",
]

var manifest: Dictionary = {}
var features: Dictionary = {}
var last_error := ""

var _heights := PackedByteArray()
var _grid_side := 0
var _side_m := 0
var _spacing_m := 0
var _tile_side_m := 0
var _height_offset_m := 0.0
var _height_scale_m := 0.0
var _height_origin_m := 0.0
var _tile_count := 0
var _feature_bounds: Dictionary = {}
var _feature_tiles: Dictionary = {}


func load_package(package_path: String, expected_map_id: String = "") -> bool:
	_clear()
	var directory := package_path.trim_suffix("/") + "/"
	var manifest_path := directory + "manifest.json"
	if not FileAccess.file_exists(manifest_path):
		return _fail("Missing map manifest: " + manifest_path)
	var parsed_manifest: Variant = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	if not parsed_manifest is Dictionary:
		return _fail("Map manifest is not a JSON object")
	if parsed_manifest.get("format", "") != FORMAT:
		return _fail("Unsupported map format: " + str(parsed_manifest.get("format", "")))
	var map_id := str(parsed_manifest.get("map_id", ""))
	if map_id.is_empty() or (not expected_map_id.is_empty() and map_id != expected_map_id):
		return _fail("Map ID differs from the requested package: " + map_id)
	var grid_side := int(parsed_manifest.get("grid_side", 0))
	var side_m := int(parsed_manifest.get("side_m", 0))
	var spacing_m := int(parsed_manifest.get("sample_spacing_m", 0))
	var tile_side_m := int(parsed_manifest.get("tile_side_m", 0))
	if grid_side < 2 or side_m <= 0 or spacing_m <= 0 or tile_side_m <= 0:
		return _fail("Invalid map grid or tile dimensions")
	if (grid_side - 1) * spacing_m != side_m or side_m % tile_side_m != 0 or tile_side_m % spacing_m != 0:
		return _fail("Map grid and tile dimensions do not align")
	var height_scale_m := float(parsed_manifest.get("height_scale_m", 0.0))
	if height_scale_m <= 0.0 or not is_finite(height_scale_m):
		return _fail("Invalid map height scale")
	var heights_path := directory + str(parsed_manifest.get("heights_file", ""))
	var features_path := directory + str(parsed_manifest.get("features_file", ""))
	if not FileAccess.file_exists(heights_path) or not FileAccess.file_exists(features_path):
		return _fail("Map height or feature file is missing")
	var heights := FileAccess.get_file_as_bytes(heights_path)
	if heights.size() != grid_side * grid_side * 2:
		return _fail("Map height grid byte count differs from manifest")
	if FileAccess.get_sha256(heights_path) != str(parsed_manifest.get("heights_sha256", "")):
		return _fail("Map height grid checksum differs from manifest")
	if FileAccess.get_sha256(features_path) != str(parsed_manifest.get("features_sha256", "")):
		return _fail("Map feature checksum differs from manifest")
	var parsed_features: Variant = JSON.parse_string(FileAccess.get_file_as_string(features_path))
	if not parsed_features is Dictionary:
		return _fail("Map features are not a JSON object")
	var counts: Dictionary = parsed_manifest.get("feature_counts", {})
	for kind in FEATURE_KINDS:
		if not parsed_features.get(kind, null) is Array:
			return _fail("Missing map feature category: " + kind)
		if parsed_features[kind].size() != int(counts.get(kind, -1)):
			return _fail("Map feature count differs from manifest: " + kind)

	manifest = parsed_manifest
	features = parsed_features
	_heights = heights
	_grid_side = grid_side
	_side_m = side_m
	_spacing_m = spacing_m
	_tile_side_m = tile_side_m
	_height_offset_m = float(manifest.get("height_offset_m", 0.0))
	_height_scale_m = height_scale_m
	_height_origin_m = float(manifest.get("height_origin_m", 0.0))
	_tile_count = side_m / tile_side_m
	_build_feature_index()
	return true


func is_loaded() -> bool:
	return _grid_side > 0


func contains_local(x: float, z: float) -> bool:
	var half := float(_side_m) * 0.5
	return is_loaded() and is_finite(x) and is_finite(z) and x >= -half and x <= half and z >= -half and z <= half


func height_at_grid(row: int, column: int) -> float:
	## Clamped grid lookup is useful for terrain normals at package edges.
	if not is_loaded():
		return NAN
	row = clampi(row, 0, _grid_side - 1)
	column = clampi(column, 0, _grid_side - 1)
	var byte_index := (row * _grid_side + column) * 2
	return _height_offset_m + float(_heights.decode_u16(byte_index)) * _height_scale_m - _height_origin_m


func height_at(x: float, z: float) -> float:
	## Bilinear local height. Outside the package there is no known surface.
	if not contains_local(x, z):
		return NAN
	return _height_at_clamped(x, z)


func surface_height_at(x: float, z: float) -> float:
	## Exact piecewise-planar surface used by the fixed-resolution collision grid.
	## Each source cell is split on the diagonal from its northeast corner (b)
	## to southwest corner (c): triangles a-c-b and b-c-d. Bilinear height_at()
	## can differ substantially at steep cells and must not place physics bodies.
	if not contains_local(x, z):
		return NAN
	var half := float(_side_m) * 0.5
	var grid_x := (x + half) / _spacing_m
	var grid_z := (z + half) / _spacing_m
	var column := mini(int(floor(grid_x)), _grid_side - 2)
	var row := mini(int(floor(grid_z)), _grid_side - 2)
	var fx := clampf(grid_x - column, 0.0, 1.0)
	var fz := clampf(grid_z - row, 0.0, 1.0)
	var a := height_at_grid(row, column)
	var b := height_at_grid(row, column + 1)
	var c := height_at_grid(row + 1, column)
	if fx + fz <= 1.0:
		return a * (1.0 - fx - fz) + b * fx + c * fz
	var d := height_at_grid(row + 1, column + 1)
	return b * (1.0 - fz) + c * (1.0 - fx) + d * (fx + fz - 1.0)


func normal_at(x: float, z: float) -> Vector3:
	if not contains_local(x, z):
		return Vector3.ZERO
	var step := float(_spacing_m)
	var dx := (_height_at_clamped(x + step, z) - _height_at_clamped(x - step, z)) / (2.0 * step)
	var dz := (_height_at_clamped(x, z + step) - _height_at_clamped(x, z - step)) / (2.0 * step)
	return Vector3(-dx, 1.0, -dz).normalized()


func tile_for_local(x: float, z: float) -> Vector2i:
	## Returns (-1, -1) outside. The positive outer edge belongs to the last tile.
	if not contains_local(x, z):
		return Vector2i(-1, -1)
	var half := float(_side_m) * 0.5
	return Vector2i(
		clampi(int(floor((x + half) / _tile_side_m)), 0, _tile_count - 1),
		clampi(int(floor((z + half) / _tile_side_m)), 0, _tile_count - 1)
	)


func is_valid_tile(tile: Vector2i) -> bool:
	return is_loaded() and tile.x >= 0 and tile.y >= 0 and tile.x < _tile_count and tile.y < _tile_count


func tile_bounds(tile: Vector2i) -> Rect2:
	if not is_valid_tile(tile):
		return Rect2()
	var half := float(_side_m) * 0.5
	return Rect2(Vector2(-half + tile.x * _tile_side_m, -half + tile.y * _tile_side_m), Vector2(_tile_side_m, _tile_side_m))


func tile_count_per_side() -> int:
	return _tile_count


func query_features(kind: String, bounds: Rect2) -> Array[Dictionary]:
	## Returns source records whose bounding boxes touch bounds. A line or polygon
	## may still miss the rectangle; callers needing exact overlap must refine it.
	var result: Array[Dictionary] = []
	if not is_loaded() or not _feature_tiles.has(kind):
		return result
	var search_bounds := bounds.abs()
	if not is_finite(search_bounds.position.x) or not is_finite(search_bounds.position.y) or not is_finite(search_bounds.end.x) or not is_finite(search_bounds.end.y):
		return result
	var map_bounds := Rect2(Vector2.ONE * (-float(_side_m) * 0.5), Vector2.ONE * float(_side_m))
	if not _rects_touch(search_bounds, map_bounds):
		return result
	var low := tile_for_local(clampf(search_bounds.position.x, map_bounds.position.x, map_bounds.end.x), clampf(search_bounds.position.y, map_bounds.position.y, map_bounds.end.y))
	var high := tile_for_local(clampf(search_bounds.end.x, map_bounds.position.x, map_bounds.end.x), clampf(search_bounds.end.y, map_bounds.position.y, map_bounds.end.y))
	var seen := {}
	var buckets: Dictionary = _feature_tiles[kind]
	var stored_bounds: Array = _feature_bounds[kind]
	var entries: Array = features[kind]
	for row in range(low.y, high.y + 1):
		for column in range(low.x, high.x + 1):
			for index in buckets.get(Vector2i(column, row), []):
				if seen.has(index):
					continue
				seen[index] = true
				if _rects_touch(search_bounds, stored_bounds[index]):
					result.append(entries[index])
	return result


func _height_at_clamped(x: float, z: float) -> float:
	var half := float(_side_m) * 0.5
	var column := clampf((x + half) / _spacing_m, 0.0, _grid_side - 1.0)
	var row := clampf((z + half) / _spacing_m, 0.0, _grid_side - 1.0)
	var x0 := int(floor(column))
	var y0 := int(floor(row))
	var fx := column - x0
	var fz := row - y0
	var north := lerpf(height_at_grid(y0, x0), height_at_grid(y0, x0 + 1), fx)
	var south := lerpf(height_at_grid(y0 + 1, x0), height_at_grid(y0 + 1, x0 + 1), fx)
	return lerpf(north, south, fz)


func _build_feature_index() -> void:
	_feature_bounds.clear()
	_feature_tiles.clear()
	for kind in FEATURE_KINDS:
		var rects: Array[Rect2] = []
		var buckets: Dictionary = {}
		var entries: Array = features[kind]
		for index in range(entries.size()):
			var rect := _feature_rect(kind, entries[index])
			rects.append(rect)
			var low := tile_for_local(rect.position.x, rect.position.y)
			var high := tile_for_local(rect.end.x, rect.end.y)
			if low.x < 0 or high.x < 0:
				continue
			for row in range(low.y, high.y + 1):
				for column in range(low.x, high.x + 1):
					var tile := Vector2i(column, row)
					if not buckets.has(tile):
						buckets[tile] = []
					buckets[tile].append(index)
		_feature_bounds[kind] = rects
		_feature_tiles[kind] = buckets


func _feature_rect(kind: String, item: Dictionary) -> Rect2:
	if kind == "trees":
		return Rect2(Vector2(float(item["x"]), float(item["z"])), Vector2.ZERO)
	var points: Array
	if kind == "buildings":
		points = item["footprint"]
	elif kind in ["water_areas", "vegetation_areas", "developed_areas"]:
		points = item["outline"]
	else:
		points = item["points"]
	if points.is_empty():
		return Rect2()
	var minimum := Vector2(INF, INF)
	var maximum := Vector2(-INF, -INF)
	for point in points:
		var xy := Vector2(float(point[0]), float(point[1]))
		minimum = minimum.min(xy)
		maximum = maximum.max(xy)
	return Rect2(minimum, maximum - minimum)


func _rects_touch(a: Rect2, b: Rect2) -> bool:
	return a.position.x <= b.end.x and a.end.x >= b.position.x and a.position.y <= b.end.y and a.end.y >= b.position.y


func _fail(message: String) -> bool:
	last_error = message
	return false


func _clear() -> void:
	manifest.clear()
	features.clear()
	_heights.clear()
	_feature_bounds.clear()
	_feature_tiles.clear()
	_grid_side = 0
	_side_m = 0
	_spacing_m = 0
	_tile_side_m = 0
	_tile_count = 0
	last_error = ""
