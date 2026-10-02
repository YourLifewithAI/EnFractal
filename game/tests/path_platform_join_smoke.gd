extends SceneTree

const Join = preload("res://scripts/path_platform_join.gd")


func _initialize() -> void:
	var platform := {
		"id": "edit:platform_1", "kind": "stone_platform",
		"world_id": "local_workshop", "frame_id": "barton_local",
		"transform": {"x_m": 0.0, "y_m": 0.0, "z_m": 0.0, "yaw_deg": 0.0},
		"params": {"width_m": 4.0, "depth_m": 2.0, "thickness_m": 0.5},
	}
	var path := {
		"id": "path:main", "kind": "stone_path",
		"world_id": "local_workshop", "frame_id": "barton_local",
		"width_m": 1.2,
		"points": [
			{"x_m": 0.0, "y_m": 0.0, "z_m": -6.0},
			{"x_m": 0.0, "y_m": 0.0, "z_m": -2.8},
		],
	}
	var original_platform := platform.duplicate(true)
	var first: Dictionary = Join.generate(path, platform)
	if not first.get("ok", false) or first != Join.generate(path, platform):
		_fail("join generation is not valid and repeatable")
		return
	var relation: Dictionary = first["relationship"]
	if relation["kind"] != "path_connects_to" or relation["attachment"] != "north" or absf(float(relation["params"]["end"]["z_m"]) + 1.0) > 0.001:
		_fail("path did not meet the platform north edge")
		return
	path["points"][1]["z_m"] = -3.2
	var moved: Dictionary = Join.generate(path, platform)
	if not moved.get("ok", false) or moved["relationship"]["id"] != relation["id"] or moved["relationship"]["params"]["start"] == relation["params"]["start"]:
		_fail("moving the path did not rebuild only its stable join")
		return
	if platform != original_platform:
		_fail("join generator changed its input platform")
		return
	var wrong_frame := path.duplicate(true)
	wrong_frame["frame_id"] = "another_frame"
	if Join.generate(wrong_frame, platform).get("error") != "frame_mismatch":
		_fail("cross-frame join was accepted")
		return
	var too_steep := path.duplicate(true)
	too_steep["points"][1]["y_m"] = -2.0
	if Join.generate(too_steep, platform).get("error") != "join_slope_out_of_range":
		_fail("unwalkable join was accepted")
		return
	var non_finite := path.duplicate(true)
	non_finite["points"][1]["x_m"] = INF
	if Join.generate(non_finite, platform).get("error") != "invalid_path":
		_fail("non-finite path endpoint was accepted")
		return
	var corner := path.duplicate(true)
	corner["points"][1]["x_m"] = 2.3
	if Join.generate(corner, platform).get("error") != "no_clear_platform_edge":
		_fail("unsupported corner join was accepted")
		return
	print("Path-platform join smoke passed: repeatable relationship, stable ID, local rebuild, rejection bounds")
	quit(0)


func _fail(message: String) -> void:
	push_error("Path-platform join smoke: " + message)
	quit(1)
