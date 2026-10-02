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
	viewer.grid_side = int(source.manifest["grid_side"])
	viewer.map_side_m = int(source.manifest["side_m"])
	viewer.sample_spacing_m = int(source.manifest["sample_spacing_m"])
	viewer.tile_side_m = int(source.manifest["tile_side_m"])
	viewer.height_origin_m = float(source.manifest["height_origin_m"])
	viewer.height_min_m = float(source.manifest["height_min_m"])
	viewer.height_max_m = float(source.manifest["height_max_m"])
	viewer.camera = Camera3D.new()
	var tile := Vector2i(0, 0)
	var center: Vector2 = viewer._tile_center(tile.y, tile.x)
	viewer.camera.position = Vector3(center.x, 10.0, center.y)
	var instance := MeshInstance3D.new()
	instance.mesh = viewer._make_tile_mesh(tile.y, tile.x, 32)
	viewer.tiles[tile] = {"instance": instance, "step": 32}
	viewer.pending_lod.push_back(tile)
	var dispatch_started := Time.get_ticks_usec()
	viewer._start_next_lod_job()
	var dispatch_ms := float(Time.get_ticks_usec() - dispatch_started) / 1000.0
	if viewer.lod_task_id < 0 or int(viewer.tiles[tile]["step"]) != 32:
		_fail("Worker dispatch replaced the coarse tile synchronously")
		return
	var active_task_id: int = viewer.lod_task_id
	viewer.pending_lod.push_back(tile)
	viewer._start_next_lod_job()
	if viewer.lod_task_id != active_task_id:
		_fail("More than one terrain worker job started")
		return
	# The same key is now far away. The completed fine mesh must be dropped.
	viewer.camera.position = Vector3(2048.0, 10.0, 2048.0)
	if not _wait_for_worker(viewer):
		_fail("Stale terrain build timed out")
		return
	viewer._service_lod_job()
	if viewer.lod_task_id >= 0 or viewer.lod_job != null or int(viewer.tiles[tile]["step"]) != 32 or viewer.lod_discarded != 1:
		_fail("Stale terrain build was published")
		return
	# Return to this tile and confirm that the valid result is published later.
	viewer.camera.position = Vector3(center.x, 10.0, center.y)
	viewer._refresh_lod_targets()
	viewer._start_next_lod_job()
	if viewer.lod_task_id < 0 or not _wait_for_worker(viewer):
		_fail("Valid terrain build did not finish")
		return
	viewer._service_lod_job()
	if viewer.lod_task_id >= 0 or int(viewer.tiles[tile]["step"]) != 2:
		_fail("Valid fine terrain tile was not committed")
		return
	var arrays := instance.mesh.surface_get_arrays(0)
	if arrays[Mesh.ARRAY_INDEX].size() / 3 != 131072:
		_fail("Committed fine tile lost its geometry")
		return
	print("Visual streaming smoke passed; dispatch %.2f ms, worker build %.2f ms, main commit %.2f ms, stale results %d" % [dispatch_ms, viewer.lod_last_build_ms, viewer.lod_last_commit_ms, viewer.lod_discarded])
	viewer.camera.free()
	viewer.free()
	instance.free()
	quit(0)


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
