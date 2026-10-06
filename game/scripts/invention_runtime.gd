extends Node3D
## Local host adapter and bounded capability interpreter. Recipes are data only.
const AUTHORITY = preload("res://scripts/creation_authority.gd")
const VISUALS = preload("res://scripts/creation_visuals.gd")
const EXECUTION = preload("res://scripts/creation_execution.gd")
const EDITOR = preload("res://scripts/invention_editor.gd")
const MAX_FIELDS := 16
var viewer
var authority = AUTHORITY.new()
var editor
var editor_open := false
var assemblies: Dictionary = {}
var instances: Dictionary = {}
var fields: Array = []
var animations: Array = []
var next_trigger: Dictionary = {}
var clock_s := 0.0
var action_counter := 0
var seen_revision := -1
var seen_permissions := -1
var hud: Label
var hud_card: PanelContainer
var message := "Choose a design with B. Press C to allow invention effects when you are ready to try it."
var guest: Node3D
var guest_velocity := Vector3.ZERO
var guest_was_affected := false
var activation_count := 0
var evaluated_nodes := 0
var rejected_activations := 0
var save_path := "user://worlds/room_inventions.json"
var session_token := ""
var field_display: MultiMeshInstance3D
var third_person := false
var camera_boom: SpringArm3D
var local_consent := false
var travel_blocked := false
var guest_gravity_scale := 1.0

func configure(map_viewer) -> void:
	viewer = map_viewer

func _ready() -> void:
	process_physics_priority = -20
	session_token = Crypto.new().generate_random_bytes(8).hex_encode()
	var configured: Dictionary = authority.configure(viewer.manifest, Callable(viewer.map_runtime, "surface_height_at"), save_path)
	var loaded: Dictionary = authority.load_saved() if configured.ok else configured
	authority.occupancy_query = Callable(self, "_check_occupancy")
	authority.activation_query = Callable(self, "_check_activation")
	viewer.player_body.spawn_clearance = Callable(self, "_spawn_clearance")
	var layer := CanvasLayer.new()
	layer.layer = 4
	add_child(layer)
	hud_card = PanelContainer.new()
	hud_card.position = Vector2(16, 16)
	hud_card.size = Vector2(840, 90)
	var card_style := StyleBoxFlat.new()
	card_style.bg_color = Color(0.045,0.11,0.12,0.92)
	card_style.content_margin_left = 14
	card_style.content_margin_right = 14
	card_style.content_margin_top = 9
	card_style.content_margin_bottom = 9
	card_style.corner_radius_bottom_left = 8
	card_style.corner_radius_bottom_right = 8
	card_style.corner_radius_top_left = 8
	card_style.corner_radius_top_right = 8
	hud_card.add_theme_stylebox_override("panel",card_style)
	layer.add_child(hud_card)
	hud = Label.new()
	hud.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	hud.add_theme_font_size_override("font_size", 16)
	hud.add_theme_color_override("font_color", Color("f4f0d9"))
	hud_card.add_child(hud)
	_make_guest()
	_make_boundary()
	_make_field_display()
	camera_boom = SpringArm3D.new()
	camera_boom.name = "CreationInspectionCamera"
	camera_boom.spring_length = 6.0
	camera_boom.margin = 0.25
	camera_boom.collision_mask = 1
	camera_boom.add_excluded_object(viewer.player_body.get_rid())
	viewer.player_body.add_child(camera_boom)
	camera_boom.position.y = 1.4
	editor = EDITOR.new()
	editor.runtime = self
	add_child(editor)
	if not loaded.ok:
		notice("Workshop save could not open: " + loaded.message)
	_refresh()

func set_editor_open(value: bool) -> void:
	editor_open = value
	viewer.player_body.editor_input_blocked = value
	viewer.player_body.jump_requested = false
	Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE if value else Input.MOUSE_MODE_CAPTURED)

func notice(text: String) -> void:
	message = text
	if hud:
		hud.text = "BARTON · INVENTION WORKSHOP\n" + message

func _action() -> String:
	action_counter += 1
	return "local_" + session_token + "_" + str(action_counter)

func _check_occupancy(artifact: Dictionary, point: Vector3, yaw_deg: float, _excluding: String) -> Dictionary:
	if artifact.source.mount != "ground":
		return {"ok":true}
	var minimum := VISUALS.vector(artifact.bounds.min)
	var maximum := VISUALS.vector(artifact.bounds.max)
	var box := Transform3D(Basis(Vector3.UP, deg_to_rad(yaw_deg)), point) * AABB(minimum, maximum-minimum)
	for body in [viewer.player_body, guest]:
		if body == null:
			continue
		var capsule := AABB(body.global_position - Vector3(0.4,0.9,0.4), Vector3(0.8,1.8,0.8))
		if box.intersects(capsule):
			return {"ok":false,"code":"occupied_placement","path":"placement","message":"Give the player and the test mannequin room. Move this ground invention away from them."}
	return {"ok":true}

func _check_activation(principal: String, item: Dictionary) -> Dictionary:
	if item.source.mount == "avatar":
		return {"ok":item.owner_id == principal,"message":"Only the wearer can manually operate this equipped design."}
	var actor: Node3D = viewer.player_body if principal == "local_player" else guest
	return {"ok":actor != null and actor.global_position.distance_to(VISUALS.vector(item.position_m)) < 9.0,"message":"Move within 9 m of this ground invention before using it."}

func _spawn_clearance(position: Vector3) -> bool:
	var capsule := AABB(position - Vector3(0.4,0.9,0.4), Vector3(0.8,1.8,0.8))
	for item in instances.values():
		if not item.active or item.source.mount != "ground":
			continue
		var minimum := VISUALS.vector(item.artifact.bounds.min)
		var maximum := VISUALS.vector(item.artifact.bounds.max)
		var box := Transform3D(Basis(Vector3.UP,deg_to_rad(float(item.yaw_deg))),VISUALS.vector(item.position_m)) * AABB(minimum,maximum-minimum)
		if capsule.intersects(box):
			return false
	return true

func default_placement() -> Dictionary:
	var point: Vector3 = viewer.player_body.global_position - viewer.player_body.global_basis.z * 4.0
	return {"x_m": snappedf(point.x, 0.25), "z_m": snappedf(point.z, 0.25), "yaw_deg": 0.0}

func preflight_draft(source: Dictionary, instance_id: String, placement: Dictionary) -> Dictionary:
	return authority.preflight("local_player", source, placement.get("x_m"), placement.get("z_m"), placement.get("yaw_deg"), instance_id)

func commit_draft(source: Dictionary, instance_id: String, placement: Dictionary, expected_revision: int, expected_permission_revision: int) -> Dictionary:
	var request := {"op": "place" if instance_id.is_empty() else "revise", "action_id": _action(), "expected_revision": expected_revision, "expected_permission_revision": expected_permission_revision, "source": source.duplicate(true), "x_m": placement.get("x_m"), "z_m": placement.get("z_m"), "yaw_deg": placement.get("yaw_deg")}
	if not instance_id.is_empty():
		request["instance_id"] = instance_id
	var result: Dictionary = authority.submit("local_player", request)
	if result.ok:
		_refresh()
		notice("Saved " + str(source.name) + (". E uses your worn design; Q revises it; H changes the view." if source.mount == "avatar" else ". Approach it and press F to use or V to revise."))
	return result

func remove_creation(instance_id: String, expected_revision: int, expected_permission_revision: int) -> Dictionary:
	var result: Dictionary = authority.submit("local_player", {"op":"remove", "action_id":_action(), "instance_id":instance_id, "expected_revision":expected_revision, "expected_permission_revision":expected_permission_revision})
	if result.ok:
		_refresh()
		notice("Invention removed. Its active effects have stopped.")
	return result

func set_local_consent(enabled: bool) -> Dictionary:
	var result: Dictionary = authority.set_consent("local_player", enabled)
	_refresh()
	return result

func set_guest_consent(enabled: bool) -> Dictionary:
	# Only the local consent-lab adapter may impersonate this synthetic mannequin.
	var result: Dictionary = authority.set_consent("guest_player", enabled)
	_refresh()
	return result

func set_guest_builder(enabled: bool) -> Dictionary:
	var result: Dictionary = authority.set_role("local_player", "guest_player", "editor" if enabled else "visitor")
	_refresh()
	return result

func _refresh() -> void:
	# A small readiness/revision check avoids copying every saved source 60 times/s.
	if authority.is_ready() and seen_revision == authority.revision and seen_permissions == authority.permission_revision:
		return
	var snapshot: Dictionary = authority.snapshot("local_player")
	if not snapshot.ok:
		local_consent = false
		fields.clear()
		animations.clear()
		next_trigger.clear()
		instances.clear()
		viewer.player_body.clear_creation_motion()
		for assembly in assemblies.values():
			remove_child(assembly)
			assembly.queue_free()
		assemblies.clear()
		seen_revision = -1
		seen_permissions = -1
		return
	if seen_revision == int(snapshot.revision) and seen_permissions == int(snapshot.permission_revision):
		return
	# Recompile-backed replacement invalidates every effect and transient reference.
	fields.clear()
	animations.clear()
	next_trigger.clear()
	viewer.player_body.clear_creation_motion()
	guest_velocity = Vector3.ZERO
	for assembly in assemblies.values():
		remove_child(assembly)
		assembly.queue_free()
	assemblies.clear()
	instances = snapshot.instances
	local_consent = bool(snapshot.consent.local_player)
	for item in instances.values():
		var assembly: Node3D = VISUALS.build(item.artifact, item.source.mount == "ground" and item.active)
		add_child(assembly)
		assembly.position = VISUALS.vector(item.position_m)
		assembly.rotation.y = deg_to_rad(float(item.yaw_deg))
		assemblies[item.id] = assembly
	seen_revision = int(snapshot.revision)
	seen_permissions = int(snapshot.permission_revision)
	notice(message)

func nearest_creation() -> String:
	var closest := ""
	var distance := 9.0
	for id in assemblies:
		if instances[id].source.mount != "ground":
			continue
		var candidate: float = assemblies[id].global_position.distance_to(viewer.player_body.global_position)
		if candidate < distance:
			closest = id
			distance = candidate
	return closest

func equipped_creation() -> String:
	for id in instances:
		if instances[id].source.mount == "avatar" and instances[id].owner_id == "local_player":
			return id
	return ""

func _unhandled_key_input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed or event.echo or editor_open or travel_blocked or not viewer.walking:
		return
	if event.keycode == KEY_B:
		editor.open_editor()
	elif event.keycode in [KEY_V, KEY_Q]:
		var id := equipped_creation() if event.keycode == KEY_Q else nearest_creation()
		if not id.is_empty():
			editor.open_editor(id)
		else:
			notice("No worn design to revise. B opens a new draft." if event.keycode == KEY_Q else "Walk closer to a ground invention to revise it. Q revises your worn design.")
	elif event.keycode in [KEY_F, KEY_E]:
		var id := equipped_creation() if event.keycode == KEY_E else nearest_creation()
		var result := activate_creation(id) if not id.is_empty() else {"ok":false, "message":"No worn design is equipped. Build one with B." if event.keycode == KEY_E else "Walk within 9 m of a ground invention, then press F. E uses your worn design."}
		notice("Invention activated. Try standing in its wind field." if result.ok else result.message)
	elif event.keycode == KEY_C:
		var snapshot: Dictionary = authority.snapshot("local_player")
		if snapshot.ok:
			var enabled := not bool(snapshot.consent.local_player)
			set_local_consent(enabled)
			notice("Invention effects allowed." if enabled else "Invention effects stopped. Your movement is your own.")
	elif event.keycode == KEY_H:
		set_third_person(not third_person)
	else:
		return
	get_viewport().set_input_as_handled()

func set_third_person(enabled: bool) -> void:
	third_person = enabled
	if enabled:
		camera_boom.rotation.x = viewer.walk_camera.rotation.x
		viewer.walk_camera.reparent(camera_boom, false)
		viewer.walk_camera.transform = Transform3D.IDENTITY
	else:
		var camera_pitch: float = camera_boom.rotation.x
		viewer.walk_camera.reparent(viewer.player_body, false)
		viewer.walk_camera.position = Vector3(0,0.82,0)
		viewer.walk_camera.rotation = Vector3(camera_pitch,0,0)

func activate_creation(id: String, principal := "local_player") -> Dictionary:
	_refresh()
	var result: Dictionary = authority.submit(principal, {"op":"activate", "action_id":_action(), "instance_id":id, "expected_revision":authority.revision, "expected_permission_revision":authority.permission_revision})
	if not result.ok or result.get("replayed", false):
		return result
	for node in instances[id].source.nodes:
		if node.op == "interact":
			return _fire(id, node.id, principal)
	return {"ok":false, "message":"This design uses a timer, sensor or passive glide. It has no Use trigger."}

func _fire(id: String, trigger: String, principal: String) -> Dictionary:
	if not instances.has(id):
		return {"ok":false, "message":"That invention no longer exists."}
	var item: Dictionary = instances[id]
	var ordered: Array = EXECUTION.plan(item.artifact, trigger)
	var field_count := 0
	for node in ordered:
		if node.op == "wind":
			field_count += 1
	if fields.size() + field_count > MAX_FIELDS:
		rejected_activations += 1
		return {"ok":false, "message":"The workshop's wind capacity is busy. Try again shortly."}
	var admitted: Dictionary = authority.consume_runtime_budget(id, principal, field_count, ordered.size(), clock_s)
	if not admitted.ok:
		rejected_activations += 1
		return admitted
	activation_count += 1
	evaluated_nodes += ordered.size()
	var parts: Dictionary = assemblies[id].get_meta("parts")
	for node in ordered:
		var part: Node3D = parts[node.part_id]
		var params: Dictionary = node.params
		if node.op == "wind":
			fields.append({"instance_id":id, "owner":item.owner_id, "part":part, "direction":VISUALS.vector(params.direction), "acceleration":float(params.acceleration_mps2), "radius":float(params.radius_m), "until":clock_s+float(params.duration_s)})
		elif node.op in ["spin", "light"]:
			# Same node replaces its earlier animation; no growing duplicate jobs.
			animations = animations.filter(func(a): return not (a.instance_id == id and a.node_id == node.id))
			animations.append({"instance_id":id, "node_id":node.id, "part":part, "op":node.op, "params":params, "until":clock_s+float(params.duration_s), "angle":0.0, "base":part.get_meta("authored_basis")})
	return {"ok":true}

func _physics_process(delta: float) -> void:
	if travel_blocked:
		viewer.player_body.clear_creation_motion()
		hud_card.visible = false
		return
	clock_s += delta
	_refresh()
	if instances.is_empty():
		viewer.player_body.set_creation_effects(Vector3.ZERO, 0.0, Callable())
	# Avatar designs attach to the approved wearer; no user-supplied target IDs.
	for id in assemblies:
		var item: Dictionary = instances[id]
		if item.source.mount == "avatar":
			var wearer: Node3D = viewer.player_body if item.owner_id == "local_player" else guest
			assemblies[id].global_transform = wearer.global_transform
			assemblies[id].position.y -= 0.6
			assemblies[id].visible = item.owner_id != "local_player" or third_person
	fields = fields.filter(func(field): return clock_s < field.until and is_instance_valid(field.part) and instances.has(field.instance_id) and instances[field.instance_id].active)
	for id in instances:
		var item: Dictionary = instances[id]
		if not item.active:
			continue
		var parts: Dictionary = assemblies[id].get_meta("parts")
		for node in item.source.nodes:
			if node.op not in ["timer", "proximity"]:
				continue
			var key: String = id + "/" + node.id
			if clock_s < float(next_trigger.get(key, 0.0)):
				continue
			next_trigger[key] = clock_s + float(node.params.interval_s)
			var actor: String = item.owner_id
			if node.op == "proximity":
				actor = ""
				for target in ["local_player", "guest_player"]:
					var point: Vector3 = viewer.player_body.global_position if target == "local_player" else guest.global_position
					if authority.can_affect(item.owner_id, target, point) and point.distance_to(parts[node.part_id].global_position) <= float(node.params.radius_m):
						actor = target
						break
			if not actor.is_empty():
				_fire(id, node.id, actor)
	_step_animations(delta)
	field_display.multimesh.visible_instance_count = fields.size()
	for index in range(fields.size()):
		var field: Dictionary = fields[index]
		var radius: float = field.radius
		field_display.multimesh.set_instance_transform(index, Transform3D(Basis.IDENTITY.scaled(Vector3(radius,0.12,radius)), field.part.global_position))
	var local_effect := effect_at("local_player", viewer.player_body.global_position)
	viewer.player_body.set_creation_effects(local_effect.acceleration, local_effect.glide, Callable(self, "_local_position_allowed") if local_effect.allowed else Callable())
	_step_guest(delta)
	if not editor_open:
		var state := "effects allowed" if local_consent else "effects stopped"
		hud.text = "BARTON · INVENTION WORKSHOP   /   " + state + "\nB Build · F Device · E Worn design · V/Q Revise · C Consent · H Camera\n" + message
	hud_card.visible = not editor_open and viewer.walking

func _step_animations(delta: float) -> void:
	var remaining: Array = []
	for animation in animations:
		if not is_instance_valid(animation.part):
			continue
		if animation.op == "light":
			var lamp = animation.part.get_node_or_null("Light_" + animation.node_id)
			if lamp:
				lamp.light_energy = float(animation.params.intensity) if clock_s < animation.until else 0.0
		elif animation.op == "spin":
			var bounded_delta := minf(delta, maxf(0.0, float(animation.until) - (clock_s - delta)))
			animation.angle = fposmod(float(animation.angle) + float(animation.params.speed_rpm) * TAU / 60.0 * bounded_delta, TAU)
			animation.part.basis = animation.base * Basis(Vector3.UP, animation.angle)
		if clock_s < animation.until:
			remaining.append(animation)
	animations = remaining

func effect_at(target: String, position: Vector3) -> Dictionary:
	var acceleration := Vector3.ZERO
	var glide := 0.0
	for field in fields:
		if _body_allowed(field.owner, target, position) and authority.can_affect(field.owner, target, field.part.global_position) and position.distance_to(field.part.global_position) <= field.radius:
			acceleration += field.part.get_parent().global_basis * field.part.get_meta("authored_basis") * field.direction * field.acceleration
	for item in instances.values():
		if item.source.mount == "avatar" and item.owner_id == target and _body_allowed(item.owner_id, target, position):
			for node in item.source.nodes:
				if node.op == "glide":
					glide = float(node.params.fall_speed_mps)
	return {"acceleration":acceleration.limit_length(4.0), "glide":glide, "allowed":not acceleration.is_zero_approx() or glide > 0.0}

func _local_position_allowed(position: Vector3) -> bool:
	# All admitted force sources share this plot; owner ACL is rechecked in effect_at.
	return _body_allowed("local_player", "local_player", position)

func _body_allowed(owner: String, target: String, position: Vector3) -> bool:
	for offset in [Vector3(-0.4,0,-0.4),Vector3(0.4,0,-0.4),Vector3(-0.4,0,0.4),Vector3(0.4,0,0.4)]:
		if not authority.can_affect(owner, target, position+offset):
			return false
	return true

func _step_guest(delta: float) -> void:
	var effect := effect_at("guest_player", guest.position)
	var affected: bool = not effect.acceleration.is_zero_approx()
	if guest_was_affected and not affected:
		guest_velocity = Vector3.ZERO
	var previous := guest.position
	guest_velocity += effect.acceleration * delta
	if effect.acceleration.y <= 0.0:
		guest_velocity.y -= 9.8 * guest_gravity_scale * delta
	guest_velocity = guest_velocity.limit_length(8.0)
	guest.position += guest_velocity * delta
	if affected and not _body_allowed("local_player", "guest_player", guest.position):
		guest.position = previous
		guest_velocity = Vector3.ZERO
	var ground: float = viewer.map_runtime.surface_height_at(guest.position.x, guest.position.z) + 0.9
	if guest.position.y < ground:
		guest.position.y = ground
		guest_velocity.y = 0.0
	guest_was_affected = affected

func _make_field_display() -> void:
	field_display = MultiMeshInstance3D.new()
	field_display.name = "BoundedWindIndicators"
	var ring := TorusMesh.new()
	ring.inner_radius = 0.98
	ring.outer_radius = 1.0
	ring.rings = 24
	ring.ring_segments = 4
	var paint := StandardMaterial3D.new()
	paint.albedo_color = Color("acd6c6")
	paint.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	ring.material = paint
	var batch := MultiMesh.new()
	batch.transform_format = MultiMesh.TRANSFORM_3D
	batch.mesh = ring
	batch.instance_count = MAX_FIELDS
	batch.visible_instance_count = 0
	field_display.multimesh = batch
	add_child(field_display)

func _make_guest() -> void:
	guest = Node3D.new()
	guest.name = "LocalConsentMannequin"
	add_child(guest)
	guest.position = Vector3(7, viewer.map_runtime.surface_height_at(7,346)+0.9,346)
	var mesh := MeshInstance3D.new()
	var body := CapsuleMesh.new()
	body.radius = 0.32
	body.height = 1.8
	mesh.mesh = body
	var material := StandardMaterial3D.new()
	material.albedo_color = Color("d1a971")
	material.roughness = 0.9
	mesh.material_override = material
	guest.add_child(mesh)
	var label := Label3D.new()
	label.text = "CONSENT LAB\nLocal test mannequin"
	label.position.y = 1.35
	label.font_size = 30
	label.pixel_size = 0.006
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	guest.add_child(label)

func _make_boundary() -> void:
	for corner in [Vector2(-15,380),Vector2(15,380),Vector2(-15,409),Vector2(15,409)]:
		var marker := Label3D.new()
		marker.text = "PUBLIC GARDEN\nNo building or invention effects"
		marker.position = Vector3(corner.x,viewer.map_runtime.surface_height_at(corner.x,corner.y)+2,corner.y)
		marker.font_size = 30
		marker.pixel_size = 0.008
		marker.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		add_child(marker)
