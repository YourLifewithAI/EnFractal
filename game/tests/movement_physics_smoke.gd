extends SceneTree
## Run with: Godot --headless --path game --script res://tests/movement_physics_smoke.gd


func _initialize() -> void:
	call_deferred("_run")


func _fail(message: String) -> void:
	push_error("Movement smoke test: " + message)
	quit(1)


func _ray_height(viewer: Node3D, x: float, z: float) -> float:
	var from := Vector3(x, 500.0, z)
	var to := Vector3(x, -500.0, z)
	var ray := PhysicsRayQueryParameters3D.create(from, to, 1)
	var hit := viewer.get_world_3d().direct_space_state.intersect_ray(ray)
	if hit.is_empty():
		return INF
	return hit["position"].y


func _run() -> void:
	var viewer = load("res://scenes/main.tscn").instantiate()
	root.add_child(viewer)
	viewer.camera.position = Vector3(0.0, viewer._height_at(0.0, 0.0) + 5.0, 0.0)
	var collider_start_ms := Time.get_ticks_msec()
	viewer._set_walking_mode(true)
	var collider_build_ms := Time.get_ticks_msec() - collider_start_ms
	if not viewer.walking or viewer.terrain_colliders.active_patches.size() != 9:
		_fail("walk mode did not create the 3 × 3 collider ring")
		return
	if viewer.terrain_colliders.source_height_sha256 != viewer.manifest["heights_sha256"] or viewer.terrain_colliders.collision_revision != 0:
		_fail("collider source does not match the pinned map height revision")
		return
	for i in range(3):
		await physics_frame
	var player = viewer.player_body
	for seam_x in [0.0, 128.0]:
		if seam_x > 0.0:
			player.spawn_at(127.4, 25.0, 0.0)
			viewer.terrain_colliders.update_center(player.global_position)
			for i in range(3):
				await physics_frame
		var left := _ray_height(viewer, seam_x - 0.001, 25.0)
		var right := _ray_height(viewer, seam_x + 0.001, 25.0)
		if not is_finite(left) or not is_finite(right) or absf(left - right) > 0.2:
			_fail("terrain collision seam has a gap or height discontinuity at X %.0f: left=%s right=%s" % [seam_x, left, right])
			return
	# The next patch is already resident before crossing X = 128 m.
	if not viewer.terrain_colliders.active_patches.has(Vector2i(34, 32)):
		_fail("neighboring collision patch was not prefetched")
		return
	viewer.terrain_colliders.update_center(Vector3(128.1, 0.0, 0.0))
	if not viewer.terrain_colliders.active_patches.has(Vector2i(35, 32)) or viewer.terrain_colliders.active_patches.size() > 9:
		_fail("collision ring did not move with the player or exceeded its bound")
		return
	player.spawn_at(127.4, 25.0, 0.0)
	viewer.terrain_colliders.update_center(player.global_position)
	for i in range(20):
		await physics_frame
	var move_right := InputEventKey.new()
	move_right.keycode = KEY_D
	move_right.pressed = true
	Input.parse_input_event(move_right)
	for i in range(38):
		await physics_frame
	move_right.pressed = false
	Input.parse_input_event(move_right)
	if player.global_position.x < 128.2 or not player.is_on_floor():
		_fail("player could not cross the active collision patch boundary")
		return
	viewer.terrain_colliders.update_center(Vector3.ZERO)
	player.spawn_at(0.0, 0.0, 0.0)
	var expected_ground: float = viewer._height_at(0.0, 0.0)
	if absf(player.global_position.y - expected_ground - 0.97) > 0.05:
		_fail("player did not spawn at a safe capsule height")
		return
	for i in range(25):
		await physics_frame
	if not player.is_on_floor():
		_fail("player did not settle on source-grid terrain")
		return
	var start_z: float = player.global_position.z
	var move_forward := InputEventKey.new()
	move_forward.keycode = KEY_W
	move_forward.pressed = true
	Input.parse_input_event(move_forward)
	for i in range(30):
		await physics_frame
	if start_z - player.global_position.z < 0.2 or Vector2(player.velocity.x, player.velocity.z).length() > 5.1:
		_fail("bounded grounded walking did not move forward at the walk speed")
		return
	move_forward.pressed = false
	Input.parse_input_event(move_forward)
	for i in range(15):
		await physics_frame
	player.jump_requested = true
	await physics_frame
	if player.velocity.y <= 0.0:
		_fail("jump did not produce upward velocity")
		return
	# A raised fall with Space held must use the glide cap, then recovery must
	# return to the last grounded position instead of leaving a lost avatar.
	player.global_position.y += 20.0
	player.velocity = Vector3.ZERO
	var hold_space := InputEventKey.new()
	hold_space.keycode = KEY_SPACE
	hold_space.pressed = true
	Input.parse_input_event(hold_space)
	for i in range(20):
		await physics_frame
	if player.velocity.y < -7.1:
		_fail("gliding exceeded its downward speed cap")
		return
	player.recover()
	if player.global_position.distance_to(player.last_safe_position) > 0.01:
		_fail("recovery did not return to a safe grounded checkpoint")
		return
	var release_space := InputEventKey.new()
	release_space.keycode = KEY_SPACE
	release_space.pressed = false
	Input.parse_input_event(release_space)
	var steep_x := 1997.0
	var steep_z := 447.0
	var collision_height: float = viewer.map_runtime.surface_height_at(steep_x, steep_z)
	if absf(collision_height - viewer.map_runtime.height_at(steep_x, steep_z)) < 1.0:
		_fail("steep regression point no longer distinguishes triangle and bilinear height")
		return
	viewer._set_walking_mode(false)
	viewer.camera.position = Vector3(steep_x, collision_height + 5.0, steep_z)
	viewer._set_walking_mode(true)
	player = viewer.player_body
	var patch_half: float = viewer.map_side_m * 0.5
	var player_patch := Vector2i(int(floor((player.global_position.x + patch_half) / viewer.terrain_colliders.PATCH_SIDE_M)), int(floor((player.global_position.z + patch_half) / viewer.terrain_colliders.PATCH_SIDE_M)))
	if viewer.terrain_colliders.center_patch != player_patch:
		_fail("collider ring was not centered on the adjusted spawn position")
		return
	for i in range(30):
		await physics_frame
	var steep_ray := _ray_height(viewer, steep_x, steep_z)
	if absf(steep_ray - collision_height) > 0.01 or not player.is_on_floor():
		_fail("steep regression spawn failed to ground: query=%s ray=%s player=%s local_surface=%s velocity=%s" % [collision_height, steep_ray, player.global_position, viewer.map_runtime.surface_height_at(player.global_position.x, player.global_position.z), player.velocity])
		return
	var half: float = viewer.map_side_m * 0.5
	for corner in [[-half, -half, 0, 0], [half, -half, 0, viewer.grid_side - 1], [-half, half, viewer.grid_side - 1, 0], [half, half, viewer.grid_side - 1, viewer.grid_side - 1]]:
		if absf(viewer.map_runtime.surface_height_at(corner[0], corner[1]) - viewer.map_runtime.height_at_grid(corner[2], corner[3])) > 0.001:
			_fail("triangle surface differs from the source grid at an outer map corner")
			return
	if not is_nan(viewer.map_runtime.surface_height_at(half + 0.01, 0.0)):
		_fail("triangle surface answered beyond the map edge")
		return
	viewer._set_walking_mode(false)
	if viewer.walking or viewer.terrain_colliders.active_patches.size() != 0 or not viewer.camera.current:
		_fail("Tab-mode exit did not restore free-fly and release collision patches")
		return
	print("Movement smoke test passed: source-grid seams, bounded ring, spawn, walk, jump, glide, recovery, fly toggle; initial collider ring ", collider_build_ms, " ms (headless diagnostic)")
	quit(0)
