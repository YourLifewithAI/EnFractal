extends SceneTree
## Hidden-window feasibility probe: wait for terrain residency, then sample warm
## main-loop frame intervals. This is not a presented-frame or low-device test.

const SETTLED_FRAMES := 30
const WARMUP_FRAMES := 30
const MEASURED_FRAMES := 180
const SETTLE_TIMEOUT_MS := 30000


func _initialize() -> void:
	call_deferred("_run")


func _percentile(sorted_samples: Array[float], fraction: float) -> float:
	var index := mini(sorted_samples.size() - 1, maxi(0, int(ceil(sorted_samples.size() * fraction)) - 1))
	return snappedf(sorted_samples[index], 0.01)


func _run() -> void:
	var image_path := OS.get_environment("ENFRACTAL_PROBE_CAPTURE")
	var metrics_path := OS.get_environment("ENFRACTAL_PROBE_METRICS")
	if image_path.is_empty() or metrics_path.is_empty():
		push_error("Set ENFRACTAL_PROBE_CAPTURE and ENFRACTAL_PROBE_METRICS")
		quit(1)
		return
	var scene = load("res://scenes/main.tscn").instantiate()
	root.add_child(scene)
	var started_ms := Time.get_ticks_msec()
	var stable_frames := 0
	var settle_frames := 0
	while stable_frames < SETTLED_FRAMES:
		await process_frame
		settle_frames += 1
		if scene.pending_lod.is_empty() and scene.lod_task_id < 0:
			stable_frames += 1
		else:
			stable_frames = 0
		if Time.get_ticks_msec() - started_ms > SETTLE_TIMEOUT_MS:
			push_error("Terrain LOD did not settle within %d ms" % SETTLE_TIMEOUT_MS)
			quit(1)
			return
	var settle_ms := Time.get_ticks_msec() - started_ms
	for frame in range(WARMUP_FRAMES):
		await process_frame
	var samples_ms: Array[float] = []
	var previous_us := Time.get_ticks_usec()
	for frame in range(MEASURED_FRAMES):
		await process_frame
		var now_us := Time.get_ticks_usec()
		samples_ms.append(float(now_us - previous_us) / 1000.0)
		previous_us = now_us
	samples_ms.sort()
	await RenderingServer.frame_post_draw
	var image_result: int = scene.get_viewport().get_texture().get_image().save_png(image_path)
	var metrics := {
		"sample_count": samples_ms.size(),
		"frame_interval_p50_ms": _percentile(samples_ms, 0.50),
		"frame_interval_p95_ms": _percentile(samples_ms, 0.95),
		"frame_interval_p99_ms": _percentile(samples_ms, 0.99),
		"frame_interval_max_ms": _percentile(samples_ms, 1.0),
		"settle_frames": settle_frames,
		"settle_ms": settle_ms,
		"remaining_lod_tiles": scene.pending_lod.size() + (1 if scene.lod_task_id >= 0 else 0),
		"last_lod_build_ms": snappedf(scene.lod_last_build_ms, 0.01),
		"last_lod_commit_ms": snappedf(scene.lod_last_commit_ms, 0.01),
		"discarded_lod_jobs": scene.lod_discarded,
		"viewport_px": [scene.get_viewport().get_visible_rect().size.x, scene.get_viewport().get_visible_rect().size.y],
		"image_saved": image_result == OK,
		"timing_scope": "Hidden-window warm main-loop frame intervals after terrain LOD settles; no moving route, presented-frame capture, GPU-memory attribution, or minimum-device certification."
	}
	var file := FileAccess.open(metrics_path, FileAccess.WRITE)
	if file == null:
		push_error("Could not open engine probe metrics path")
		quit(1)
		return
	file.store_string(JSON.stringify(metrics, "  ") + "\n")
	file.close()
	print("Engine render probe passed: ", JSON.stringify(metrics))
	quit(0 if image_result == OK and metrics["remaining_lod_tiles"] == 0 else 1)
