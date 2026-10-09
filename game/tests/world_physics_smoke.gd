extends SceneTree
## Bounded world physics through the real C# body: presets, revisions, gravity, wind and refusal of
## invalid profiles. Gravity changes how long a fall or jump lasts; lighter gravity than the default also lets the body leap higher.
const GUARD = preload("res://tests/kernel_test_guard.gd")
## Fails the suite on any script or engine error (kernel_test_guard.gd).
var guard = GUARD.new()

const Player = preload("res://scripts/native/SmallPlayerController.cs")
const Profile = preload("res://scripts/world_physics_profile.gd")


func _initialize() -> void:
	OS.add_logger(guard)
	call_deferred("_run")


func _run() -> void:
	var floor := StaticBody3D.new()
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(20, 0.1, 20)
	shape.shape = box
	floor.add_child(shape)
	floor.position = Vector3(0, -0.05, 0)
	root.add_child(floor)
	var player = Player.new()
	player.ReadKeyboard = false
	root.add_child(player)
	await _frames(5)
	if player.WorldPhysicsId != Profile.DEFAULT["id"] or not is_equal_approx(player.GravityMps2, Profile.DEFAULT["gravity_mps2"]):
		_fail("body did not start with the default room profile")
		return
	var invalid: Dictionary = Profile.preset("room_breeze_test", 1)
	invalid["wind_x_mps"] = INF
	if player.SetWorldPhysics(invalid) or player.WorldPhysicsRevision != 0:
		_fail("non-finite wind changed world physics")
		return
	invalid = Profile.preset("room_breeze_test", 1)
	invalid["wind_x_mps"] = 20.0
	if player.SetWorldPhysics(invalid):
		_fail("unbounded wind was accepted")
		return
	invalid = Profile.preset("room_tuned", 1)
	invalid["gravity_mps2"] = 0.2
	if player.SetWorldPhysics(invalid):
		_fail("gravity below the room bound was accepted")
		return
	invalid = Profile.preset("room_tuned", 1)
	invalid["authority"] = "player"
	if player.SetWorldPhysics(invalid):
		_fail("a profile with an extra field was accepted")
		return
	if not player.SetWorldPhysics(Profile.preset("room_floaty", 1)):
		_fail("bounded floaty revision was rejected")
		return
	var floaty_fall := await _fall_speed(player)
	if not player.SetWorldPhysics(Profile.preset("room_real", 2)):
		_fail("bounded real-gravity revision was rejected")
		return
	var real_fall := await _fall_speed(player)
	if real_fall < floaty_fall + 2.0:
		_fail("gravity profile did not materially change falling speed (%.2f vs %.2f m/s)" % [floaty_fall, real_fall])
		return
	if player.SetWorldPhysics(Profile.preset("room_floaty", 1)):
		_fail("stale physics revision was accepted")
		return
	if not player.SetWorldPhysics(Profile.preset("room_breeze_test", 3)):
		_fail("bounded wind revision was rejected")
		return
	player.global_position = Vector3(0.0, 2.0, 0.0)
	player.velocity = Vector3.ZERO
	await _frames(30)
	if player.velocity.x < 0.3 or player.global_position.x < 0.08:
		_fail("airborne wind did not move the body (vx=%.3f, x=%.3f)" % [player.velocity.x, player.global_position.x])
		return
	print("World physics smoke passed: C# body, room presets, revision, gravity, wind and invalid-profile bounds")
	quit(guard.exit_code(false))


func _fall_speed(player) -> float:
	player.global_position = Vector3(0.0, 3.0, 0.0)
	player.velocity = Vector3.ZERO
	await _frames(30)
	return -player.velocity.y


func _frames(count: int) -> void:
	for _index in range(count):
		await physics_frame


func _fail(message: String) -> void:
	push_error("World physics smoke: " + message)
	quit(guard.exit_code(true))
