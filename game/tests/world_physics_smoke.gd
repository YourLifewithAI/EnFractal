extends SceneTree

const PlayerScene = preload("res://scenes/player_test.tscn")
const Profile = preload("res://scripts/world_physics_profile.gd")


func _initialize() -> void:
	call_deferred("_run")


func _flat_height(_x: float, _z: float) -> float:
	return 0.0


func _run() -> void:
	var player: CharacterBody3D = PlayerScene.instantiate()
	root.add_child(player)
	player.configure(Callable(self, "_flat_height"), 1000.0)
	if not player.spawn_at(0.0, 0.0, 0.0):
		_fail("flat-world test spawn failed")
		return
	var invalid: Dictionary = Profile.preset("ridge_breeze_test", 1)
	invalid["wind_x_mps"] = INF
	if player.set_world_physics(invalid) or player.world_physics["revision"] != 0:
		_fail("non-finite wind changed world physics")
		return
	invalid = Profile.preset("ridge_breeze_test", 1)
	invalid["wind_x_mps"] = 20.0
	if player.set_world_physics(invalid):
		_fail("unbounded wind was accepted")
		return
	if not player.set_world_physics(Profile.preset("light_gravity_test", 1)):
		_fail("bounded light-gravity revision was rejected")
		return
	player.global_position = Vector3(0.0, 50.0, 0.0)
	for i in range(30):
		await physics_frame
	var light_fall_speed: float = -player.velocity.y
	if not player.set_world_physics(Profile.preset("earth_test", 2)):
		_fail("bounded Earth revision was rejected")
		return
	player.global_position = Vector3(0.0, 50.0, 0.0)
	player.velocity = Vector3.ZERO
	for i in range(30):
		await physics_frame
	var earth_fall_speed: float = -player.velocity.y
	if earth_fall_speed < light_fall_speed + 5.0:
		_fail("gravity profile did not materially change falling speed")
		return
	if player.set_world_physics(Profile.preset("light_gravity_test", 1)):
		_fail("stale physics revision was accepted")
		return
	if not player.set_world_physics(Profile.preset("ridge_breeze_test", 3)):
		_fail("bounded wind revision was rejected")
		return
	player.global_position = Vector3(0.0, 50.0, 0.0)
	player.velocity = Vector3.ZERO
	for i in range(30):
		await physics_frame
	if player.velocity.x < 3.0 or player.global_position.x < 0.4:
		_fail("airborne wind did not move the player")
		return
	print("World physics smoke passed: revision, gravity, wind, and invalid-profile bounds")
	quit(0)


func _fail(message: String) -> void:
	push_error("World physics smoke: " + message)
	quit(1)
