extends SceneTree

const Viewer = preload("res://scripts/map_viewer.gd")
const Runtime = preload("res://scripts/map_runtime.gd")


func _initialize() -> void:
	var source = Runtime.new()
	if not source.load_package("res://maps/barton_creek", "barton_creek_v0"):
		_fail(source.last_error)
		return
	var viewer = Viewer.new()
	viewer.map_runtime = source
	viewer.manifest = source.manifest
	viewer.map_side_m = int(source.manifest["side_m"])
	viewer.sample_spacing_m = int(source.manifest["sample_spacing_m"])
	viewer.tile_side_m = int(source.manifest["tile_side_m"])
	viewer.height_origin_m = float(source.manifest["height_origin_m"])
	viewer.height_min_m = float(source.manifest["height_min_m"])
	viewer.height_max_m = float(source.manifest["height_max_m"])
	viewer.camera = Camera3D.new()
	viewer.camera.position = Vector3(0.0, 10.0, 350.0)
	viewer.startup_cover = ColorRect.new()
	viewer.startup_label = Label.new()
	viewer.startup_cover.add_child(viewer.startup_label)
	viewer.add_child(viewer.startup_cover)
	var began := Time.get_ticks_msec()
	viewer._build_initial_tiles()
	if viewer.coarse_total_tiles != 64 or viewer.coarse_ready_tiles != 9:
		_fail("Initial coarse phase did not seed exactly the nearby 3x3 tiles")
		return
	if not viewer.startup_cover.visible:
		_fail("Incomplete terrain was exposed before the base phase finished")
		return
	viewer.lod_resident_budget_bytes = 0
	if viewer.lod_resident_budget_bytes != viewer.coarse_base_budget_floor_bytes or viewer.lod_budget_clamps != 1 or viewer.lod_resident_bytes > viewer.lod_resident_budget_bytes:
		_fail("A below-base startup budget was not clamped to full coarse coverage")
		return
	viewer._refresh_lod_targets()
	if viewer.pending_lod.size() != 55:
		_fail("Missing base tiles were not scheduled ahead of nearby detail")
		return
	# Simulate a portal-scale jump while a base job is running. Base coverage
	# remains useful at the old location, while the next request reprioritizes.
	viewer.camera.position = Vector3(1700.0, 10.0, -1700.0)
	viewer._refresh_lod_targets()
	var first: Vector2i = viewer.pending_lod[0]
	if viewer._tile_center(first.y, first.x).distance_to(Vector2(1700.0, -1700.0)) > 200.0:
		_fail("Coarse queue did not prioritize the destination after a jump")
		return
	viewer._start_next_lod_job()
	viewer.camera.position = Vector3(-1700.0, 10.0, 1700.0)
	viewer._refresh_lod_targets()
	if viewer.pending_lod.has(first):
		_fail("Active base tile was reinserted into the refreshed queue")
		return
	if viewer.lod_task_id < 0 or not _wait_for_worker(viewer):
		_fail("In-flight coarse job timed out during a jump")
		return
	viewer._service_lod_job()
	if int(viewer.tiles[first]["step"]) != 32 or viewer.coarse_ready_tiles != 10:
		_fail("In-flight base result was discarded after a camera jump")
		return
	viewer._refresh_lod_targets()
	var next: Vector2i = viewer.pending_lod[0]
	if viewer._tile_center(next.y, next.x).distance_to(Vector2(-1700.0, 1700.0)) > 200.0:
		_fail("Next coarse request did not follow the new camera location")
		return
	viewer._start_next_lod_job()
	if viewer.lod_job_step != 32 or int(viewer.tiles[viewer.lod_job_key]["step"]) != 0:
		_fail("Detail work overtook unfinished coarse coverage")
		return
	if not _wait_for_worker(viewer):
		_fail("Second coarse worker timed out")
		return
	viewer._service_lod_job()
	var deadline := Time.get_ticks_msec() + 15000
	var peak_base_commit_ms: float = viewer.lod_last_commit_ms
	while viewer.coarse_ready_tiles < viewer.coarse_total_tiles and Time.get_ticks_msec() < deadline:
		viewer._start_next_lod_job()
		if viewer.lod_task_id < 0 or not _wait_for_worker(viewer):
			_fail("Coarse worker did not complete")
			return
		viewer._service_lod_job()
		peak_base_commit_ms = maxf(peak_base_commit_ms, viewer.lod_last_commit_ms)
	var phased_ms := Time.get_ticks_msec() - began
	if viewer.coarse_ready_tiles != 64 or viewer.lod_resident_bytes > viewer.lod_resident_budget_bytes:
		_fail("Coarse coverage or residency ceiling failed")
		return
	if viewer.startup_cover.visible:
		_fail("The loading cover remained after complete coarse coverage")
		return
	for key in viewer.tiles:
		if viewer.tiles[key]["instance"].mesh == null or int(viewer.tiles[key]["step"]) != 32:
			_fail("A map tile remained visually empty after coarse phase")
			return
	viewer.lod_resident_budget_bytes = Viewer.DEFAULT_RESIDENT_TILE_BUDGET_BYTES
	viewer.pending_lod.clear()
	var a := Vector2i(3, 4)
	var b := Vector2i(4, 4)
	var c := Vector2i(5, 4)
	viewer.camera.position = Vector3(-256.0, 10.0, 256.0)
	if not _build_requested(viewer, a):
		_fail("First fine tile did not build")
		return
	var fine_payload: int = int(viewer.tiles[a]["detail_bytes"])
	if fine_payload < 4 * 1024 * 1024:
		_fail("Fine packed payload accounting was unexpectedly small")
		return
	viewer.lod_resident_budget_bytes = viewer.lod_resident_bytes + 1024
	viewer.camera.position = Vector3(128.0, 10.0, 256.0)
	if not _build_requested(viewer, b):
		_fail("Second fine tile did not build")
		return
	if int(viewer.tiles[a]["step"]) != 32 or int(viewer.tiles[b]["step"]) != 2 or viewer.lod_evictions != 1:
		_fail("Farther detail tile was not evicted to preserve the nearer fine tile")
		return
	if viewer.lod_resident_bytes > viewer.lod_resident_budget_bytes or viewer.tiles[a]["instance"].mesh == null:
		_fail("Eviction exceeded the budget or removed coarse coverage")
		return
	viewer.lod_resident_budget_bytes = viewer.lod_resident_bytes
	viewer.camera.position = Vector3(400.0, 10.0, 256.0)
	if not _build_requested(viewer, c):
		_fail("Third fine worker did not finish")
		return
	if int(viewer.tiles[c]["step"]) != 32 or int(viewer.tiles[b]["step"]) != 2 or viewer.lod_rejected_budget != 1:
		_fail("Budget check displaced a nearer tile or accepted an oversized result")
		return
	viewer._refresh_lod_targets()
	if viewer.pending_lod.has(c):
		_fail("Budget-denied tile was immediately rescheduled without a changed camera or budget")
		return
	var peak_detail_commit_ms: float = viewer.lod_last_commit_ms
	viewer.camera.position = Vector3(-128.0, 10.0, 256.0)
	if not _build_requested(viewer, a):
		_fail("Evicted fine tile did not reload")
		return
	peak_detail_commit_ms = maxf(peak_detail_commit_ms, viewer.lod_last_commit_ms)
	if int(viewer.tiles[a]["step"]) != 2 or int(viewer.tiles[b]["step"]) != 32 or viewer.lod_evictions != 2:
		_fail("Reload did not reclaim the farther detail payload")
		return
	if viewer.lod_resident_bytes > viewer.lod_resident_budget_bytes:
		_fail("Fine tile reload exceeded the residency ceiling")
		return
	viewer.lod_resident_budget_bytes = 0
	if viewer.lod_resident_budget_bytes != viewer.coarse_base_budget_floor_bytes or viewer.lod_resident_bytes != viewer.coarse_base_resident_bytes or int(viewer.tiles[a]["step"]) != 32:
		_fail("Reducing the budget after startup did not preserve the coarse floor and evict detail")
		return
	if viewer.lod_budget_clamps != 2 or viewer.lod_evictions != 3:
		_fail("Below-base budget change did not record the clamp and detail eviction")
		return
	print("Terrain residency smoke passed; startup 9/64 in %.1f ms, all coarse in %d ms, peak base commit %.2f ms, detail commit up to %.2f ms, packed base %.2f MiB, fine %.2f MiB, evictions %d, rejections %d, floor clamps %d" % [viewer.coarse_initial_ms, phased_ms, peak_base_commit_ms, peak_detail_commit_ms, float(viewer.coarse_base_resident_bytes) / 1048576.0, float(fine_payload) / 1048576.0, viewer.lod_evictions, viewer.lod_rejected_budget, viewer.lod_budget_clamps])
	viewer.camera.free()
	viewer.free()
	quit(0)


func _build_requested(viewer, key: Vector2i) -> bool:
	viewer.pending_lod.clear()
	viewer.pending_lod.push_back(key)
	viewer._start_next_lod_job()
	if viewer.lod_task_id < 0 or not _wait_for_worker(viewer):
		return false
	viewer._service_lod_job()
	return viewer.lod_task_id < 0


func _wait_for_worker(viewer) -> bool:
	var deadline := Time.get_ticks_msec() + 10000
	while Time.get_ticks_msec() < deadline:
		if WorkerThreadPool.is_task_completed(viewer.lod_task_id):
			return true
		OS.delay_msec(1)
	return false


func _fail(message: String) -> void:
	push_error(message)
	quit(1)
