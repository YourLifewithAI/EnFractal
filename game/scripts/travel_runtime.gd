extends Node3D
## Local trusted host bridge. Remote gameplay authentication is a separate gate.
const TRANSPORT = preload("res://scripts/travel_transport.gd")
const AUTHORITY = preload("res://scripts/creation_authority.gd")
const PANEL = preload("res://scripts/travel_panel.gd")
const PHYSICS = preload("res://scripts/world_physics_profile.gd")
const GROUND = preload("res://scripts/painterly_ground_kit.gd")
var viewer
var workshop
var panel
var transport = TRANSPORT.new()
var session_id := ""
var world_id := "home"
var presence_epoch := 0
var store_revision := 0
var connected := false
var transitioning := false
var panel_open := false
var status: Dictionary = {}
var message := "Connecting to your saved worlds…"
var invite_id := ""
var action_counter := 0
var token_prefix := ""
var pending_save_hash := ""
var pending_save_action := ""
var heartbeat: Thread
var heartbeat_at := 0
var last_contact := 0
var heartbeat_session := ""
var heartbeat_context: Dictionary = {}
var initial_home: Dictionary = {}
var world_label: Label
var portal_label: Label3D
var portal_root: Node3D
var draft_cache: Dictionary = {}

func configure(map_viewer, invention_workshop) -> void:
	viewer = map_viewer
	workshop = invention_workshop

func _ready() -> void:
	token_prefix = Crypto.new().generate_random_bytes(8).hex_encode()
	transport.port = int(OS.get_environment("ENFRACTAL_TRAVEL_PORT")) if not OS.get_environment("ENFRACTAL_TRAVEL_PORT").is_empty() else 8765
	transport.token = OS.get_environment("ENFRACTAL_TRAVEL_TOKEN")
	panel = PANEL.new()
	panel.runtime = self
	add_child(panel)
	var layer := CanvasLayer.new()
	layer.layer = 5
	add_child(layer)
	world_label = Label.new()
	world_label.position = Vector2(18,116)
	world_label.add_theme_font_size_override("font_size",17)
	world_label.add_theme_color_override("font_color",Color("f4ead5"))
	world_label.add_theme_color_override("font_shadow_color",Color("142526"))
	world_label.add_theme_constant_override("shadow_offset_x",1)
	world_label.add_theme_constant_override("shadow_offset_y",1)
	layer.add_child(world_label)
	_make_portal()
	if workshop.authority.is_ready():
		initial_home = workshop.authority.export_envelope()
	workshop.authority.access_guard = Callable(self,"_can_edit")
	_freeze(true)
	call_deferred("_connect_after_entry")

func _connect_after_entry() -> void:
	await get_tree().process_frame
	reconnect()

func _can_edit() -> bool:
	return connected and not transitioning and Time.get_ticks_msec() - last_contact < 7000

func _action() -> String:
	action_counter += 1
	return "travel_" + token_prefix + "_" + str(action_counter)

func _context(op: String, mutation := true) -> Dictionary:
	var request := {"op":op,"session_id":session_id,"world_id":world_id,"presence_epoch":presence_epoch}
	if mutation:
		request.action_id = _action()
	return request

func _rpc(request: Dictionary) -> Dictionary:
	var result: Dictionary = transport.call_service(request)
	if result.get("ok",false):
		_accept_metadata(result)
	else:
		message = str(result.get("message","The save service could not confirm this action."))
		if str(result.get("code","")) in ["commit_unknown","database_unavailable","service_unavailable","session_stale","host_stale","presence_stale"]:
			_fail_closed(result)
	return result

func _accept_metadata(result: Dictionary) -> void:
	session_id = str(result.get("session_id",session_id))
	world_id = str(result.get("world_id",world_id))
	presence_epoch = int(result.get("presence_epoch",presence_epoch))
	store_revision = int(result.get("store_revision",store_revision))
	if result.get("status") is Dictionary:
		status = result.status.duplicate(true)
	last_contact = Time.get_ticks_msec()

func _bootstrap() -> Dictionary:
	_freeze(true)
	connected = false
	if initial_home.is_empty():
		return {"ok":false,"message":"The existing local workshop save is invalid. Repair or restore it before importing saved worlds; it has not been overwritten."}
	var empty = AUTHORITY.new()
	empty.configure(viewer.manifest,Callable(viewer.map_runtime,"surface_height_at"),"user://worlds/travel_empty_unused.json")
	# Only the first bootstrap imports the existing Home source. The service never
	# overwrites an existing database world from a later local client snapshot.
	return _rpc({"op":"bootstrap","action_id":_action(),"base_pin":initial_home.base_pin,"initial_home":initial_home,"empty_world":empty.export_envelope()})

func reconnect() -> Dictionary:
	var result := _bootstrap()
	if result.ok:
		result = _load_and_arrive(result)
	if not result.ok:
		_fail_closed(result)
	return result

func _materialize(result: Dictionary, release := true) -> Dictionary:
	if not result.get("envelope") is Dictionary:
		return {"ok":false,"message":"The destination save could not be read. Reconnect to recover safely."}
	var replacement = AUTHORITY.new()
	replacement.configure(viewer.manifest,Callable(viewer.map_runtime,"surface_height_at"),"user://worlds/travel_unused.json")
	var checked: Dictionary = replacement.load_envelope(result.envelope)
	if not checked.ok:
		return checked
	if workshop.editor.ui.visible:
		workshop.editor.close_editor()
	var from_world: String = str(workshop.get_meta("travel_world","home"))
	draft_cache[from_world] = {"source":workshop.editor._suspended_new_source.duplicate(true),"placement":workshop.editor._suspended_new_placement.duplicate(true)}
	# An old editor's tested hash or instance ID cannot be applied in another world.
	workshop.editor.queue_free()
	workshop.editor = workshop.EDITOR.new()
	workshop.editor.runtime = workshop
	workshop.add_child(workshop.editor)
	if draft_cache.has(world_id):
		workshop.editor._suspended_new_source = draft_cache[world_id].source.duplicate(true)
		workshop.editor._suspended_new_placement = draft_cache[world_id].placement.duplicate(true)
	workshop.set_meta("travel_world",world_id)
	workshop.authority = replacement
	replacement.occupancy_query = Callable(workshop,"_check_occupancy")
	replacement.activation_query = Callable(workshop,"_check_activation")
	replacement.persistence_sink = Callable(self,"_persist_creation")
	replacement.access_guard = Callable(self,"_can_edit")
	connected = true
	transitioning = false
	pending_save_hash = ""
	pending_save_action = ""
	workshop.seen_revision = -1
	workshop.seen_permissions = -1
	workshop._refresh()
	var scale := float(status.get("current_world",{}).get("gravity_scale",1.0))
	workshop.guest_gravity_scale = scale
	var rules := PHYSICS.DEFAULT.duplicate(true)
	rules.id = "home" if world_id == "home" else "sandbox_quarter_gravity"
	# The controller's activation revision is local and monotonic. Durable rules
	# revisions are scoped to a world and may legitimately decrease on returning.
	rules.revision = int(viewer.player_body.world_physics.revision) + 1
	rules.gravity_mps2 = float(PHYSICS.DEFAULT.gravity_mps2) * scale
	rules.glide_gravity_mps2 = float(PHYSICS.DEFAULT.glide_gravity_mps2) * scale
	if not viewer.player_body.set_world_physics(rules):
		return {"ok":false,"message":"The destination rules are incompatible with this player. Return Home or reconnect."}
	var checkpoint: Array = result.get("arrival_checkpoint",[-22.0,0.0,340.0])
	var heading := float(result.get("heading_rad",0.0))
	if not viewer.walking:
		viewer._set_walking_mode(true)
	if workshop.third_person:
		workshop.set_third_person(false)
	viewer.walk_camera.rotation.x = -0.03
	if not viewer.player_body.spawn_at(float(checkpoint[0]),float(checkpoint[2]),heading):
		if not viewer.player_body.spawn_at(-22.0,340.0,0.0):
			return {"ok":false,"message":"The arrival ground is unavailable. Your saved location is safe; reconnect to try again."}
	viewer.terrain_colliders.update_center(viewer.player_body.global_position)
	workshop.guest.position = Vector3(7,viewer.map_runtime.surface_height_at(7,346)+0.9,346)
	workshop.guest_velocity = Vector3.ZERO
	_freeze(not release)
	message = "Saved world loaded. Invention effects start off; C allows them."
	workshop.notice(message)
	return {"ok":true,"message":message}

func _load_and_arrive(result: Dictionary) -> Dictionary:
	var transfer := str(result.get("transfer_id",status.get("pending",{}).get("id","")))
	var loaded := _materialize(result,false)
	if not loaded.ok:
		return loaded
	if not transfer.is_empty():
		var request := _context("travel_arrive")
		request.transfer_id = transfer
		var arrived := _rpc(request)
		if not arrived.ok:
			return arrived
	_freeze(false)
	return loaded

func _persist_creation(envelope: Dictionary) -> Dictionary:
	var fingerprint: String = workshop.authority.COMPILER.canonical_json(envelope).sha256_text()
	if fingerprint != pending_save_hash:
		pending_save_hash = fingerprint
		pending_save_action = _action()
	var request := _context("world_save")
	request.action_id = pending_save_action
	request.expected_store_revision = store_revision
	request.envelope = envelope
	var result := _rpc(request)
	if not result.ok:
		_fail_closed(result)
	else:
		pending_save_hash = ""
		pending_save_action = ""
		message = "Invention saved durably in " + str(status.get("current_world",{}).get("name","this world")) + "."
	return result

func travel_status() -> Dictionary:
	var view := status.duplicate(true)
	view.ok = connected
	view.message = message
	if not connected:
		view.pending = {"recovery":true}
	return view

func create_sandbox() -> Dictionary:
	if not _can_edit():
		return {"ok":false,"message":"Reconnect to your saved world before creating a sandbox."}
	var request := _context("create_sandbox")
	request.name = "My Greenbelt"
	request.gravity = 0.25
	var result := _rpc(request)
	if result.ok:
		message = "Your free sandbox is saved. It starts from the landscape with its own inventions and quarter gravity."
	result.message = message
	return result

func invite_guest() -> Dictionary:
	var target := _owned_sandbox()
	if target.is_empty():
		return {"ok":false,"message":"Create your sandbox first."}
	var existing := _guest_invitations()
	if not existing.is_empty():
		invite_id = str(existing[0].id)
		return {"ok":true,"message":"A saved invitation for the local test guest is already available. Revoke it before issuing another."}
	var request := _context("invite")
	request.merge({"sandbox_id":target,"recipient":"guest_player","role":"visitor","expires_in_seconds":3600,"max_uses":1})
	var result := _rpc(request)
	if result.ok:
		invite_id = str(result.get("invite_id",""))
		message = "A one-use, one-hour visitor invitation is saved for the local test identity. Remote invitations come after account integration."
	result.message = message
	return result

func revoke_guest() -> Dictionary:
	var invitations := _guest_invitations()
	if invitations.is_empty():
		return {"ok":false,"message":"There are no outstanding saved test invitations to revoke."}
	for invitation in invitations:
		var request := _context("revoke")
		request.invite_id = invitation.id
		var result := _rpc(request)
		if not result.ok:
			return result
	message = "The saved test invitations have been revoked."
	invite_id = ""
	return {"ok":true,"message":message}

func _guest_invitations() -> Array:
	return status.get("invitations",[]).filter(func(invitation): return invitation.get("owner_id") == "local_player" and invitation.get("recipient") == "guest_player" and not invitation.get("revoked",false) and int(invitation.get("remaining_uses",0)) > 0 and float(invitation.get("expires_at",0)) > Time.get_unix_time_from_system())

func _owned_sandbox() -> String:
	for world in status.get("worlds",[]):
		if world.id != "home" and world.owner_id == "local_player":
			return world.id
	return ""

func visit_world(destination: String) -> Dictionary:
	if not _can_edit():
		return {"ok":false,"message":"Reconnect before starting another journey."}
	if destination == "home":
		return return_home()
	var request := _context("travel_prepare")
	request.merge({"destination_world_id":destination,"invite_id":"","expected_store_revision":store_revision,"envelope":workshop.authority.export_envelope(),"source_checkpoint":_checkpoint(),"heading_rad":viewer.player_body.rotation.y})
	_freeze(true)
	var result := _rpc(request)
	if not result.ok:
		_fail_closed(result)
		return result
	var transfer: String = str(result.get("transfer_id",""))
	var revision: int = int(result.get("destination_rules",{}).get("revision",result.get("rules_revision",0)))
	for op in ["travel_freeze","travel_commit"]:
		request = _context(op)
		request.transfer_id = transfer
		if op == "travel_commit":
			request.expected_rules_revision = revision
		result = _rpc(request)
		if not result.ok:
			_fail_closed(result)
			return result
	result = _load_and_arrive(result)
	if not result.ok:
		_fail_closed(result)
	else:
		message = "Arrived in your sandbox. Quarter gravity; separate saved inventions. Return Home is always in T Travel."
	result.message = message
	return result

func return_home() -> Dictionary:
	# A lost response is reconciled by bootstrap first. Never revive a source on timeout.
	var source_materialized := connected and not transitioning
	if not connected:
		# Recover service identity separately: an incompatible destination scene must
		# never prevent a new fenced journey to a healthy Home save.
		var recovered := _bootstrap()
		if not recovered.ok:
			_fail_closed(recovered)
			return recovered
	_freeze(true)
	var request := _context("return_home")
	if source_materialized:
		request.source_checkpoint = _checkpoint()
		request.heading_rad = viewer.player_body.rotation.y
	var result := _rpc(request)
	if result.ok:
		result = _load_and_arrive(result)
	if not result.ok:
		_fail_closed(result)
	else:
		message = "Returned Home. Your Home inventions and normal gravity are restored."
	result.message = message
	return result

func _checkpoint() -> Array:
	var point: Vector3 = viewer.player_body.global_position
	return [point.x,point.y,point.z]

func _freeze(value: bool) -> void:
	transitioning = value
	workshop.travel_blocked = value or panel_open
	viewer.player_body.editor_input_blocked = value or panel_open or workshop.editor_open
	if value:
		workshop.fields.clear()
		workshop.animations.clear()
		workshop.next_trigger.clear()
		viewer.player_body.clear_creation_motion()
		viewer.player_body.velocity = Vector3.ZERO
		viewer.player_body.set_physics_process(false)
	else:
		viewer.player_body.set_physics_process(viewer.player_body.active)

func _fail_closed(result: Dictionary) -> void:
	connected = false
	message = str(result.get("message","The journey could not be confirmed. Reconnect to recover it."))
	_freeze(true)
	workshop.notice(message + " T opens travel recovery.")

func set_panel_open(value: bool) -> void:
	panel_open = value
	workshop.travel_blocked = value or transitioning
	viewer.player_body.editor_input_blocked = value or transitioning or workshop.editor_open
	viewer.player_body.jump_requested = false
	Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE if value else Input.MOUSE_MODE_CAPTURED)

func _unhandled_key_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_T and not workshop.editor_open:
		if panel_open:
			panel.close_panel()
		else:
			panel.open_panel()
		get_viewport().set_input_as_handled()

func _process(_delta: float) -> void:
	var current: Dictionary = status.get("current_world",{})
	world_label.text = str(current.get("name","Saved worlds")) + (" · quarter gravity" if float(current.get("gravity_scale",1)) < 1 else " · normal gravity") + "   |   T Travel / Return Home"
	world_label.visible = not panel_open and not workshop.editor_open
	if portal_label:
		portal_label.text = "GREENBELT PORTAL\nT · Travel and Return Home"
	if heartbeat != null and not heartbeat.is_alive():
		var result: Dictionary = heartbeat.wait_to_finish()
		heartbeat = null
		if connected and not transitioning and heartbeat_session == session_id and heartbeat_context == {"world":world_id,"epoch":presence_epoch,"revision":store_revision}:
			if not result.get("ok",false) or str(result.get("world_id",world_id)) != world_id or int(result.get("presence_epoch",presence_epoch)) != presence_epoch or int(result.get("store_revision",store_revision)) != store_revision:
				_fail_closed(result)
			else:
				status = result.get("status",status).duplicate(true)
				last_contact = Time.get_ticks_msec()
	if connected and not transitioning and heartbeat == null and Time.get_ticks_msec() >= heartbeat_at:
		heartbeat_at = Time.get_ticks_msec() + 2000
		heartbeat_session = session_id
		heartbeat_context = {"world":world_id,"epoch":presence_epoch,"revision":store_revision}
		var request := _context("status",false)
		heartbeat = Thread.new()
		heartbeat.start(Callable(transport,"call_service").bind(request))
	if connected and Time.get_ticks_msec() - last_contact >= 7000:
		_fail_closed({"message":"The save service stopped responding. Reconnect to recover your world."})

func _exit_tree() -> void:
	if heartbeat != null:
		heartbeat.wait_to_finish()

func _make_portal() -> void:
	portal_root = Node3D.new()
	portal_root.name = "GreenbeltTravelDoorway"
	add_child(portal_root)
	portal_root.position = Vector3(-22,viewer.map_runtime.surface_height_at(-22,348),348)
	var stone := GROUND.make_surface_material("limestone")
	var seed_value := 410
	for offset in [Vector3(-1.3,0,0),Vector3(1.3,0,0),Vector3(0,3.5,0)]:
		var size := Vector3(0.5,3.6,0.65) if offset.x != 0 else Vector3(3.1,0.5,0.7)
		var mesh := GROUND.make_limestone_piece(size,seed_value,stone)
		seed_value += 1
		mesh.position = offset
		portal_root.add_child(mesh)
	portal_label = Label3D.new()
	portal_label.position.y = 4.25
	portal_label.font_size = 34
	portal_label.pixel_size = 0.008
	portal_label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	portal_root.add_child(portal_label)
