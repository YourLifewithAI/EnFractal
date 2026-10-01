extends SceneTree

const Runtime = preload("res://scripts/map_runtime.gd")


func _initialize() -> void:
	var map = Runtime.new()
	if not map.load_package("res://maps/barton_creek", "barton_creek_v0"):
		_fail(map.last_error)
		return
	if map.manifest["map_id"] != "barton_creek_v0" or map.tile_count_per_side() != 8:
		_fail("Wrong Barton Creek package or tile count")
		return
	if absf(map.height_at(0.0, 0.0)) > 0.02:
		_fail("Local center must be near its height origin")
		return
	if not is_nan(map.height_at(2049.0, 0.0)) or map.tile_for_local(2049.0, 0.0) != Vector2i(-1, -1):
		_fail("Out-of-package surface queries must fail closed")
		return
	if map.tile_for_local(0.0, 0.0) != Vector2i(4, 4):
		_fail("Local origin maps to the wrong terrain tile")
		return
	if map.tile_for_local(2048.0, 2048.0) != Vector2i(7, 7):
		_fail("Positive map edge must belong to the final tile")
		return
	if map.tile_for_local(-2048.0, -2048.0) != Vector2i.ZERO:
		_fail("Negative map edge must belong to the first tile")
		return
	if map.tile_for_local(-1536.001, -2048.0) != Vector2i.ZERO or map.tile_for_local(-1536.0, -2048.0) != Vector2i(1, 0):
		_fail("Tile lookup disagrees at a shared boundary")
		return
	if map.tile_bounds(Vector2i(1, 0)).position != Vector2(-1536.0, -2048.0):
		_fail("Tile bounds disagree with tile lookup")
		return
	if absf(map.height_at(-1536.0, -2048.0) - map.height_at_grid(0, 256)) > 0.001:
		_fail("Tile seam height differs from the shared grid")
		return
	for corner in [[-2048.0, -2048.0, 0, 0], [2048.0, -2048.0, 0, 2048], [-2048.0, 2048.0, 2048, 0], [2048.0, 2048.0, 2048, 2048]]:
		if absf(map.height_at(corner[0], corner[1]) - map.height_at_grid(corner[2], corner[3])) > 0.001:
			_fail("Grid corner and bilinear height disagree")
			return
	if map.normal_at(0.0, 0.0).dot(Vector3.UP) <= 0.0:
		_fail("Terrain normal does not face upward")
		return
	var mall_found := false
	for building in map.query_features("buildings", Rect2(100.0, -1000.0, 500.0, 500.0)):
		if int(building["osm_id"]) == 27453848:
			mall_found = true
	if not mall_found:
		_fail("Mall footprint missing from local feature query")
		return
	var whole_map := Rect2(-2048.0, -2048.0, 4096.0, 4096.0)
	for kind in Runtime.FEATURE_KINDS:
		if map.query_features(kind, whole_map).size() != int(map.manifest["feature_counts"][kind]):
			_fail("Spatial index dropped or duplicated " + kind)
			return
	if not map.query_features("unknown", Rect2(-1.0, -1.0, 2.0, 2.0)).is_empty():
		_fail("Unknown feature category should be empty")
		return
	if not map.query_features("buildings", Rect2(5000.0, 5000.0, 10.0, 10.0)).is_empty():
		_fail("Off-map feature query should be empty")
		return
	var wrong_map = Runtime.new()
	if wrong_map.load_package("res://maps/barton_creek", "another_map") or wrong_map.is_loaded():
		_fail("Wrong requested map ID must fail before map use")
		return
	print("MapRuntime smoke checks passed: heights, tile edges, normals, and spatial lookup")
	quit(0)


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
