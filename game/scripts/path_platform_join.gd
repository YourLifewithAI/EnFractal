class_name PathPlatformJoin
extends RefCounted
## Pure, bounded relationship generator for the first structured-scene fixture.
## The returned semantic join is a recipe, not an authoritative placement.

const GENERATOR_ID := "path_platform_join"
const GENERATOR_VERSION := 1
const MIN_RUN_M := 0.75
const MAX_RUN_M := 3.0
const MAX_RISE_M := 0.75
const MAX_SLOPE := 0.5


static func generate(path: Dictionary, platform: Dictionary) -> Dictionary:
	if path.get("kind") != "stone_path" or platform.get("kind") != "stone_platform":
		return _error("unsupported_parts")
	for identity in [path.get("world_id"), path.get("frame_id"), platform.get("world_id"), platform.get("frame_id")]:
		if typeof(identity) != TYPE_STRING or String(identity).is_empty():
			return _error("invalid_part_identity")
	if path.get("world_id") != platform.get("world_id") or path.get("frame_id") != platform.get("frame_id"):
		return _error("frame_mismatch")
	if path.has("join_generator") and not _join_pin_matches(path["join_generator"]):
		return _error("unsupported_join_generator")
	if path.has("style") and platform.has("style") and not _styles_match(path["style"], platform["style"]):
		return _error("style_mismatch")
	if typeof(path.get("id")) != TYPE_STRING or typeof(platform.get("id")) != TYPE_STRING:
		return _error("invalid_part_identity")
	var path_id: String = path["id"]
	var platform_id: String = platform["id"]
	if path_id.is_empty() or platform_id.is_empty() or path_id.length() > 100 or platform_id.length() > 100 or path_id == platform_id:
		return _error("invalid_part_identity")
	var points: Variant = path.get("points")
	if not points is Array or points.size() < 2 or points.size() > 32:
		return _error("invalid_path")
	for point in points:
		if not point is Dictionary or not _point_is_finite(point):
			return _error("invalid_path")
	var end: Variant = points[points.size() - 1]
	if not _finite_number(path.get("width_m")):
		return _error("invalid_path")
	var width := float(path["width_m"])
	if width < 0.8 or width > 3.0:
		return _error("invalid_path_width")
	if not platform.get("transform") is Dictionary or not platform.get("params") is Dictionary:
		return _error("invalid_platform")
	var transform: Dictionary = platform["transform"]
	var params: Dictionary = platform["params"]
	for key in ["x_m", "y_m", "z_m", "yaw_deg"]:
		if not _finite_number(transform.get(key)):
			return _error("invalid_platform")
	for key in ["width_m", "depth_m", "thickness_m"]:
		if not _finite_number(params.get(key)):
			return _error("invalid_platform")
	var half_x := float(params["width_m"]) * 0.5
	var half_z := float(params["depth_m"]) * 0.5
	if half_x < 0.5 or half_z < 0.5 or half_x > 3.0 or half_z > 3.0 or float(params["thickness_m"]) <= 0.0 or float(params["thickness_m"]) > 2.0 or absf(float(transform["yaw_deg"])) > 180.0:
		return _error("invalid_platform")
	var theta := deg_to_rad(float(transform["yaw_deg"]))
	var cosine := cos(theta)
	var sine := sin(theta)
	var dx := float(end["x_m"]) - float(transform["x_m"])
	var dz := float(end["z_m"]) - float(transform["z_m"])
	var local_x := dx * cosine - dz * sine
	var local_z := dx * sine + dz * cosine
	var target_x := local_x
	var target_z := local_z
	var run := 0.0
	var attachment := ""
	if absf(local_x) > half_x and absf(local_z) <= half_z - width * 0.5:
		target_x = signf(local_x) * half_x
		run = absf(local_x) - half_x
		attachment = "east" if local_x > 0.0 else "west"
	elif absf(local_z) > half_z and absf(local_x) <= half_x - width * 0.5:
		target_z = signf(local_z) * half_z
		run = absf(local_z) - half_z
		attachment = "south" if local_z > 0.0 else "north"
	else:
		return _error("no_clear_platform_edge")
	if run < MIN_RUN_M or run > MAX_RUN_M:
		return _error("join_run_out_of_range")
	var top_y := float(transform["y_m"]) + float(params["thickness_m"]) * 0.5
	var rise := top_y - float(end["y_m"])
	if rise < 0.0 or rise > MAX_RISE_M or rise / run > MAX_SLOPE:
		return _error("join_slope_out_of_range")
	var target_world_x := float(transform["x_m"]) + target_x * cosine + target_z * sine
	var target_world_z := float(transform["z_m"]) - target_x * sine + target_z * cosine
	var start_point := {"x_m": float(end["x_m"]), "y_m": float(end["y_m"]), "z_m": float(end["z_m"])}
	var end_point := {"x_m": target_world_x, "y_m": top_y, "z_m": target_world_z}
	var relation_id := "join:" + (path_id + "|" + platform_id).sha256_text().substr(0, 20)
	var relationship := {
		"id": relation_id,
		"kind": "path_connects_to",
		"world_id": path["world_id"], "frame_id": path["frame_id"],
		"from_part_id": path_id, "to_part_id": platform_id,
		"attachment": attachment,
		"generator": {"id": GENERATOR_ID, "version": GENERATOR_VERSION},
		"params": {"start": start_point, "end": end_point, "width_m": width},
		"material_role": "rock", "collision_role": "walkable_static",
		"provenance": {"classification": "player_change", "derived_from": [path_id, platform_id]},
	}
	if path.has("style"):
		relationship["style"] = path["style"].duplicate(true)
	return {"ok": true, "relationship": relationship}


static func _point_is_finite(point: Dictionary) -> bool:
	return _finite_number(point.get("x_m")) and _finite_number(point.get("y_m")) and _finite_number(point.get("z_m"))


static func _join_pin_matches(value: Variant) -> bool:
	return typeof(value) == TYPE_DICTIONARY and value.size() == 2 \
		and value.get("id") == GENERATOR_ID and _finite_number(value.get("version")) \
		and float(value["version"]) == float(GENERATOR_VERSION)


static func _styles_match(first: Variant, second: Variant) -> bool:
	return typeof(first) == TYPE_DICTIONARY and typeof(second) == TYPE_DICTIONARY \
		and first.size() == 3 and second.size() == 3 \
		and first.get("family") == second.get("family") \
		and first.get("recipe_sha256") == second.get("recipe_sha256") \
		and _finite_number(first.get("version")) and _finite_number(second.get("version")) \
		and float(first["version"]) == float(second["version"])


static func _finite_number(value: Variant) -> bool:
	return (typeof(value) == TYPE_FLOAT or typeof(value) == TYPE_INT) and is_finite(float(value))


static func _error(code: String) -> Dictionary:
	return {"ok": false, "error": code}
