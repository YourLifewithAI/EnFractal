extends SceneTree
## Supplemental rapid traversal of the Barton terrain. First pass starts with
## unrefined visual LOD; second reverses the same route after residency settles.

const ROUTE_FRAMES := 360
const SETTLE_TIMEOUT_MS := 30000
const START := Vector2(0.0, 350.0)
const END := Vector2(1536.0, -400.0)


func _initialize() -> void:
	call_deferred("_run")


func _percentile(samples: Array[float], fraction: float) -> float:
	var index := mini(samples.size() - 1, maxi(0, int(ceil(samples.size() * fraction)) - 1))
	return snappedf(samples[index], 0.01)


func _measure_route(scene, start: Vector2, finish: Vector2) -> Dictionary:
	var samples: Array[float] = []
	var queue_peak := 0
	var build_peak_ms := 0.0
	var commit_peak_ms := 0.0
	var over_33_ms := 0
	var over_50_ms := 0
	var over_100_ms := 0
	var prior_us := Time.get_ticks_usec()
	for frame in range(ROUTE_FRAMES):
		var point := start.lerp(finish, float(frame + 1) / ROUTE_FRAMES)
		scene.camera.position = Vector3(point.x, scene._height_at(point.x, point.y) + 80.0, point.y)
		await RenderingServer.frame_post_draw
		var now_us := Time.get_ticks_usec()
		var interval_ms := float(now_us - prior_us) / 1000.0
		prior_us = now_us
		samples.append(interval_ms)
		queue_peak = maxi(queue_peak, scene.pending_lod.size() + (1 if scene.lod_task_id >= 0 else 0))
		build_peak_ms = maxf(build_peak_ms, scene.lod_last_build_ms)
		commit_peak_ms = maxf(commit_peak_ms, scene.lod_last_commit_ms)
		if interval_ms > 33.3:
			over_33_ms += 1
		if interval_ms > 50.0:
			over_50_ms += 1
		if interval_ms > 100.0:
			over_100_ms += 1
	samples.sort()
	return {
		"samples": samples.size(),
		"p50_ms": _percentile(samples, 0.50),
		"p95_ms": _percentile(samples, 0.95),
		"p99_ms": _percentile(samples, 0.99),
		"max_ms": _percentile(samples, 1.0),
		"intervals_over_33_ms": over_33_ms,
		"intervals_over_50_ms": over_50_ms,
		"intervals_over_100_ms": over_100_ms,
		"peak_lod_queue": queue_peak,
		"peak_worker_build_ms": snappedf(build_peak_ms, 0.01),
		"peak_mesh_commit_ms": snappedf(commit_peak_ms, 0.01)
	}


func _wait_for_residency(scene) -> bool:
	var began_ms := Time.get_ticks_msec()
	var stable := 0
	while stable < 30:
		await RenderingServer.frame_post_draw
		if scene.pending_lod.is_empty() and scene.lod_task_id < 0:
			stable += 1
		else:
			stable = 0
		if Time.get_ticks_msec() - began_ms > SETTLE_TIMEOUT_MS:
			return false
	return true


func _wait_for_coarse_coverage(scene) -> bool:
	var began_ms := Time.get_ticks_msec()
	while scene.coarse_ready_tiles < scene.coarse_total_tiles:
		await RenderingServer.frame_post_draw
		if Time.get_ticks_msec() - began_ms > SETTLE_TIMEOUT_MS:
			return false
	return true


func _run() -> void:
	var path := OS.get_environment("ENFRACTAL_PROBE_ROUTE_METRICS")
	if path.is_empty():
		push_error("Set ENFRACTAL_PROBE_ROUTE_METRICS")
		quit(1)
		return
	Engine.max_fps = 60
	var attach_started_us := Time.get_ticks_usec()
	var scene = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	var attach_ms := float(Time.get_ticks_usec() - attach_started_us) / 1000.0
	var coarse_wait_started_us := Time.get_ticks_usec()
	if not await _wait_for_coarse_coverage(scene):
		push_error("Coarse terrain did not cover the route before traversal")
		quit(1)
		return
	var coarse_wait_ms := float(Time.get_ticks_usec() - coarse_wait_started_us) / 1000.0
	var cold: Dictionary = await _measure_route(scene, START, END)
	if not await _wait_for_residency(scene):
		push_error("Terrain LOD did not settle after first traversal")
		quit(1)
		return
	var warm: Dictionary = await _measure_route(scene, END, START)
	if not await _wait_for_residency(scene):
		push_error("Terrain LOD did not settle after second traversal")
		quit(1)
		return
	var metrics := {
		"route": "local X/Z (0,350) to (1536,-400) and back; camera 80 m above source terrain",
		"frame_cap": 60,
		"synchronous_scene_attach_ms": snappedf(attach_ms, 0.01),
		"coarse_coverage_wait_ms": snappedf(coarse_wait_ms, 0.01),
		"coarse_ready_tiles_before_traversal": scene.coarse_total_tiles,
		"first_traversal_unrefined_lod": cold,
		"second_traversal_after_residency": warm,
		"final_lod_queue": scene.pending_lod.size() + (1 if scene.lod_task_id >= 0 else 0),
		"timing_scope": "Hidden-window frame-post-draw callback intervals after all coarse tiles exist; no present timestamps, GPU-memory attribution, actual gameplay movement, or minimum-device certification. First pass is unrefined visual LOD, not cold disk cache."
	}
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		push_error("Cannot write route metrics")
		quit(1)
		return
	file.store_string(JSON.stringify(metrics, "  ") + "\n")
	file.close()
	print("Engine route probe passed: ", JSON.stringify(metrics))
	quit(0)
