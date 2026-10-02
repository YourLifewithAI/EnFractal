extends RefCounted
## Host-owned local command boundary. The principal argument must come from a trusted
## adapter; request fields cannot grant identity, permissions, costs or ownership.

const COMPILER = preload("res://scripts/creation_compiler.gd")
const SCHEMA := "enfractal.creation-world"
const VERSION := 1
const STYLE_VERSION := "barton_painterly_v1"
const MAX_SAVE_BYTES := 4 * 1024 * 1024
const MAX_RECEIPTS := 2048
const MAX_REQUEST_BYTES := 65536
const PLOT_MIN := Vector2(-60.0, 290.0)
const PLOT_MAX := Vector2(60.0, 410.0)
const PROTECTED_MIN := Vector2(-15.0, 380.0)
const PROTECTED_MAX := Vector2(15.0, 410.0)
const OWNER_LIMITS := {"instances": 8, "parts": 96, "nodes": 64, "edges": 128, "fields": 16, "lights": 16, "rotors": 16}
const WORLD_LIMITS := {"instances": 16, "parts": 192, "nodes": 128, "edges": 256, "fields": 32, "lights": 32, "rotors": 32}

var revision := 0
var permission_revision := 0
var last_error := ""
# Optional trusted host seam, bound by the playable runtime. It receives the
# compiled artifact, host position, yaw and replaced ID before ground publication.
var occupancy_query := Callable()
var activation_query := Callable()
# A trusted host adapter may replace the local file with a durable transaction.
# The sink must acknowledge commit before returning ok; errors retain old memory.
var persistence_sink := Callable()
var access_guard := Callable()
var _roles := {"local_player": "owner", "guest_player": "visitor"}
var _consent := {"local_player": false, "guest_player": false}
var _instances: Dictionary = {}
var _receipts: Dictionary = {}
var _activation_receipts: Dictionary = {}
var _terrain := Callable()
var _base_pin := ""
var _save_path := ""
var _next_id := 1
var _configured := false
var _load_required := false
var _runtime_events: Array = []
var _last_budget_time := -1.0


func configure(manifest: Dictionary, terrain_height: Callable, save_path: String) -> Dictionary:
	_configured = false
	_instances.clear()
	_receipts.clear()
	_activation_receipts.clear()
	_runtime_events.clear()
	_last_budget_time = -1.0
	revision = 0
	permission_revision = 0
	_next_id = 1
	_roles = {"local_player": "owner", "guest_player": "visitor"}
	_consent = {"local_player": false, "guest_player": false}
	if manifest.is_empty() or not _json_safe(manifest) or not terrain_height.is_valid():
		return _failure("configuration_invalid", "manifest", "A pinned map and terrain sampler are required.")
	if not save_path.begins_with("user://") or ".." in save_path or save_path.ends_with("/"):
		return _failure("save_path_invalid", "save_path", "Creation saves must be inside the application user folder.")
	_terrain = terrain_height
	_base_pin = COMPILER.canonical_json(manifest).sha256_text()
	_save_path = save_path
	_load_required = FileAccess.file_exists(_save_path)
	_configured = true
	return {"ok": true, "load_required": _load_required}


func submit(principal: String, request: Dictionary) -> Dictionary:
	if not _available():
		return _failure("save_not_ready", "", "Load the saved workshop successfully before changing it.")
	if not _roles.has(principal):
		return _failure("principal_unknown", "principal", "The host did not admit this player.")
	if not _json_safe(request) or JSON.stringify(request).to_utf8_buffer().size() > MAX_REQUEST_BYTES:
		return _failure("request_invalid", "", "The command must contain bounded finite JSON data.")
	var action: Variant = request.get("action_id")
	if not _token(action):
		return _failure("action_id_invalid", "action_id", "Use a unique action ID of at most 64 letters, digits, underscores or hyphens.")
	var key := principal + "|" + String(action)
	var fingerprint := COMPILER.canonical_json(request).sha256_text()
	# Identity is checked above, but committed retries precede current permissions
	# and revision checks. The key is principal-bound, never globally action-bound.
	for ledger in [_receipts, _activation_receipts]:
		if ledger.has(key):
			var prior: Dictionary = ledger[key]
			if prior.fingerprint != fingerprint:
				return _failure("action_id_conflict", "action_id", "This action ID was already used for different content.")
			var receipt: Dictionary = prior.receipt.duplicate(true)
			receipt.replayed = true
			return receipt
	var op: Variant = request.get("op")
	if op not in ["place", "revise", "remove", "activate"]:
		return _failure("operation_invalid", "op", "Choose place, revise, remove or activate.")
	var allowed := ["op", "action_id", "expected_revision", "expected_permission_revision"]
	if op in ["place", "revise"]:
		allowed.append_array(["source", "x_m", "z_m", "yaw_deg"])
	if op != "place":
		allowed.append("instance_id")
	for field in request:
		if field not in allowed:
			return _failure("field_unknown", String(field), "Identity, ownership, height and compiled costs are assigned by the host.")
	if not _whole(request.get("expected_revision")) or int(request.expected_revision) != revision:
		return _failure("revision_conflict", "expected_revision", "The workshop changed. Refresh before confirming this edit.")
	if not _whole(request.get("expected_permission_revision")) or int(request.expected_permission_revision) != permission_revision:
		return _failure("permission_revision_conflict", "expected_permission_revision", "Permissions changed. Refresh before confirming this edit.")
	var instance_id := ""
	if op != "place":
		if not request.get("instance_id") is String or not _instances.has(request.instance_id):
			return _failure("instance_not_found", "instance_id", "That invention no longer exists.")
		instance_id = request.instance_id
	if op == "activate":
		if not can_activate(principal, instance_id):
			return _failure("activation_denied", "instance_id", "Consent and an active permitted invention are required.")
		if activation_query.is_valid():
			var context: Variant = activation_query.call(principal, _instances[instance_id].duplicate(true))
			if not context is Dictionary or not context.get("ok", false):
				return _failure("activation_context", "instance_id", str(context.get("message", "This invention cannot be used from here.")) if context is Dictionary else "The host could not validate this activation.")
		if _activation_receipts.size() >= MAX_RECEIPTS:
			return _failure("activation_receipt_limit", "action_id", "Restart the local session to clear transient activation receipts.")
		var transient := {"ok": true, "instance_id": instance_id, "revision": revision, "permission_revision": permission_revision, "replayed": false, "transient": true}
		_activation_receipts[key] = {"fingerprint": fingerprint, "receipt": transient.duplicate(true)}
		return transient
	var next_instances := _instances.duplicate(true)
	var next_id := _next_id
	if op in ["place", "revise"]:
		var prepared := _prepare_candidate(principal, request.get("source"), request.get("x_m"), request.get("z_m"), request.get("yaw_deg"), instance_id)
		if not prepared.ok:
			return prepared
		next_instances = prepared.instances
		next_id = int(prepared.next_id)
		instance_id = prepared.instance_id
	else:
		if not _can_build(principal):
			return _failure("build_denied", "principal", "Only an owner or editor may change inventions.")
		if _instances[instance_id].owner_id != principal and _roles[principal] != "owner":
			return _failure("ownership_denied", "instance_id", "Only its creator or the plot owner can remove this invention.")
		if _receipts.size() >= MAX_RECEIPTS:
			return _failure("receipt_limit", "action_id", "The local creation receipt limit has been reached.")
		next_instances.erase(instance_id)
	var receipt := {"ok": true, "instance_id": instance_id, "revision": revision + 1, "permission_revision": permission_revision, "replayed": false}
	var next_receipts := _receipts.duplicate(true)
	next_receipts[key] = {"principal": principal, "action_id": action, "fingerprint": fingerprint, "receipt": receipt.duplicate(true)}
	var persisted := _persist(next_instances, next_receipts, revision + 1, permission_revision, _roles, _consent, next_id)
	if not persisted.ok:
		return persisted
	_instances = next_instances
	_receipts = next_receipts
	_next_id = next_id
	revision += 1
	return receipt


func preflight(principal: String, source: Dictionary, x_m: Variant, z_m: Variant, yaw_deg: Variant, instance_id: String = "") -> Dictionary:
	# A read-only diagnostic of the same candidate path used by commit. It never
	# reserves an ID, capacity or receipt, changes revisions, or touches the save.
	var previous_error := last_error
	var candidate := _prepare_candidate(principal, source, x_m, z_m, yaw_deg, instance_id)
	last_error = previous_error
	if not candidate.ok:
		return candidate
	return {"ok": true, "artifact": candidate.artifact, "position_m": candidate.position_m, "revision": revision, "permission_revision": permission_revision}


func _prepare_candidate(principal: String, source: Variant, x_m: Variant, z_m: Variant, yaw_deg: Variant, instance_id: String) -> Dictionary:
	if not _available():
		return _failure("save_not_ready", "", "Load the saved workshop successfully before changing it.")
	if not _roles.has(principal):
		return _failure("principal_unknown", "principal", "The host did not admit this player.")
	if not _can_build(principal):
		return _failure("build_denied", "principal", "Only an owner or editor may change inventions.")
	if not instance_id.is_empty():
		if not _instances.has(instance_id):
			return _failure("instance_not_found", "instance_id", "That invention no longer exists.")
		if _instances[instance_id].owner_id != principal:
			return _failure("ownership_denied", "instance_id", "You can revise only your own invention.")
	if _receipts.size() >= MAX_RECEIPTS:
		return _failure("receipt_limit", "action_id", "The local creation receipt limit has been reached.")
	var compiled: Dictionary = COMPILER.compile(source)
	if not compiled.ok:
		return compiled
	var placement := _validate_placement(compiled.artifact, x_m, z_m, yaw_deg)
	if not placement.ok:
		return placement
	if compiled.artifact.source.mount == "ground" and occupancy_query.is_valid():
		var position := Vector3(float(placement.position_m[0]), float(placement.position_m[1]), float(placement.position_m[2]))
		var occupancy: Variant = occupancy_query.call(compiled.artifact.duplicate(true), position, float(yaw_deg), instance_id)
		if not occupancy is Dictionary or not occupancy.has("ok") or not occupancy.ok is bool:
			return _failure("occupancy_unavailable", "position", "The host could not verify live avatar clearance.")
		if not occupancy.ok:
			return _failure(String(occupancy.get("code", "avatar_occupied")), String(occupancy.get("path", "position")), String(occupancy.get("message", "Move the invention away from live avatars before confirming.")))
	var next_instances := _instances.duplicate(true)
	var next_id := _next_id
	var candidate_id := instance_id
	if candidate_id.is_empty():
		candidate_id = "creation:%08d" % next_id
		next_id += 1
	var instance_revision := 1 if instance_id.is_empty() else int(_instances[instance_id].revision) + 1
	next_instances[candidate_id] = {"id": candidate_id, "owner_id": principal, "revision": instance_revision, "source": compiled.artifact.source.duplicate(true), "artifact": compiled.artifact.duplicate(true), "position_m": placement.position_m, "yaw_deg": float(yaw_deg), "active": true}
	var capacity := _validate_capacity(next_instances)
	if not capacity.ok:
		return capacity
	return {"ok": true, "instances": next_instances, "next_id": next_id, "instance_id": candidate_id, "artifact": compiled.artifact, "position_m": placement.position_m}


func snapshot(principal: String) -> Dictionary:
	if not _available():
		return _failure("save_not_ready", "", "The workshop save is not available.")
	if not _roles.has(principal):
		return _failure("principal_unknown", "principal", "The host did not admit this player.")
	var readable: Dictionary = {}
	for instance in _instances.values():
		var item: Dictionary = instance.duplicate(true)
		item.active = _can_build(item.owner_id)
		readable[item.id] = item
	return {"ok": true, "revision": revision, "permission_revision": permission_revision, "instances": readable, "roles": _roles.duplicate(), "consent": _consent.duplicate(), "principal_role": _roles[principal], "capacity": _capacity(_instances), "limits": {"owner": OWNER_LIMITS.duplicate(), "world": WORLD_LIMITS.duplicate()}, "base_pin": _base_pin}


func is_ready() -> bool:
	return _available()


func set_role(operator: String, target: String, role: String) -> Dictionary:
	if not _available():
		return _failure("save_not_ready", "", "The workshop save is not available.")
	if _roles.get(operator) != "owner":
		return _failure("role_denied", "operator", "Only the plot owner can change roles.")
	if not _roles.has(target) or role not in ["owner", "editor", "visitor"] or (target == "local_player" and role != "owner") or (target != "local_player" and role == "owner"):
		return _failure("role_invalid", "role", "The local plot has one fixed owner; guests can be editors or visitors.")
	if _roles[target] == role:
		return {"ok": true, "revision": revision, "permission_revision": permission_revision, "changed": false}
	var next_roles := _roles.duplicate()
	next_roles[target] = role
	return _commit_permissions(next_roles, _consent.duplicate())


func set_consent(principal: String, enabled: bool) -> Dictionary:
	if not _available() or not _roles.has(principal):
		return _failure("consent_denied", "principal", "Only an admitted player can set their own consent.")
	if _consent[principal] == enabled:
		return {"ok": true, "revision": revision, "permission_revision": permission_revision, "changed": false}
	var next_consent := _consent.duplicate()
	next_consent[principal] = enabled
	return _commit_permissions(_roles.duplicate(), next_consent)


func can_activate(principal: String, instance_id: String) -> bool:
	return _available() and _roles.has(principal) and bool(_consent.get(principal, false)) and _instances.has(instance_id) and _can_build(_instances[instance_id].owner_id)


func can_affect(owner: String, target: String, position: Vector3) -> bool:
	if not _available() or not _can_build(owner) or not _roles.has(target) or not bool(_consent.get(target, false)) or not position.is_finite():
		return false
	return _point_authorized(Vector2(position.x, position.z))


func consume_runtime_budget(instance_id: String, principal: String, fields: int, node_evaluations: int, now_seconds: float) -> Dictionary:
	if not can_activate(principal, instance_id):
		return _failure("activation_denied", "instance_id", "Consent or permissions were revoked before activation.")
	if fields < 0 or fields > 16 or node_evaluations < 1 or node_evaluations > 16 or not is_finite(now_seconds) or now_seconds < 0.0 or now_seconds < _last_budget_time:
		return _failure("runtime_cost_invalid", "runtime", "Runtime costs and monotonic host time must be bounded.")
	_last_budget_time = now_seconds
	var recent: Array = []
	for entry in _runtime_events:
		if float(entry.time) > now_seconds - 1.0:
			recent.append(entry)
	_runtime_events = recent
	var owner: String = _instances[instance_id].owner_id
	var actors := [owner]
	if principal != owner:
		actors.append(principal)
	var world_fields := fields
	var world_nodes := node_evaluations
	var instance_count := 1
	var instance_nodes := node_evaluations
	for entry in recent:
		world_fields += int(entry.fields)
		world_nodes += int(entry.nodes)
		if entry.instance_id == instance_id:
			instance_count += 1
			instance_nodes += int(entry.nodes)
	if recent.size() + 1 > 20 or world_fields > 16 or world_nodes > 320 or instance_count > 5 or instance_nodes > 80:
		return _failure("runtime_budget", "runtime", "The invention or workshop activation budget is busy. Try again shortly.")
	for actor in actors:
		var actor_count := 1
		var actor_fields := fields
		var actor_nodes := node_evaluations
		for entry in recent:
			if actor in entry.actors:
				actor_count += 1
				actor_fields += int(entry.fields)
				actor_nodes += int(entry.nodes)
		if actor_count > 10 or actor_fields > 8 or actor_nodes > 160:
			return _failure("runtime_budget", "runtime", "This player's activation budget is busy. Try again shortly.")
	_runtime_events.append({"time": now_seconds, "instance_id": instance_id, "actors": actors, "fields": fields, "nodes": node_evaluations})
	return {"ok": true, "revision": revision, "permission_revision": permission_revision}


func load_saved() -> Dictionary:
	if not _configured:
		return _failure("configuration_invalid", "", "Configure a map before loading inventions.")
	if not FileAccess.file_exists(_save_path):
		if _load_required:
			return _failure("save_missing", "save", "The previously detected save disappeared; it has not been overwritten.")
		return {"ok": true, "loaded": false, "revision": revision, "permission_revision": permission_revision}
	_load_required = true
	var file := FileAccess.open(_save_path, FileAccess.READ)
	if file == null or file.get_length() > MAX_SAVE_BYTES:
		return _failure("save_invalid", "save", "The save is unreadable or exceeds the local size limit.")
	var parser := JSON.new()
	var text := file.get_as_text()
	file.close()
	if parser.parse(text) != OK or not parser.data is Dictionary or not _json_safe(parser.data):
		return _failure("save_invalid", "save", "The save contains invalid JSON; it has not been overwritten.")
	return load_envelope(parser.data)


func load_envelope(data: Dictionary) -> Dictionary:
	if not _configured:
		return _failure("configuration_invalid", "", "Configure a map before loading inventions.")
	_load_required = true
	var checked := _validate_saved(data)
	if not checked.ok:
		return checked
	_instances = checked.instances
	_receipts = data.receipts.duplicate(true)
	_roles = data.roles.duplicate()
	# Consent is a session decision. Inventions are readable on reload, but no
	# saved opt-in can silently cause forces on a returning player.
	_consent = {"local_player": false, "guest_player": false}
	revision = int(data.revision)
	permission_revision = int(data.permission_revision) + 1
	_next_id = int(data.next_id)
	_activation_receipts.clear()
	_runtime_events.clear()
	_last_budget_time = -1.0
	_load_required = false
	return {"ok": true, "loaded": true, "revision": revision, "permission_revision": permission_revision}


func export_envelope() -> Dictionary:
	return _envelope(_instances, _receipts, revision, permission_revision, _roles, _consent, _next_id)


func _commit_permissions(roles: Dictionary, consent: Dictionary) -> Dictionary:
	var result := _persist(_instances, _receipts, revision + 1, permission_revision + 1, roles, consent, _next_id)
	if not result.ok:
		return result
	_roles = roles
	_consent = consent
	revision += 1
	permission_revision += 1
	return {"ok": true, "revision": revision, "permission_revision": permission_revision, "changed": true}


func _validate_placement(artifact: Dictionary, x: Variant, z: Variant, yaw: Variant) -> Dictionary:
	if not _number(x) or not _number(z) or not _number(yaw) or abs(float(yaw)) > 180.0:
		return _failure("placement_invalid", "position", "Use finite placement coordinates and yaw between -180 and 180 degrees.")
	# Check scalar doubles before converting to single-precision Vector3 values;
	# huge finite JSON numbers must never overflow into NaN footprint comparisons.
	if float(x) < PLOT_MIN.x or float(x) > PLOT_MAX.x or float(z) < PLOT_MIN.y or float(z) > PLOT_MAX.y:
		return _failure("plot_bounds", "position", "The placement origin must be inside the workshop plot.")
	var angle := deg_to_rad(float(yaw))
	var basis := Basis(Vector3.UP, angle)
	var low: Array = artifact.bounds.min
	var high: Array = artifact.bounds.max
	var minimum := Vector2(INF, INF)
	var maximum := Vector2(-INF, -INF)
	for bx in [float(low[0]), float(high[0])]:
		for bz in [float(low[2]), float(high[2])]:
			var point := basis * Vector3(bx, 0.0, bz) + Vector3(float(x), 0.0, float(z))
			minimum = minimum.min(Vector2(point.x, point.z))
			maximum = maximum.max(Vector2(point.x, point.z))
	var footprint := _rectangle_authorized(minimum, maximum)
	if not footprint.ok:
		return footprint
	# Field centers are attached to their part. The whole radius, including
	# proximity sensors, must stay inside editable and effect-authorized space.
	var parts: Dictionary = {}
	for part in artifact.source.parts:
		parts[part.id] = part
	for node in artifact.source.nodes:
		if node.op in ["wind", "proximity"]:
			var p: Array = parts[node.part_id].position_m
			var center3 := basis * Vector3(float(p[0]), float(p[1]), float(p[2]))
			var center := Vector2(float(x) + center3.x, float(z) + center3.z)
			var radius := float(node.params.radius_m)
			var field := _rectangle_authorized(center - Vector2.ONE * radius, center + Vector2.ONE * radius)
			if not field.ok:
				field.path = "nodes." + String(node.id) + ".params.radius_m"
				return field
	var heights: Array[float] = []
	for sample in [minimum, maximum, Vector2(minimum.x, maximum.y), Vector2(maximum.x, minimum.y), Vector2(float(x), float(z))]:
		var height: Variant = _terrain.call(sample.x, sample.y)
		if not _number(height) or abs(float(height)) > 10000.0:
			return _failure("terrain_unavailable", "position", "This footprint needs valid loaded terrain before placement.")
		heights.append(float(height))
	var highest: float = heights.max()
	var lowest: float = heights.min()
	if highest - lowest > 3.0:
		return _failure("terrain_too_uneven", "position", "Choose a footprint with less than three metres of terrain height difference.")
	return {"ok": true, "position_m": [float(x), highest, float(z)]}


func _rectangle_authorized(minimum: Vector2, maximum: Vector2) -> Dictionary:
	if minimum.x < PLOT_MIN.x or minimum.y < PLOT_MIN.y or maximum.x > PLOT_MAX.x or maximum.y > PLOT_MAX.y:
		return _failure("plot_bounds", "position", "The whole invention and its fields must fit inside the workshop plot.")
	if maximum.x >= PROTECTED_MIN.x and minimum.x <= PROTECTED_MAX.x and maximum.y >= PROTECTED_MIN.y and minimum.y <= PROTECTED_MAX.y:
		return _failure("protected_zone", "position", "The public garden is protected from inventions and their effects.")
	return {"ok": true}


func _point_authorized(point: Vector2) -> bool:
	return point.x >= PLOT_MIN.x and point.x <= PLOT_MAX.x and point.y >= PLOT_MIN.y and point.y <= PLOT_MAX.y and not (point.x >= PROTECTED_MIN.x and point.x <= PROTECTED_MAX.x and point.y >= PROTECTED_MIN.y and point.y <= PROTECTED_MAX.y)


func _capacity(instances: Dictionary) -> Dictionary:
	var world := {"instances": 0, "parts": 0, "nodes": 0, "edges": 0, "fields": 0, "lights": 0, "rotors": 0}
	var owners := {"local_player": world.duplicate(), "guest_player": world.duplicate()}
	for instance in instances.values():
		world.instances += 1
		owners[instance.owner_id].instances += 1
		for key in instance.artifact.cost:
			world[key] += int(instance.artifact.cost[key])
			owners[instance.owner_id][key] += int(instance.artifact.cost[key])
	return {"world": world, "owners": owners}


func _validate_capacity(instances: Dictionary) -> Dictionary:
	var counts := _capacity(instances)
	var mounted := {"local_player": 0, "guest_player": 0}
	for instance in instances.values():
		if instance.source.mount == "avatar":
			mounted[instance.owner_id] += 1
			if mounted[instance.owner_id] > 1:
				return _failure("avatar_slot_full", "mount", "Each player can equip one invention. Revise or remove their existing avatar invention first.")
	for key in WORLD_LIMITS:
		if int(counts.world[key]) > int(WORLD_LIMITS[key]):
			return _failure("world_capacity", "cost." + key, "The workshop's aggregate " + key + " limit would be exceeded.")
	for owner in counts.owners:
		for key in OWNER_LIMITS:
			if int(counts.owners[owner][key]) > int(OWNER_LIMITS[key]):
				return _failure("owner_capacity", "cost." + key, "This player's aggregate " + key + " limit would be exceeded.")
	return {"ok": true}


func _envelope(instances: Dictionary, receipts: Dictionary, world_revision: int, permissions: int, roles: Dictionary, consent: Dictionary, next_id: int) -> Dictionary:
	var stored_instances: Array = []
	for instance in instances.values():
		var stored: Dictionary = instance.duplicate(true)
		stored.erase("artifact")
		stored.erase("active")
		stored_instances.append(stored)
	return {"schema": SCHEMA, "version": VERSION, "compiler_version": 1, "style_version": STYLE_VERSION, "base_pin": _base_pin, "revision": world_revision, "permission_revision": permissions, "next_id": next_id, "roles": roles.duplicate(true), "consent": consent.duplicate(true), "instances": stored_instances, "receipts": receipts.duplicate(true)}


func _persist(instances: Dictionary, receipts: Dictionary, world_revision: int, permissions: int, roles: Dictionary, consent: Dictionary, next_id: int) -> Dictionary:
	var envelope := _envelope(instances, receipts, world_revision, permissions, roles, consent, next_id)
	var serialized := COMPILER.canonical_json(envelope)
	if serialized.to_utf8_buffer().size() > MAX_SAVE_BYTES:
		return _failure("save_size", "save", "The creation save exceeds the local size limit.")
	if persistence_sink.is_valid():
		var result: Variant = persistence_sink.call(envelope)
		if not result is Dictionary or not result.get("ok", false):
			return _failure("durable_save_pending", "save", str(result.get("message", "The durable save has not been confirmed. Reconnect before editing.")) if result is Dictionary else "The durable save has not been confirmed. Reconnect before editing.")
		return {"ok": true}
	var absolute := ProjectSettings.globalize_path(_save_path)
	if DirAccess.make_dir_recursive_absolute(absolute.get_base_dir()) != OK:
		return _failure("save_directory", "save", "The save folder could not be created.")
	var temporary := absolute + ".pending"
	var file := FileAccess.open(temporary, FileAccess.WRITE)
	if file == null:
		return _failure("save_write", "save", "The pending save could not be opened.")
	file.store_string(serialized)
	file.flush()
	var write_error := file.get_error()
	file.close()
	if write_error != OK:
		return _failure("save_write", "save", "The pending save could not be written completely.")
	# The committed file is replaced only after a complete flushed write. Failure
	# never publishes the candidate in memory and never removes the prior file.
	if DirAccess.rename_absolute(temporary, absolute) != OK:
		return _failure("save_replace", "save", "The previous save could not be replaced safely.")
	return {"ok": true}


func _validate_saved(data: Dictionary) -> Dictionary:
	var expected := ["schema", "version", "compiler_version", "style_version", "base_pin", "revision", "permission_revision", "next_id", "roles", "consent", "instances", "receipts"]
	if not _exact_keys(data, expected) or data.get("schema") != SCHEMA or data.get("version") != VERSION or data.get("compiler_version") != 1 or data.get("style_version") != STYLE_VERSION or data.get("base_pin") != _base_pin:
		return _failure("save_incompatible", "save", "The save's map, compiler or style version does not match this workshop.")
	if not _whole(data.revision) or int(data.revision) < 0 or not _whole(data.permission_revision) or int(data.permission_revision) < 0 or not _whole(data.next_id) or int(data.next_id) < 1:
		return _failure("save_invalid", "revision", "Saved revisions or identity counters are invalid.")
	if not data.roles is Dictionary or not _exact_keys(data.roles, ["local_player", "guest_player"]) or data.roles.local_player != "owner" or data.roles.guest_player not in ["editor", "visitor"]:
		return _failure("save_invalid", "roles", "Saved roles are invalid.")
	if not data.consent is Dictionary or not _exact_keys(data.consent, ["local_player", "guest_player"]) or not data.consent.local_player is bool or not data.consent.guest_player is bool:
		return _failure("save_invalid", "consent", "Saved consent is invalid.")
	if not data.instances is Array or data.instances.size() > 16 or not data.receipts is Dictionary or data.receipts.size() > MAX_RECEIPTS:
		return _failure("save_invalid", "instances", "Saved instances or receipts exceed their bounds.")
	var rebuilt: Dictionary = {}
	for item in data.instances:
		if not item is Dictionary or not _exact_keys(item, ["id", "owner_id", "revision", "source", "position_m", "yaw_deg"]):
			return _failure("save_invalid", "instances", "Saved instance fields are invalid.")
		if not item.id is String or not String(item.id).begins_with("creation:") or rebuilt.has(item.id) or item.owner_id not in ["local_player", "guest_player"] or not _whole(item.revision) or int(item.revision) < 1 or int(item.revision) > int(data.revision):
			return _failure("save_invalid", "instances.id", "Saved identity or revision is invalid.")
		var suffix := String(item.id).trim_prefix("creation:")
		if not suffix.is_valid_int() or int(suffix) < 1 or int(suffix) >= int(data.next_id) or item.id != "creation:%08d" % int(suffix):
			return _failure("save_invalid", "instances.id", "Saved instance identity is not host-assigned.")
		if not item.position_m is Array or item.position_m.size() != 3 or not _number(item.position_m[1]):
			return _failure("save_invalid", "instances.position_m", "Saved placement is invalid.")
		var compiled: Dictionary = COMPILER.compile(item.source)
		if not compiled.ok:
			return _failure("save_source_invalid", compiled.get("path", "source"), compiled.get("message", "The saved source did not compile."))
		var placed := _validate_placement(compiled.artifact, item.position_m[0], item.position_m[2], item.yaw_deg)
		if not placed.ok or abs(float(placed.get("position_m", [0, INF, 0])[1]) - float(item.position_m[1])) > 0.00001:
			return _failure("save_placement_invalid", "instances.position_m", "Saved placement does not pass current terrain and protected-space checks.")
		var rebuilt_item: Dictionary = item.duplicate(true)
		rebuilt_item.artifact = compiled.artifact
		rebuilt_item.source = compiled.artifact.source
		rebuilt_item.active = true
		rebuilt[item.id] = rebuilt_item
	var capacity := _validate_capacity(rebuilt)
	if not capacity.ok:
		return capacity
	for key in data.receipts:
		var entry: Variant = data.receipts[key]
		if not entry is Dictionary or not _exact_keys(entry, ["principal", "action_id", "fingerprint", "receipt"]) or entry.principal not in ["local_player", "guest_player"] or not _token(entry.action_id) or key != entry.principal + "|" + entry.action_id or not _hash(entry.fingerprint):
			return _failure("save_receipt_invalid", "receipts", "Saved action identity or fingerprint is invalid.")
		var receipt: Variant = entry.receipt
		if not receipt is Dictionary or not _exact_keys(receipt, ["ok", "instance_id", "revision", "permission_revision", "replayed"]) or receipt.ok != true or receipt.replayed != false or not receipt.instance_id is String or not receipt.instance_id.begins_with("creation:") or not _whole(receipt.revision) or int(receipt.revision) < 1 or int(receipt.revision) > int(data.revision) or not _whole(receipt.permission_revision) or int(receipt.permission_revision) < 0 or int(receipt.permission_revision) > int(data.permission_revision):
			return _failure("save_receipt_invalid", "receipts.receipt", "Saved receipt revisions are invalid.")
	return {"ok": true, "instances": rebuilt}


func _available() -> bool:
	return _configured and not _load_required and (not access_guard.is_valid() or bool(access_guard.call()))


func _can_build(principal: String) -> bool:
	return _roles.get(principal, "") in ["owner", "editor"]


func _failure(code: String, path: String, message: String) -> Dictionary:
	last_error = code
	return {"ok": false, "code": code, "path": path, "message": message}


static func _number(value: Variant) -> bool:
	return (value is int or value is float) and is_finite(float(value))


static func _whole(value: Variant) -> bool:
	return _number(value) and float(value) == floor(float(value)) and abs(float(value)) <= 2147483647.0


static func _token(value: Variant) -> bool:
	if not value is String or value.is_empty() or value.length() > 64:
		return false
	for character in value:
		if not (character >= "a" and character <= "z") and not (character >= "A" and character <= "Z") and not (character >= "0" and character <= "9") and character not in ["_", "-"]:
			return false
	return true


static func _hash(value: Variant) -> bool:
	if not value is String or value.length() != 64:
		return false
	for character in value:
		if character not in "0123456789abcdef":
			return false
	return true


static func _exact_keys(value: Dictionary, keys: Array) -> bool:
	if value.size() != keys.size():
		return false
	for key in keys:
		if not value.has(key):
			return false
	return true


static func _json_safe(value: Variant, depth := 0, visited: Array = []) -> bool:
	if visited.is_empty():
		visited.append(0)
	visited[0] += 1
	if depth > 24 or int(visited[0]) > 32768:
		return false
	if value == null or value is bool or value is String:
		return true
	if value is int or value is float:
		return is_finite(float(value))
	if value is Array:
		for item in value:
			if not _json_safe(item, depth + 1, visited):
				return false
		return true
	if value is Dictionary:
		for key in value:
			if not key is String or not _json_safe(value[key], depth + 1, visited):
				return false
		return true
	return false
