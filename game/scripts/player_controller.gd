extends CharacterBody3D
## LEGACY FIXTURE BODY (1.7 m, GDScript). Kept only because invention_runtime.gd
## and its tests are wired to this API (configure/spawn_at/creation effects).
## The game's controller is scripts/native/SmallPlayerController.cs. R0 rewires
## the invention runtime to the C# controller and removes this file.

signal recovered(position: Vector3)

const WALK_SPEED_MPS := 5.0
const RUN_SPEED_MPS := 8.0
const GROUND_ACCEL_MPS2 := 26.0
const AIR_ACCEL_MPS2 := 9.0
const JUMP_SPEED_MPS := 7.0
const GLIDE_TERMINAL_SPEED_MPS := 7.0
const FALL_TERMINAL_SPEED_MPS := 40.0
const BODY_HALF_HEIGHT_M := 0.85
const MAP_EDGE_MARGIN_M := 1.0
const MAX_SPAWN_SEARCH_M := 16
const STABLE_FOOTPRINT_DELTA_M := 0.4
const PHYSICS_PROFILE_SCRIPT := preload("res://scripts/world_physics_profile.gd")

var terrain_height: Callable
var map_half_side_m := 0.0
var last_safe_position := Vector3.ZERO
var pending_spawn_position := Vector3.ZERO
var has_grounded_checkpoint := false
var recover_attempts := 0
var active := false
var jump_requested := false
var world_physics: Dictionary = PHYSICS_PROFILE_SCRIPT.DEFAULT.duplicate(true)
var editor_input_blocked := false
var creation_acceleration := Vector3.ZERO
var creation_velocity := Vector3.ZERO
var creation_applied := Vector3.ZERO
var creation_glide_limit := 0.0
var creation_position_guard := Callable()
var spawn_clearance := Callable()


func clear_creation_motion() -> void:
	velocity -= creation_applied
	creation_applied = Vector3.ZERO
	creation_velocity = Vector3.ZERO
	creation_acceleration = Vector3.ZERO
	creation_glide_limit = 0.0
	creation_position_guard = Callable()


func set_creation_effects(acceleration: Vector3, glide_limit: float, position_guard: Callable) -> void:
	if not position_guard.is_valid() or not acceleration.is_finite():
		clear_creation_motion()
		return
	if acceleration.is_zero_approx():
		velocity -= creation_applied
		creation_applied = Vector3.ZERO
		creation_velocity = Vector3.ZERO
	creation_acceleration = acceleration.limit_length(4.0)
	creation_glide_limit = clampf(glide_limit, 0.0, 7.0)
	creation_position_guard = position_guard


func _ready() -> void:
	motion_mode = CharacterBody3D.MOTION_MODE_GROUNDED
	floor_snap_length = 0.4
	floor_max_angle = deg_to_rad(45.0)
	set_physics_process(false)


func configure(height_query: Callable, map_side_m: float) -> void:
	terrain_height = height_query
	map_half_side_m = map_side_m * 0.5


func set_world_physics(profile: Dictionary) -> bool:
	# This is a trusted local fixture seam. Future servers must authorize the
	# source world and activate the revision with its collision/world transition.
	if not PHYSICS_PROFILE_SCRIPT.validate(profile) or int(profile["revision"]) <= int(world_physics["revision"]):
		return false
	world_physics = profile.duplicate(true)
	return true


func spawn_at(x: float, z: float, heading_rad: float, activate_now: bool = true) -> bool:
	clear_creation_motion()
	if not terrain_height.is_valid():
		return false
	x = clampf(x, -map_half_side_m + MAP_EDGE_MARGIN_M, map_half_side_m - MAP_EDGE_MARGIN_M)
	z = clampf(z, -map_half_side_m + MAP_EDGE_MARGIN_M, map_half_side_m - MAP_EDGE_MARGIN_M)
	var spawn_position := _find_stable_spawn(x, z)
	if not is_finite(spawn_position.y):
		spawn_position = _find_stable_spawn(0.0, 0.0)
	if not is_finite(spawn_position.y):
		push_error("No walkable terrain was found near the requested or map-center spawn")
		suspend()
		return false
	global_position = spawn_position
	rotation.y = heading_rad
	velocity = Vector3.ZERO
	jump_requested = false
	pending_spawn_position = global_position
	has_grounded_checkpoint = false
	recover_attempts = 0
	active = activate_now
	set_physics_process(activate_now)
	return true


func activate() -> void:
	active = true
	set_physics_process(true)


func _find_stable_spawn(request_x: float, request_z: float) -> Vector3:
	# Search nearby, rather than placing a capsule on a sheer source-data cliff.
	# A candidate must have a modest height change across the capsule footprint.
	for radius in range(MAX_SPAWN_SEARCH_M + 1):
		for dx in range(-radius, radius + 1):
			var dz: int = radius - abs(dx)
			for sign in [-1, 1]:
				if dz == 0 and sign == 1:
					continue
				var x: float = request_x + dx
				var z: float = request_z + dz * sign
				if x < -map_half_side_m + MAP_EDGE_MARGIN_M or x > map_half_side_m - MAP_EDGE_MARGIN_M or z < -map_half_side_m + MAP_EDGE_MARGIN_M or z > map_half_side_m - MAP_EDGE_MARGIN_M:
					continue
				var ground: float = terrain_height.call(x, z)
				if not is_finite(ground):
					continue
				var stable := true
				for offset in [Vector2(-0.5, 0.0), Vector2(0.5, 0.0), Vector2(0.0, -0.5), Vector2(0.0, 0.5), Vector2(-0.35, -0.35), Vector2(0.35, 0.35), Vector2(-0.35, 0.35), Vector2(0.35, -0.35)]:
					var neighbor: float = terrain_height.call(x + offset.x, z + offset.y)
					if not is_finite(neighbor) or absf(neighbor - ground) > STABLE_FOOTPRINT_DELTA_M:
						stable = false
						break
				if stable:
					var candidate := Vector3(x, ground + BODY_HALF_HEIGHT_M + 0.12, z)
					if (not creation_position_guard.is_valid() or bool(creation_position_guard.call(candidate))) and (not spawn_clearance.is_valid() or bool(spawn_clearance.call(candidate))):
						return candidate
	return Vector3(NAN, NAN, NAN)


func suspend() -> void:
	active = false
	clear_creation_motion()
	velocity = Vector3.ZERO
	jump_requested = false
	set_physics_process(false)


func recover() -> void:
	if not active:
		return
	if creation_position_guard.is_valid():
		# Creation-induced recovery cannot reuse a checkpoint in protected space.
		var checkpoint_clear := not spawn_clearance.is_valid() or bool(spawn_clearance.call(last_safe_position))
		var candidate := last_safe_position if has_grounded_checkpoint and checkpoint_clear and bool(creation_position_guard.call(last_safe_position)) else _find_stable_spawn(global_position.x, global_position.z)
		if candidate.is_finite() and bool(creation_position_guard.call(candidate)):
			global_position = candidate
		clear_creation_motion()
		velocity = Vector3.ZERO
		jump_requested = false
		recovered.emit(global_position)
		return
	if has_grounded_checkpoint:
		var checkpoint := last_safe_position
		if spawn_clearance.is_valid() and not bool(spawn_clearance.call(checkpoint)):
			checkpoint = _find_stable_spawn(checkpoint.x, checkpoint.z)
		if checkpoint.is_finite():
			global_position = checkpoint
	else:
		recover_attempts += 1
		if recover_attempts == 1:
			global_position = pending_spawn_position + Vector3.UP * 0.5
		else:
			var fallback := _find_stable_spawn(0.0, 0.0)
			if not is_finite(fallback.y):
				push_error("No safe terrain is available for player recovery")
				suspend()
				return
			global_position = fallback + Vector3.UP * 0.5
	velocity = Vector3.ZERO
	recovered.emit(global_position)


func _input(event: InputEvent) -> void:
	if active and not editor_input_blocked and event is InputEventKey and event.keycode == KEY_SPACE and event.pressed and not event.echo:
		jump_requested = true


func _physics_process(delta: float) -> void:
	if not active or not terrain_height.is_valid():
		return
	velocity -= creation_applied
	creation_applied = Vector3.ZERO
	if editor_input_blocked:
		jump_requested = false
	var input_direction := Vector3.ZERO
	if not editor_input_blocked and Input.is_key_pressed(KEY_W):
		input_direction -= global_basis.z
	if not editor_input_blocked and Input.is_key_pressed(KEY_S):
		input_direction += global_basis.z
	if not editor_input_blocked and Input.is_key_pressed(KEY_D):
		input_direction += global_basis.x
	if not editor_input_blocked and Input.is_key_pressed(KEY_A):
		input_direction -= global_basis.x
	input_direction.y = 0.0
	input_direction = input_direction.normalized()
	var speed := RUN_SPEED_MPS if Input.is_key_pressed(KEY_SHIFT) else WALK_SPEED_MPS
	var desired := input_direction * speed
	if not is_on_floor():
		desired += Vector3(float(world_physics["wind_x_mps"]), 0.0, float(world_physics["wind_z_mps"]))
		desired = desired.limit_length(RUN_SPEED_MPS + PHYSICS_PROFILE_SCRIPT.MAX_WIND_MPS)
	var acceleration := GROUND_ACCEL_MPS2 if is_on_floor() else AIR_ACCEL_MPS2
	velocity.x = move_toward(velocity.x, desired.x, acceleration * delta)
	velocity.z = move_toward(velocity.z, desired.z, acceleration * delta)

	if is_on_floor():
		if jump_requested:
			velocity.y = JUMP_SPEED_MPS
		else:
			velocity.y = -0.5
	else:
		var gliding := not editor_input_blocked and Input.is_key_pressed(KEY_SPACE) and velocity.y < 0.0
		var gravity := float(world_physics["glide_gravity_mps2"]) if gliding else float(world_physics["gravity_mps2"])
		var terminal_speed := GLIDE_TERMINAL_SPEED_MPS if gliding else FALL_TERMINAL_SPEED_MPS
		velocity.y = maxf(velocity.y - gravity * delta, -terminal_speed)
	# Friendly lift is an explicit game capability: upward acceleration includes
	# weight support. It is not claimed as aerodynamic fluid simulation.
	if creation_acceleration.y > 0.0:
		velocity.y = maxf(velocity.y, 0.0)
	if creation_glide_limit > 0.0:
		velocity.y = maxf(velocity.y, -creation_glide_limit)
	var base_velocity := velocity
	creation_velocity = (creation_velocity + creation_acceleration * delta).limit_length(8.0)
	velocity += creation_velocity
	if creation_glide_limit > 0.0:
		velocity.y = maxf(velocity.y, -creation_glide_limit)
	# Track only the contribution actually applied after the descent limit.
	creation_applied = velocity - base_velocity
	var before_move := global_position
	move_and_slide()
	# Project the external contribution along contacts just as move_and_slide
	# projects total motion. Clearing one changed axis can leave force on slopes.
	for index in range(get_slide_collision_count()):
		var normal := get_slide_collision(index).get_normal()
		if creation_applied.dot(normal) < 0.0:
			creation_applied = creation_applied.slide(normal)
		if creation_velocity.dot(normal) < 0.0:
			creation_velocity = creation_velocity.slide(normal)
	if creation_position_guard.is_valid() and not bool(creation_position_guard.call(global_position)):
		global_position = before_move
		velocity = Vector3.ZERO
		creation_applied = Vector3.ZERO
		creation_velocity = Vector3.ZERO
	jump_requested = false
	var edge := map_half_side_m - MAP_EDGE_MARGIN_M
	if absf(global_position.x) > edge or absf(global_position.z) > edge:
		global_position.x = clampf(global_position.x, -edge, edge)
		global_position.z = clampf(global_position.z, -edge, edge)
		velocity.x = 0.0
		velocity.z = 0.0
	var ground: float = terrain_height.call(global_position.x, global_position.z)
	if global_position.y < ground - 5.0:
		recover()
	elif is_on_floor():
		last_safe_position = global_position
		has_grounded_checkpoint = true
		recover_attempts = 0
