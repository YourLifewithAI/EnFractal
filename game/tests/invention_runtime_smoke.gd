extends SceneTree
## Uses the real authority, capability interpreter and CharacterBody controller.
## Only the map height provider and viewer shell are fixtures; the floor is real.
const Runtime = preload("res://scripts/invention_runtime.gd")
const Player = preload("res://scripts/player_controller.gd")
const Compiler = preload("res://scripts/creation_compiler.gd")
const SAVE_PATH := "user://tests/manual_invention_runtime.json"

class FlatMap extends RefCounted:
	func surface_height_at(_x: float, _z: float) -> float:
		return 0.0

class TestViewer extends Node3D:
	var manifest := {"id": "runtime-hostile-fixture", "source": "flat-pinned-physics-floor"}
	var map_runtime = FlatMap.new()
	var player_body
	var walking := true

var failures := 0
var checks := 0
var viewer
var runtime


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_remove_test_save()
	viewer = TestViewer.new()
	root.add_child(viewer)
	var floor := StaticBody3D.new()
	var floor_shape := CollisionShape3D.new()
	var floor_box := BoxShape3D.new()
	floor_box.size = Vector3(150, 1, 150)
	floor_shape.shape = floor_box
	floor.position = Vector3(0, -0.5, 350)
	floor.add_child(floor_shape)
	viewer.add_child(floor)
	var player = Player.new()
	player.name = "RealTestPlayer"
	var player_shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.3
	capsule.height = 1.7
	player_shape.shape = capsule
	player.add_child(player_shape)
	viewer.player_body = player
	viewer.add_child(player)
	player.configure(Callable(viewer.map_runtime, "surface_height_at"), 1000.0)
	player.spawn_at(0, 350, 0)
	runtime = Runtime.new()
	runtime.save_path = SAVE_PATH
	runtime.configure(viewer)
	viewer.add_child(runtime)
	await _frames(25)
	if not is_instance_valid(runtime.editor):
		_expect(false, "runtime and editor dependencies initialize without script errors")
		_finish()
		return
	_expect(player.is_on_floor(), "real CharacterBody settles on physics floor")
	_expect(not runtime.authority.snapshot("local_player").consent.local_player, "fresh runtime begins without effect consent")
	_expect(runtime.set_local_consent(true).ok, "player explicitly opts into invention effects")
	var source := _wind_source()
	var before_occupied: int = runtime.authority.revision
	var occupied := _commit(source, "", 0.0, 350.0)
	_expect(not occupied.ok and occupied.get("code") == "occupied_placement" and runtime.authority.revision == before_occupied, "live host clearance blocks a collider overlapping the player")
	occupied = _commit(source, "", 7.0, 346.0)
	_expect(not occupied.ok and occupied.get("code") == "occupied_placement", "live host clearance protects nonconsenting mannequin")
	var placed := _commit(source, "", 1.7, 350.0)
	_expect(placed.get("ok", false), "manual data design commits through runtime and authority")
	if not placed.get("ok", false):
		print(placed)
		_finish()
		return
	var id: String = placed.instance_id
	_expect(runtime.instances.has(id) and runtime.assemblies.has(id), "host instance rendered from compiled source")
	var before_x: float = player.position.x
	_expect(runtime.activate_creation(id).ok, "Use trigger activates capability graph")
	_expect(runtime.fields.size() == 1 and runtime.evaluated_nodes == 2, "reachable graph evaluated once and one field admitted")
	await _frames(35)
	_expect(player.position.x > before_x + 0.08, "compiled wind changes actual CharacterBody movement")
	_expect(player.velocity.length() < 10.0, "creation force remains bounded")
	_expect(runtime.set_local_consent(false).ok, "local player can revoke consent during a field")
	_expect(runtime.fields.is_empty(), "consent change invalidates transient field references")
	_expect(runtime.effect_at("local_player", player.position).acceleration.is_zero_approx(), "revoked consent immediately removes sampled force")
	var after_revoke: float = player.position.x
	await _frames(8)
	_expect(abs(player.position.x - after_revoke) < 0.06, "creation-induced velocity does not coast after consent revocation")
	_expect(not runtime.activate_creation(id).ok, "revoked local player cannot reactivate")
	_expect(runtime.set_local_consent(true).ok, "local player can consent again")
	_expect(runtime.activate_creation(id).ok, "explicit activation can start another bounded lifetime")
	await _frames(130)
	_expect(runtime.fields.is_empty() and player.creation_velocity.is_zero_approx(), "field lifetime expires and induced velocity is cleared")
	await _test_revision_removal_reload(id, source)
	await _test_guest()
	await _test_timer_and_glide()
	await _test_slope_revocation()
	await _test_boundary_motion()
	await _test_corrupt_load_cleanup()
	_finish()


func _test_revision_removal_reload(id: String, source: Dictionary) -> void:
	viewer.player_body.spawn_at(0, 350, 0)
	var old_assembly = runtime.assemblies[id]
	var weak_old: WeakRef = weakref(old_assembly)
	_expect(runtime.activate_creation(id).ok, "field can be active before source revision")
	var edited := source.duplicate(true)
	edited.name = "Revised wind"
	edited.nodes[1].params.direction = [0, 0, 1]
	var result := _commit(edited, id, 1.7, 350.0)
	_expect(result.get("ok", false), "revision uses same host identity with new source: " + str(result))
	if not result.get("ok", false):
		return
	_expect(runtime.fields.is_empty() and runtime.instances[id].revision == 2, "revision clears old effects and advances instance revision")
	await process_frame
	_expect(weak_old.get_ref() == null, "replaced compiled assembly is freed")
	var load_result: Dictionary = runtime.authority.load_saved()
	_expect(load_result.ok, "runtime authority reload succeeds")
	runtime._refresh()
	_expect(runtime.instances[id].source.name == "Revised wind", "reload retains revised editable source")
	_expect(not runtime.authority.can_activate("local_player", id), "reloaded session requires fresh consent")
	runtime.set_local_consent(true)
	var removed: Dictionary = runtime.remove_creation(id, runtime.authority.revision, runtime.authority.permission_revision)
	_expect(removed.ok and not runtime.instances.has(id) and not runtime.assemblies.has(id), "remove unpublishes assembly and effects")
	_expect(not runtime.activate_creation(id).ok, "removed invention cannot activate")
	await _frames(2)


func _test_guest() -> void:
	var source := _wind_source()
	source.nodes[1].params.direction = [0, 1, 0]
	var placed := _commit(source, "", 6.0, 346.0)
	_expect(placed.get("ok", false), "field source can be placed beside mannequin with physical clearance: " + str(placed))
	if not placed.get("ok", false):
		return
	var id: String = placed.instance_id
	_expect(runtime.activate_creation(id).ok, "public field starts near consent mannequin")
	_expect(runtime.effect_at("guest_player", runtime.guest.position).acceleration.is_zero_approx(), "default guest receives no force")
	_expect(runtime.set_guest_consent(true).ok, "test mannequin can explicitly consent")
	_expect(runtime.activate_creation(id).ok, "field can restart after permission revision")
	var before: float = runtime.guest.position.y
	await _frames(25)
	_expect(runtime.guest.position.y > before + 0.03, "consenting mannequin visibly moves under field")
	runtime.set_guest_consent(false)
	_expect(runtime.guest_velocity.is_zero_approx(), "guest revocation clears induced momentum")
	_expect(runtime.effect_at("guest_player", runtime.guest.position).acceleration.is_zero_approx(), "guest revocation blocks next effect sample")
	runtime.set_guest_builder(true)
	runtime.set_guest_consent(true)
	var guest_request := {"op": "place", "action_id": "guest_runtime_creation", "source": source, "x_m": 6.0, "z_m": 346.0, "yaw_deg": 0.0, "expected_revision": runtime.authority.revision, "expected_permission_revision": runtime.authority.permission_revision}
	var guest_placed: Dictionary = runtime.authority.submit("guest_player", guest_request)
	_expect(guest_placed.ok, "guest editor can contribute independent source")
	if not guest_placed.ok:
		return
	runtime._refresh()
	_expect(runtime.activate_creation(guest_placed.instance_id, "guest_player").ok, "guest-owned active invention operates")
	runtime.set_guest_builder(false)
	_expect(runtime.fields.is_empty() and not runtime.instances[guest_placed.instance_id].active, "owner role revocation removes live effects")
	_expect(not runtime.activate_creation(guest_placed.instance_id, "guest_player").ok, "revoked owner cannot operate their disabled invention")
	_expect(runtime.effect_at("guest_player", runtime.guest.position).acceleration.is_zero_approx(), "revoked owner effects remain absent")
	runtime.remove_creation(id, runtime.authority.revision, runtime.authority.permission_revision)
	runtime.remove_creation(guest_placed.instance_id, runtime.authority.revision, runtime.authority.permission_revision)
	await _frames(2)


func _test_timer_and_glide() -> void:
	var source := _wind_source()
	source.name = "Periodic wind"
	source.nodes[0].op = "timer"
	source.nodes[0].params = {"interval_s": 0.5}
	var placed := _commit(source, "", -10.0, 350.0)
	_expect(placed.get("ok", false), "timer design commits: " + str(placed))
	if not placed.get("ok", false):
		return
	var baseline: int = runtime.activation_count
	await _frames(70)
	var fired: int = runtime.activation_count - baseline
	_expect(fired >= 2 and fired <= 4, "timer fires on bounded cadence without catch-up burst")
	runtime.remove_creation(placed.instance_id, runtime.authority.revision, runtime.authority.permission_revision)
	var glider: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://creation_templates/storm_glider.json"))
	for node in glider.nodes:
		if node.op == "wind":
			node.params.direction = [0, -1, 0]
			node.params.acceleration_mps2 = 4.0
			node.params.duration_s = 2.0
	var equipped := _commit(glider, "", 0.0, 350.0)
	_expect(equipped.ok, "composed glider equips through general system")
	if not equipped.ok:
		return
	viewer.player_body.position = Vector3(0, 15, 350)
	viewer.player_body.velocity = Vector3.ZERO
	await _frames(50)
	_expect(viewer.player_body.velocity.y >= -3.1 and viewer.player_body.velocity.y < 0.0, "equipped glide constrains actual descent to source value")
	_expect(runtime.activate_creation(equipped.instance_id).ok, "downwind capability can run alongside passive glide")
	await _frames(30)
	_expect(viewer.player_body.velocity.y >= -3.1 and viewer.player_body.velocity.y < 0.0, "downwind remains constrained by equipped glide")
	var before_revoke: float = viewer.player_body.velocity.y
	runtime.set_local_consent(false)
	_expect(viewer.player_body.velocity.y <= before_revoke + 0.02 and viewer.player_body.velocity.y < 0.0, "revoking clipped downwind cannot add phantom upward velocity")
	await _frames(20)
	_expect(viewer.player_body.velocity.y < -3.1, "revoking consent releases passive glide cap")
	runtime.set_local_consent(true)
	runtime.remove_creation(equipped.instance_id, runtime.authority.revision, runtime.authority.permission_revision)
	viewer.player_body.spawn_at(0, 350, 0)
	await _frames(20)


func _test_boundary_motion() -> void:
	var source := _wind_source()
	source.mount = "avatar"
	source.nodes[1].params.direction = [0, 0, 1]
	source.nodes[1].params.radius_m = 1.0
	var placed := _commit(source, "", 0.0, 350.0)
	_expect(placed.ok, "avatar field compiles with authorized initial footprint")
	if not placed.ok:
		return
	viewer.player_body.position = Vector3(0, 0.9, 379.3)
	viewer.player_body.velocity = Vector3.ZERO
	await _frames(3)
	_expect(runtime.activate_creation(placed.instance_id).ok, "moving avatar invention can trigger near protected boundary")
	await _frames(65)
	_expect(viewer.player_body.position.z > 379.35 and viewer.player_body.position.z < 379.6, "creation-induced movement approaches but cannot overlap protected garden")
	_expect(runtime.effect_at("local_player", Vector3(0, 1, 385)).acceleration.is_zero_approx(), "protected target position receives no force")
	viewer.player_body.last_safe_position = Vector3(0, 0.9, 385)
	viewer.player_body.has_grounded_checkpoint = true
	_expect(viewer.player_body.creation_position_guard.is_valid(), "boundary recovery regression retains an active motion guard")
	viewer.player_body.recover()
	_expect(viewer.player_body.position.z < 380.0 and viewer.player_body.position.z >= 290.0, "recovery during creation motion cannot teleport to a protected checkpoint")
	_expect(viewer.player_body.creation_velocity.is_zero_approx(), "recovery clears invention-induced momentum")
	runtime.remove_creation(placed.instance_id, runtime.authority.revision, runtime.authority.permission_revision)


func _test_slope_revocation() -> void:
	var ramp := StaticBody3D.new()
	var collider := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(8.0, 0.5, 8.0)
	collider.shape = box
	ramp.add_child(collider)
	ramp.position = Vector3(-20, 1, 350)
	ramp.rotation.z = deg_to_rad(10.0)
	viewer.add_child(ramp)
	viewer.player_body.spawn_at(-22, 350, 0)
	viewer.player_body.position.y = 4.0
	await _frames(60)
	_expect(viewer.player_body.is_on_floor() and viewer.player_body.position.y > 1.5, "actual CharacterBody settles on sloped collision surface")
	var placed := _commit(_wind_source(), "", -23.0, 350.0)
	_expect(placed.get("ok", false), "field for sloped contact regression commits")
	if not placed.get("ok", false):
		ramp.queue_free()
		return
	var start: Vector3 = viewer.player_body.position
	_expect(runtime.activate_creation(placed.instance_id).ok, "field activates beside sloped contact")
	await _frames(30)
	_expect(viewer.player_body.position.x > start.x + 0.05, "wind causes tangible movement along slope")
	runtime.set_local_consent(false)
	_expect(viewer.player_body.creation_velocity.is_zero_approx() and Vector2(viewer.player_body.velocity.x, viewer.player_body.velocity.z).length() < 0.2, "slope contact does not retain force after revocation")
	var stopped: Vector3 = viewer.player_body.position
	await _frames(8)
	_expect(abs(viewer.player_body.position.x - stopped.x) < 0.06, "revoked slope force does not leave residual drift")
	runtime.set_local_consent(true)
	runtime.remove_creation(placed.instance_id, runtime.authority.revision, runtime.authority.permission_revision)
	ramp.queue_free()
	viewer.player_body.spawn_at(0, 350, 0)
	await _frames(2)


func _commit(source: Dictionary, id: String, x: float, z: float) -> Dictionary:
	return runtime.commit_draft(source, id, {"x_m": x, "z_m": z, "yaw_deg": 0.0}, runtime.authority.revision, runtime.authority.permission_revision)


func _test_corrupt_load_cleanup() -> void:
	viewer.player_body.spawn_at(0, 350, 0)
	var placed := _commit(_wind_source(), "", -3.0, 350.0)
	_expect(placed.get("ok", false), "create an assembly before corrupt-load regression")
	if not placed.get("ok", false):
		return
	_expect(runtime.activate_creation(placed.instance_id).ok and runtime.fields.size() == 1, "live field exists before failed load")
	var file := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	file.store_string("{invalid")
	file.close()
	_expect(not runtime.authority.load_saved().ok, "runtime's authority fails closed on corrupt save")
	runtime._refresh()
	_expect(runtime.assemblies.is_empty() and runtime.instances.is_empty() and runtime.fields.is_empty() and runtime.animations.is_empty(), "failed authority snapshot removes runtime assemblies and transient effects")
	_expect(viewer.player_body.creation_velocity.is_zero_approx(), "failed load clears actual player external velocity")
	await _frames(2)


func _wind_source() -> Dictionary:
	return {"schema": "enfractal.creation", "version": 1, "name": "Runtime wind", "seed": 1, "mount": "ground", "parts": [{"id": "base", "shape": "box", "position_m": [0, 0.25, 0], "rotation_deg": [0, 0, 0], "size_m": [0.5, 0.5, 0.5], "material": "stone"}], "nodes": [{"id": "use", "op": "interact", "part_id": "base", "params": {}}, {"id": "wind", "op": "wind", "part_id": "base", "params": {"direction": [1, 0, 0], "acceleration_mps2": 4.0, "radius_m": 3.0, "duration_s": 2.0}}], "edges": [{"from": "use", "to": "wind"}]}


func _frames(count: int) -> void:
	for _index in range(count):
		await physics_frame


func _expect(condition: bool, label: String) -> void:
	checks += 1
	if not condition:
		failures += 1
		push_error("Invention runtime check failed: " + label)


func _remove_test_save() -> void:
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SAVE_PATH + ".pending"))


func _finish() -> void:
	Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
	viewer.queue_free()
	_remove_test_save()
	print("Invention runtime: %d checks, %d failures" % [checks, failures])
	quit(1 if failures else 0)
