extends RefCounted
## A deliberately small local creation vocabulary. This translates a player's
## stone-platform intent into the canonical WorldState command; it never mutates
## the state or grants itself permission. Future manual/AI front ends can use the
## same operation and the same trusted authority context.

const PLATFORM_KIND := "stone_platform"
const GENERATOR_ID := "stone_platform"
const GENERATOR_VERSION := 1
const MAX_PLATFORM_COUNT := 16
const MIN_SIDE_M := 1.0
const MAX_SIDE_M := 6.0
const MAX_GROUND_VARIATION_M := 0.75
const PLATFORM_RISE_M := 0.42


## Input commands:
##   place_platform  {action_id, expected_revision, x_m, z_m, yaw_deg, width_m, depth_m}
##   revise_platform {same fields, entity_id}
##   remove_platform {action_id, expected_revision, entity_id}
## Authority is supplied by the local host, never copied from an input command:
##   {actor_id, world_id, frame_id, can_edit, plot: {id, min_x_m, max_x_m, min_z_m, max_z_m}}
## The caller must pass a pinned-base terrain height sampler and submit a
## successful result's `command` to WorldState.apply_validated_edit().
static func prepare(command: Dictionary, state, authority: Dictionary, terrain_height: Callable) -> Dictionary:
	if state == null or not terrain_height.is_valid():
		return _error("missing_world_or_terrain")
	if not _valid_action_id(command.get("action_id")):
		return _error("invalid_action_id")
	if typeof(command.get("expected_revision")) != TYPE_INT or int(command["expected_revision"]) < 0:
		return _error("invalid_revision")
	if not _valid_authority(state, authority):
		return _error("authority_denied")
	var plot: Dictionary = authority["plot"]
	if not _valid_plot(plot):
		return _error("invalid_plot")

	var action_id: String = command["action_id"]
	var actor_id: String = authority["actor_id"]
	var plot_id: String = plot["id"]
	var result_command := {
		"action_id": action_id,
		"expected_revision": command["expected_revision"],
		"actor_id": actor_id,
		"plot_id": plot_id,
	}
	# A committed action must reach the ledger for fingerprint comparison even if
	# its target was subsequently removed or the plot has since filled up.
	var is_replay: bool = state.has_action(action_id)
	match command.get("op", ""):
		"place_platform", "revise_platform":
			var compiled: Dictionary = _platform_entity(command, state, actor_id, plot, terrain_height)
			if not compiled.get("ok", false):
				return compiled
			var requested_entity: Dictionary = compiled["entity"]
			if command["op"] == "place_platform":
				if not is_replay and _platform_count(state, plot_id) >= MAX_PLATFORM_COUNT:
					return _error("plot_platform_limit")
				result_command["op"] = "place"
			else:
				var target_id: Variant = command.get("entity_id", "")
				if not _valid_entity_id(target_id):
					return _error("invalid_entity_id")
				if not is_replay:
					var target: Dictionary = state.get_entity(target_id)
					var target_error := _target_error(target, actor_id, plot_id)
					if not target_error.is_empty():
						return _error(target_error)
				result_command["op"] = "update"
				result_command["entity_id"] = target_id
			result_command["entity"] = requested_entity
		"remove_platform":
			var target_id: Variant = command.get("entity_id", "")
			if not _valid_entity_id(target_id):
				return _error("invalid_entity_id")
			if not is_replay:
				var target: Dictionary = state.get_entity(target_id)
				var target_error := _target_error(target, actor_id, plot_id)
				if not target_error.is_empty():
					return _error(target_error)
			result_command["op"] = "remove"
			result_command["entity_id"] = target_id
		_:
			return _error("unsupported_operation")
	return {"ok": true, "command": result_command}


## WorldState deliberately stores generic JSON. Check its semantic and physical
## contract again before turning a loaded entity into a mesh or collider. This
## fails closed for unknown generator versions or new fields until migrated.
static func validate_stored_entity(entity: Variant, state, plot: Dictionary, terrain_height: Callable) -> Dictionary:
	if state == null or not terrain_height.is_valid():
		return _error("missing_world_or_terrain")
	if not _valid_plot(plot):
		return _error("invalid_plot")
	if typeof(entity) != TYPE_DICTIONARY:
		return _error("invalid_stored_entity")
	var stored: Dictionary = entity
	if not _only_keys(stored, ["id", "kind", "world_id", "frame_id", "plot_id", "owner_id", "transform", "params", "material_role", "collision_role", "generator", "provenance"]):
		return _error("invalid_stored_entity")
	if not _valid_entity_id(stored.get("id")) or stored.get("kind") != PLATFORM_KIND:
		return _error("invalid_stored_identity")
	if stored.get("world_id") != state.world_id or stored.get("frame_id") != state.frame_id:
		return _error("stored_world_mismatch")
	if stored.get("plot_id") != plot["id"]:
		return _error("stored_plot_mismatch")
	if not _valid_action_id(stored.get("owner_id")):
		return _error("invalid_stored_owner")
	if plot.has("owner_id") and stored["owner_id"] != plot["owner_id"]:
		return _error("invalid_stored_owner")
	if stored.get("material_role") != "rock" or stored.get("collision_role") != "static_solid":
		return _error("invalid_stored_roles")
	if typeof(stored.get("generator")) != TYPE_DICTIONARY:
		return _error("unsupported_stored_generator")
	var generator: Dictionary = stored["generator"]
	if not _only_keys(generator, ["id", "version"]) or generator.get("id") != GENERATOR_ID or not _finite_number(generator.get("version")) or float(generator["version"]) != float(GENERATOR_VERSION):
		return _error("unsupported_stored_generator")
	if stored.get("provenance") != {"classification": "player_change", "author_id": stored["owner_id"]}:
		return _error("invalid_stored_provenance")
	if typeof(stored.get("transform")) != TYPE_DICTIONARY or typeof(stored.get("params")) != TYPE_DICTIONARY:
		return _error("invalid_stored_geometry")
	var transform: Dictionary = stored["transform"]
	var params: Dictionary = stored["params"]
	if not _only_keys(transform, ["x_m", "y_m", "z_m", "yaw_deg"]) or not _only_keys(params, ["width_m", "depth_m", "thickness_m", "rise_m"]):
		return _error("invalid_stored_geometry")
	for key in ["x_m", "y_m", "z_m", "yaw_deg"]:
		if not _finite_number(transform.get(key)):
			return _error("invalid_stored_geometry")
	for key in ["width_m", "depth_m", "thickness_m", "rise_m"]:
		if not _finite_number(params.get(key)):
			return _error("invalid_stored_geometry")
	var request := {
		"x_m": transform["x_m"], "z_m": transform["z_m"], "yaw_deg": transform["yaw_deg"],
		"width_m": params["width_m"], "depth_m": params["depth_m"],
	}
	var rebuilt: Dictionary = _platform_entity(request, state, stored["owner_id"], plot, terrain_height)
	if not rebuilt.get("ok", false):
		return _error("invalid_stored_placement")
	var expected: Dictionary = rebuilt["entity"]
	if absf(float(transform["y_m"]) - float(expected["transform"]["y_m"])) > 0.001:
		return _error("stored_terrain_anchor_mismatch")
	if absf(float(params["thickness_m"]) - float(expected["params"]["thickness_m"])) > 0.001:
		return _error("stored_terrain_anchor_mismatch")
	if absf(float(params["rise_m"]) - PLATFORM_RISE_M) > 0.000001:
		return _error("invalid_stored_geometry")
	return {"ok": true}


static func _platform_entity(command: Dictionary, state, actor_id: String, plot: Dictionary, terrain_height: Callable) -> Dictionary:
	for key in ["x_m", "z_m", "yaw_deg", "width_m", "depth_m"]:
		if not _finite_number(command.get(key)):
			return _error("invalid_number")
	var x: float = float(command["x_m"])
	var z: float = float(command["z_m"])
	var yaw: float = float(command["yaw_deg"])
	var width: float = float(command["width_m"])
	var depth: float = float(command["depth_m"])
	if width < MIN_SIDE_M or width > MAX_SIDE_M or depth < MIN_SIDE_M or depth > MAX_SIDE_M:
		return _error("invalid_platform_size")
	if absf(yaw) > 180.0:
		return _error("invalid_rotation")
	var angle := deg_to_rad(yaw)
	var cosine := cos(angle)
	var sine := sin(angle)
	var extent_x := (absf(width * cosine) + absf(depth * sine)) * 0.5
	var extent_z := (absf(width * sine) + absf(depth * cosine)) * 0.5
	if x - extent_x < float(plot["min_x_m"]) or x + extent_x > float(plot["max_x_m"]):
		return _error("outside_authorized_plot")
	if z - extent_z < float(plot["min_z_m"]) or z + extent_z > float(plot["max_z_m"]):
		return _error("outside_authorized_plot")

	var heights: Array[float] = []
	var center_height = terrain_height.call(x, z)
	if not _finite_number(center_height):
		return _error("invalid_terrain")
	heights.append(float(center_height))
	for dx in [-width * 0.5, width * 0.5]:
		for dz in [-depth * 0.5, depth * 0.5]:
			# Godot's positive Y rotation turns local +X toward world -Z.
			var sample_x: float = x + dx * cosine + dz * sine
			var sample_z: float = z - dx * sine + dz * cosine
			var height = terrain_height.call(sample_x, sample_z)
			if not _finite_number(height):
				return _error("invalid_terrain")
			heights.append(float(height))
	heights.sort()
	var ground_min: float = heights[0]
	var ground_max: float = heights[heights.size() - 1]
	if ground_max - ground_min > MAX_GROUND_VARIATION_M:
		return _error("terrain_too_steep")
	# The foundation embeds slightly into the sampled ground. Its top clears the
	# highest sampled corner; its source dimensions remain editable on reload.
	var bottom_y := ground_min - 0.08
	var top_y := ground_max + PLATFORM_RISE_M
	return {"ok": true, "entity": {
		"kind": PLATFORM_KIND,
		"world_id": state.world_id,
		"frame_id": state.frame_id,
		"plot_id": plot["id"],
		"owner_id": actor_id,
		"transform": {"x_m": x, "y_m": (bottom_y + top_y) * 0.5, "z_m": z, "yaw_deg": yaw},
		"params": {"width_m": width, "depth_m": depth, "thickness_m": top_y - bottom_y, "rise_m": PLATFORM_RISE_M},
		"material_role": "rock",
		"collision_role": "static_solid",
		"generator": {"id": GENERATOR_ID, "version": GENERATOR_VERSION},
		"provenance": {"classification": "player_change", "author_id": actor_id},
	}}


static func _valid_authority(state, authority: Dictionary) -> bool:
	return authority.get("can_edit", false) == true \
		and typeof(authority.get("actor_id")) == TYPE_STRING \
		and not String(authority["actor_id"]).is_empty() \
		and authority.get("world_id") == state.world_id \
		and authority.get("frame_id") == state.frame_id \
		and typeof(authority.get("plot")) == TYPE_DICTIONARY \
		and (not authority["plot"].has("owner_id") or authority["plot"]["owner_id"] == authority["actor_id"])


static func _valid_plot(plot: Dictionary) -> bool:
	if typeof(plot.get("id")) != TYPE_STRING or String(plot["id"]).is_empty():
		return false
	if plot.has("owner_id") and not _valid_action_id(plot["owner_id"]):
		return false
	for key in ["min_x_m", "max_x_m", "min_z_m", "max_z_m"]:
		if not _finite_number(plot.get(key)):
			return false
	return float(plot["min_x_m"]) < float(plot["max_x_m"]) \
		and float(plot["min_z_m"]) < float(plot["max_z_m"])


static func _only_keys(value: Dictionary, keys: Array) -> bool:
	if value.size() != keys.size():
		return false
	for key in keys:
		if not value.has(key):
			return false
	return true


static func _platform_count(state, plot_id: String) -> int:
	var count := 0
	for entity in state.get_entities().values():
		if entity.get("kind") == PLATFORM_KIND and entity.get("plot_id") == plot_id:
			count += 1
	return count


static func _target_error(target: Dictionary, actor_id: String, plot_id: String) -> String:
	if target.is_empty():
		return "entity_not_found"
	if target.get("kind") != PLATFORM_KIND:
		return "wrong_entity_kind"
	if target.get("plot_id") != plot_id:
		return "outside_authorized_plot"
	if target.get("owner_id") != actor_id:
		return "not_owner"
	return ""


static func _valid_entity_id(value: Variant) -> bool:
	return typeof(value) == TYPE_STRING and String(value).begins_with("edit:") and _valid_action_id(String(value).substr(5))


static func _valid_action_id(value: Variant) -> bool:
	if typeof(value) != TYPE_STRING or String(value).is_empty() or String(value).length() > 64:
		return false
	for i in range(String(value).length()):
		var code: int = String(value).unicode_at(i)
		if not ((code >= 48 and code <= 57) or (code >= 65 and code <= 90) or (code >= 97 and code <= 122) or code == 45 or code == 95):
			return false
	return true


static func _finite_number(value: Variant) -> bool:
	if typeof(value) != TYPE_INT and typeof(value) != TYPE_FLOAT:
		return false
	var number: float = float(value)
	return number == number and absf(number) <= 1000000.0


static func _error(code: String) -> Dictionary:
	return {"ok": false, "error": code}
