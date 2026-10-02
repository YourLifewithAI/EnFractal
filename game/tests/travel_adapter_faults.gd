extends SceneTree
## Real map/controller + deterministic transport faults. No database or personal save writes.
const COMPILER = preload("res://scripts/creation_compiler.gd")
var checks := 0
var failures := 0
var viewer
var travel
var workshop

class FaultTransport extends RefCounted:
	var mode := "offline"
	var home: Dictionary = {}
	var calls: Array = []
	var saved_revision := 0

	func response(world: String, envelope: Dictionary, revision: int) -> Dictionary:
		var summary := {"id":world,"name":"Home Earth" if world == "home" else "Broken destination","owner_id":"local_player","gravity_scale":1.0 if world == "home" else 0.25,"status":"active","can_visit":true,"occupants":["local_player"]}
		return {"ok":true,"session_id":"fault_session","world_id":world,"presence_epoch":2 if world == "home" else 1,"store_revision":revision,"envelope":envelope.duplicate(true),"rules":{"revision":1,"gravity_scale":summary.gravity_scale},"arrival_checkpoint":[-22.0,0.0,340.0],"heading_rad":0.0,"status":{"current_world":summary,"worlds":[summary],"presence":{"world_id":world},"pending":{},"invitations":[]}}

	func call_service(request: Dictionary) -> Dictionary:
		calls.append(request.duplicate(true))
		if mode == "offline":
			return {"ok":false,"code":"service_unavailable","message":"Injected local service outage."}
		if request.op == "bootstrap":
			var broken := home.duplicate(true)
			broken.compiler_version = 999
			return response("sandbox_local_player",broken,0)
		if request.op == "return_home":
			return response("home",home,saved_revision)
		if request.op == "world_save":
			home = request.envelope.duplicate(true)
			saved_revision += 1
			return response("home",home,saved_revision)
		return response("home",home,saved_revision)

func _initialize() -> void:
	call_deferred("_run")

func check(value: bool, label: String) -> void:
	checks += 1
	if not value:
		failures += 1
		push_error("Travel adapter fault: " + label)

func _fixed_reply(response: Dictionary) -> Dictionary:
	# Only detached JSON data crosses this thread boundary, never scene nodes.
	return response.duplicate(true)

func _finish_heartbeat(response: Dictionary, context: Dictionary) -> void:
	travel.heartbeat_session = travel.session_id
	travel.heartbeat_context = context.duplicate(true)
	travel.heartbeat = Thread.new()
	check(travel.heartbeat.start(Callable(self,"_fixed_reply").bind(response)) == OK,"status thread starts")
	for frame in range(120):
		if not travel.heartbeat.is_alive():
			return
		await process_frame
	check(false,"status response completes within bounded frames")

func _run() -> void:
	var unique := str(OS.get_process_id())
	OS.set_environment("ENFRACTAL_MANUAL_INVENTION","1")
	OS.set_environment("ENFRACTAL_SAVE_TRAVEL","1")
	OS.set_environment("ENFRACTAL_START_WALK","1")
	OS.set_environment("ENFRACTAL_INVENTION_SAVE","user://tests/travel_faults_" + unique + ".json")
	OS.set_environment("ENFRACTAL_WORKSHOP_TEST_SAVE","user://tests/travel_faults_legacy_" + unique + ".json")
	viewer = load("res://scenes/main.tscn").instantiate()
	root.add_child(viewer)
	travel = viewer.travel_runtime
	workshop = viewer.invention_runtime
	var transport := FaultTransport.new()
	transport.home = travel.initial_home.duplicate(true)
	# Installed before the deferred first connection, so this fixture never sends HTTP.
	travel.transport = transport
	for frame in range(4):
		await process_frame
	travel.set_process(false)
	check(transport.calls.size() == 1 and transport.calls[0].op == "bootstrap","startup reaches injected transport exactly once")
	check(not travel.connected and travel.transitioning,"unavailable startup remains in recovery")
	check(not viewer.walking and not viewer.player_body.is_physics_processing(),"deferred walking startup cannot reactivate a frozen player")
	check(workshop.travel_blocked and viewer.player_body.editor_input_blocked,"unavailable startup blocks creation and movement")
	var frozen_position: Vector3 = viewer.player_body.position
	for frame in range(4):
		await physics_frame
	check(viewer.player_body.position == frozen_position,"gravity cannot move unavailable startup presence")

	transport.mode = "broken_destination"
	var failed_load: Dictionary = travel.reconnect()
	check(not failed_load.ok and not travel.connected,"durable destination with unsupported compiler stays frozen")
	check(travel.world_id == "sandbox_local_player" and travel.session_id == "fault_session","failed destination still retains confirmed service identity")
	check(not viewer.player_body.is_physics_processing(),"failed destination never activates physics")
	var first_return := transport.calls.size()
	var returned: Dictionary = travel.return_home()
	check(returned.ok and travel.connected and travel.world_id == "home","Return Home succeeds without materializing broken destination")
	check(transport.calls.size() == first_return + 2 and transport.calls[first_return].op == "bootstrap" and transport.calls[first_return + 1].op == "return_home","recovery establishes identity then routes Home directly")
	check(viewer.walking and viewer.player_body.is_physics_processing(),"healthy Home activates actual walking controller")
	check(is_equal_approx(viewer.player_body.world_physics.gravity_mps2,22.0),"recovered Home restores approved normal gravity")
	check(not workshop.authority.snapshot("local_player").consent.local_player,"recovered Home requires fresh effect consent")
	check(workshop.fields.is_empty() and viewer.player_body.creation_velocity.is_zero_approx(),"recovery carries no destination force state")

	# Status captured before an edit finishes, but the callback is consumed afterward.
	var old_context := {"world":travel.world_id,"epoch":travel.presence_epoch,"revision":travel.store_revision}
	var old_reply := transport.response("home",transport.home,travel.store_revision)
	old_reply.status.current_world.name = "STALE HEARTBEAT"
	await _finish_heartbeat(old_reply,old_context)
	var source: Dictionary = COMPILER.templates()[4].duplicate(true)
	source.name = "Acknowledged after heartbeat"
	var committed: Dictionary = workshop.commit_draft(source,"",{"x_m":12.0,"z_m":350.0,"yaw_deg":0.0},workshop.authority.revision,workshop.authority.permission_revision)
	check(committed.ok and travel.store_revision == 1,"new creation acknowledged after old heartbeat was captured")
	travel.heartbeat_at = Time.get_ticks_msec() + 60000
	travel._process(0.0)
	check(travel.connected and not travel.transitioning,"delayed old heartbeat does not falsely disconnect current save")
	check(travel.store_revision == 1 and travel.status.current_world.name == "Home Earth","old heartbeat cannot regress store revision or current metadata")
	check(workshop.authority.snapshot("local_player").instances.values()[0].source.name == source.name,"old heartbeat cannot replace newly acknowledged source")
	check(travel.heartbeat == null,"completed stale thread is reclaimed")

	# Unlike a stale callback, a current-context response showing another writer
	# must stop the client until it reloads the authoritative source.
	var current_context := {"world":travel.world_id,"epoch":travel.presence_epoch,"revision":travel.store_revision}
	var conflict := transport.response("home",transport.home,travel.store_revision + 1)
	await _finish_heartbeat(conflict,current_context)
	travel._process(0.0)
	check(not travel.connected and travel.transitioning,"unexpected current-context store change freezes instead of silently merging")
	check(travel.store_revision == 1,"conflicting heartbeat never blesses an unloaded source revision")
	check(not viewer.player_body.is_physics_processing() and workshop.travel_blocked,"store conflict stops consequential local gameplay")
	check(not FileAccess.file_exists("user://tests/travel_faults_" + unique + ".json"),"fake durable transport creates no competing JSON save")
	viewer.queue_free()
	await process_frame
	await process_frame
	print("Travel adapter faults: %d checks, %d failures" % [checks,failures])
	quit(0 if failures == 0 else 1)
