extends RefCounted
class_name EnfractalWorldState
## Local prototype state: an immutable, hash-pinned map package plus sparse player edits.
## This is deliberately not an authority or permission system. Only commands already
## checked by the creation/permission layer may be passed to apply_validated_edit().

const SCHEMA := "enfractal-world-state"
# Version 2 adds the spatial-manifest digest. Version 1 saves cannot be safely
# placed after a coordinate/decode change, so they require an explicit migration.
const SCHEMA_VERSION := 2
const MAX_ENTITIES := 256
const MAX_ACTIONS := 2048
const MAX_COMMAND_BYTES := 16384
const MAX_SAVE_BYTES := 4 * 1024 * 1024

var world_id := ""
var frame_id := ""
var base_map: Dictionary = {}
var revision := 0
var last_error := ""

# Never serialize the terrain or OSM feature package here. These dictionaries contain
# only authored entities and action receipts for this one world.
var _entities: Dictionary = {}
var _actions: Dictionary = {}


func initialize(new_world_id: String, new_frame_id: String, manifest: Dictionary) -> bool:
	last_error = ""
	var pinned_base := _pin_base(manifest)
	if not _valid_token(new_world_id) or not _valid_token(new_frame_id) or pinned_base.is_empty():
		last_error = "invalid_world_identity_or_base"
		return false
	world_id = new_world_id
	frame_id = new_frame_id
	base_map = pinned_base
	revision = 0
	_entities.clear()
	_actions.clear()
	return true


func get_entity(entity_id: String) -> Dictionary:
	if not _entities.has(entity_id):
		return {}
	return _entities[entity_id].duplicate(true)


func get_entities() -> Dictionary:
	return _entities.duplicate(true)


func has_action(action_id: String) -> bool:
	return _actions.has(action_id)


func get_snapshot() -> Dictionary:
	return _snapshot(_entities, _actions, revision)


func apply_validated_edit(command: Dictionary) -> Dictionary:
	if world_id.is_empty() or base_map.is_empty():
		return _failure("world_not_initialized")
	if not _json_safe(command) or JSON.stringify(command, "", true).to_utf8_buffer().size() > MAX_COMMAND_BYTES:
		return _failure("invalid_command_data")
	var action_id: Variant = command.get("action_id")
	var expected_revision: Variant = command.get("expected_revision")
	var operation: Variant = command.get("op")
	if typeof(action_id) != TYPE_STRING or not _valid_token(action_id):
		return _failure("invalid_action_id")
	if typeof(expected_revision) != TYPE_INT or expected_revision < 0:
		return _failure("invalid_expected_revision")
	if typeof(operation) != TYPE_STRING or operation not in ["place", "update", "remove"]:
		return _failure("unsupported_operation")
	var fingerprint := JSON.stringify(command, "", true).sha256_text()
	if _actions.has(action_id):
		var prior: Dictionary = _actions[action_id]
		if prior.get("fingerprint") != fingerprint:
			return _failure("action_id_conflict")
		var repeated: Dictionary = prior["receipt"].duplicate(true)
		repeated["replayed"] = true
		return repeated
	if expected_revision != revision:
		return _failure("revision_conflict")
	if _actions.size() >= MAX_ACTIONS:
		return _failure("action_limit")

	var next_entities := _entities.duplicate(true)
	var entity_id := ""
	if operation == "place":
		if next_entities.size() >= MAX_ENTITIES:
			return _failure("entity_limit")
		if not command.get("entity") is Dictionary:
			return _failure("invalid_entity")
		var new_entity: Dictionary = command["entity"].duplicate(true)
		if new_entity.has("id") or new_entity.is_empty():
			return _failure("invalid_entity")
		entity_id = "edit:" + action_id
		if next_entities.has(entity_id):
			return _failure("entity_id_conflict")
		new_entity["id"] = entity_id
		next_entities[entity_id] = new_entity
	else:
		var requested_id: Variant = command.get("entity_id")
		if typeof(requested_id) != TYPE_STRING or not requested_id.begins_with("edit:") or not next_entities.has(requested_id):
			return _failure("entity_not_found")
		entity_id = requested_id
		if operation == "update":
			if not command.get("entity") is Dictionary:
				return _failure("invalid_entity")
			var replacement: Dictionary = command["entity"].duplicate(true)
			if replacement.is_empty() or (replacement.has("id") and replacement["id"] != entity_id):
				return _failure("invalid_entity")
			replacement["id"] = entity_id
			next_entities[entity_id] = replacement
		else:
			next_entities.erase(entity_id)

	var receipt := {"ok": true, "revision": revision + 1, "entity_id": entity_id, "replayed": false}
	var next_actions := _actions.duplicate(true)
	next_actions[action_id] = {"fingerprint": fingerprint, "receipt": receipt.duplicate(true)}
	if not _json_safe(next_entities) or JSON.stringify(_snapshot(next_entities, next_actions, revision + 1), "", true).to_utf8_buffer().size() > MAX_SAVE_BYTES:
		return _failure("state_limit")
	_entities = next_entities
	_actions = next_actions
	revision += 1
	return receipt


func save_to_path(path: String) -> bool:
	last_error = ""
	if world_id.is_empty() or not _valid_save_path(path):
		last_error = "invalid_save_path_or_world"
		return false
	var serialized := JSON.stringify(get_snapshot(), "\t", true)
	if serialized.to_utf8_buffer().size() > MAX_SAVE_BYTES:
		last_error = "state_limit"
		return false
	var absolute := ProjectSettings.globalize_path(path)
	var parent := absolute.get_base_dir()
	if DirAccess.make_dir_recursive_absolute(parent) != OK:
		last_error = "save_directory_failed"
		return false
	var temporary := absolute + ".tmp"
	var file := FileAccess.open(temporary, FileAccess.WRITE)
	if file == null:
		last_error = "save_open_failed"
		return false
	file.store_string(serialized)
	file.flush()
	file.close()
	# Replace only after the complete new snapshot has been flushed. A failed rename
	# leaves the previous save intact; the temporary file can be discarded later.
	if DirAccess.rename_absolute(temporary, absolute) != OK:
		last_error = "save_replace_failed"
		return false
	return true


func load_from_path(path: String, expected_manifest: Dictionary, expected_world_id := "", expected_frame_id := "") -> bool:
	last_error = ""
	if not _valid_save_path(path):
		last_error = "invalid_save_path"
		return false
	var expected_base := _pin_base(expected_manifest)
	if expected_base.is_empty():
		last_error = "invalid_expected_base"
		return false
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		last_error = "save_open_failed"
		return false
	if file.get_length() > MAX_SAVE_BYTES:
		last_error = "state_limit"
		return false
	var json := JSON.new()
	if json.parse(file.get_as_text()) != OK or not json.data is Dictionary:
		last_error = "invalid_save_json"
		return false
	var snapshot: Dictionary = json.data
	if snapshot.get("schema") != SCHEMA or _json_integer(snapshot.get("version")) != SCHEMA_VERSION:
		last_error = "unsupported_save_version"
		return false
	var loaded_world: Variant = snapshot.get("world_id")
	var loaded_frame: Variant = snapshot.get("frame_id")
	if typeof(loaded_world) != TYPE_STRING or typeof(loaded_frame) != TYPE_STRING or not _valid_token(loaded_world) or not _valid_token(loaded_frame):
		last_error = "invalid_world_identity"
		return false
	if (not expected_world_id.is_empty() and loaded_world != expected_world_id) or (not expected_frame_id.is_empty() and loaded_frame != expected_frame_id):
		last_error = "world_identity_mismatch"
		return false
	if snapshot.get("base_map") != expected_base:
		last_error = "base_map_mismatch"
		return false
	var loaded_revision := _json_integer(snapshot.get("revision"))
	var loaded_entities: Variant = snapshot.get("entities")
	var loaded_actions: Variant = snapshot.get("actions")
	if loaded_revision < 0 or not loaded_entities is Dictionary or not loaded_actions is Dictionary:
		last_error = "invalid_save_structure"
		return false
	if loaded_entities.size() > MAX_ENTITIES or loaded_actions.size() > MAX_ACTIONS or loaded_actions.size() != loaded_revision:
		last_error = "invalid_save_counts"
		return false
	for id in loaded_entities:
		if typeof(id) != TYPE_STRING or not id.begins_with("edit:") or not _valid_token(id.substr(5)):
			last_error = "invalid_entity_id"
			return false
		var entity: Variant = loaded_entities[id]
		if not entity is Dictionary or entity.get("id") != id or not _json_safe(entity):
			last_error = "invalid_entity"
			return false
	for id in loaded_actions:
		var action: Variant = loaded_actions[id]
		if typeof(id) != TYPE_STRING or not _valid_token(id) or not action is Dictionary:
			last_error = "invalid_action"
			return false
		if not _valid_sha256(action.get("fingerprint")) or not action.get("receipt") is Dictionary:
			last_error = "invalid_action"
			return false
		var old_receipt: Dictionary = action["receipt"]
		var receipt_revision := _json_integer(old_receipt.get("revision"))
		if old_receipt.get("ok") != true or receipt_revision < 1 or receipt_revision > loaded_revision:
			last_error = "invalid_receipt"
			return false
		old_receipt["revision"] = receipt_revision
	# No live state changes until every identity, hash, and structure check succeeds.
	world_id = loaded_world
	frame_id = loaded_frame
	base_map = expected_base
	revision = loaded_revision
	_entities = loaded_entities.duplicate(true)
	_actions = loaded_actions.duplicate(true)
	return true


func _snapshot(entities: Dictionary, actions: Dictionary, current_revision: int) -> Dictionary:
	return {
		"schema": SCHEMA,
		"version": SCHEMA_VERSION,
		"world_id": world_id,
		"frame_id": frame_id,
		"base_map": base_map.duplicate(true),
		"revision": current_revision,
		"entities": entities.duplicate(true),
		"actions": actions.duplicate(true),
	}


func _pin_base(manifest: Dictionary) -> Dictionary:
	var map_id: Variant = manifest.get("map_id")
	var features_hash: Variant = manifest.get("features_sha256")
	var heights_hash: Variant = manifest.get("heights_sha256")
	if typeof(map_id) != TYPE_STRING or not _valid_token(map_id) or not _valid_sha256(features_hash) or not _valid_sha256(heights_hash):
		return {}
	# Identical terrain/feature bytes can land in a different place when the CRS,
	# local origin, axis convention, dimensions, or height decoder changes. Pin
	# those fields independently of incidental manifest text such as credits.
	var spatial_keys := [
		"format", "crs", "axes", "center_lat", "center_lon",
		"center_projected_m", "side_m", "grid_side", "sample_spacing_m",
		"tile_side_m", "height_encoding", "height_offset_m",
		"height_scale_m", "height_origin_m", "height_min_m", "height_max_m",
	]
	var spatial := {}
	for key in spatial_keys:
		if not manifest.has(key):
			return {}
		spatial[key] = manifest[key]
	if not _valid_spatial_metadata(spatial):
		return {}
	var spatial_hash := JSON.stringify(spatial, "", true, true).sha256_text()
	return {
		"map_id": map_id,
		"features_sha256": features_hash,
		"heights_sha256": heights_hash,
		"spatial_manifest_sha256": spatial_hash,
	}


func _valid_spatial_metadata(spatial: Dictionary) -> bool:
	for key in ["format", "crs", "axes", "height_encoding"]:
		if typeof(spatial[key]) != TYPE_STRING or String(spatial[key]).is_empty():
			return false
	for key in ["center_lat", "center_lon", "height_offset_m", "height_scale_m", "height_origin_m", "height_min_m", "height_max_m"]:
		if not _finite_number(spatial[key]):
			return false
	for key in ["side_m", "grid_side", "sample_spacing_m", "tile_side_m"]:
		if not _finite_number(spatial[key]) or float(spatial[key]) <= 0.0 or float(spatial[key]) != floor(float(spatial[key])):
			return false
	var projected: Variant = spatial["center_projected_m"]
	return projected is Array and projected.size() == 2 and _finite_number(projected[0]) and _finite_number(projected[1])


func _finite_number(value: Variant) -> bool:
	if typeof(value) != TYPE_FLOAT and typeof(value) != TYPE_INT:
		return false
	return not is_nan(float(value)) and not is_inf(float(value))


func _valid_sha256(value: Variant) -> bool:
	if typeof(value) != TYPE_STRING or value.length() != 64:
		return false
	for index in range(value.length()):
		if not "0123456789abcdef".contains(value[index]):
			return false
	return true


func _json_integer(value: Variant) -> int:
	if typeof(value) == TYPE_INT:
		return value
	if typeof(value) == TYPE_FLOAT and not is_nan(value) and not is_inf(value) and value >= 0.0 and value <= float(MAX_ACTIONS) and value == floor(value):
		return int(value)
	return -1


func _valid_token(value: String) -> bool:
	if value.is_empty() or value.length() > 100:
		return false
	for index in range(value.length()):
		if not "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._:-".contains(value[index]):
			return false
	return true


func _valid_save_path(path: String) -> bool:
	if not path.begins_with("user://") or path.contains("\\") or path.contains(".."):
		return false
	var relative := path.trim_prefix("user://")
	if relative.is_empty() or relative.begins_with("/") or relative.ends_with("/"):
		return false
	for index in range(relative.length()):
		if not "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-/".contains(relative[index]):
			return false
	return true


func _json_safe(value: Variant, depth := 0) -> bool:
	if depth > 16:
		return false
	match typeof(value):
		TYPE_NIL, TYPE_BOOL, TYPE_INT:
			return true
		TYPE_FLOAT:
			return not is_nan(value) and not is_inf(value)
		TYPE_STRING:
			return value.length() <= 1024
		TYPE_ARRAY:
			if value.size() > 256:
				return false
			for item in value:
				if not _json_safe(item, depth + 1):
					return false
			return true
		TYPE_DICTIONARY:
			if value.size() > 256:
				return false
			for key in value:
				if typeof(key) != TYPE_STRING or key.length() > 128 or not _json_safe(value[key], depth + 1):
					return false
			return true
	return false


func _failure(code: String) -> Dictionary:
	return {"ok": false, "error": code, "revision": revision}
