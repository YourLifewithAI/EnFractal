extends Node3D
## Lightweight 3D viewer for the offline-built map package. Geometry is rebuilt
## from one shared height grid, so every terrain LOD uses matching edge heights.

const MAP_PATH := "res://maps/barton_creek/"
const STYLE_PATH := "res://styles/greenbelt_styles.json"
const MAX_SEGMENT_M := 18.0
const MAX_PENDING_LOD_TILES := 64
const INITIAL_COARSE_RADIUS_TILES := 1
const DEFAULT_RESIDENT_TILE_BUDGET_BYTES := 64 * 1024 * 1024
const MAP_RUNTIME_SCRIPT := preload("res://scripts/map_runtime.gd")
const TERRAIN_MESH_JOB_SCRIPT := preload("res://scripts/terrain_mesh_job.gd")
const WORKSHOP_RUNTIME_SCRIPT := preload("res://scripts/workshop_runtime.gd")
const INVENTION_RUNTIME_SCRIPT := preload("res://scripts/invention_runtime.gd")
const TRAVEL_RUNTIME_SCRIPT := preload("res://scripts/travel_runtime.gd")
const PLAYER_TEST_SCENE := preload("res://scenes/player_test.tscn")
const TERRAIN_COLLISION_SCRIPT := preload("res://scripts/terrain_collision_streamer.gd")
const PAINTERLY_GROUND_KIT := preload("res://scripts/painterly_ground_kit.gd")
const PAINTERLY_ASSETS := preload("res://scripts/painterly_assets.gd")
const PAINTERLY_PATCH_CENTER := Vector2(0.0, 350.0)

var manifest: Dictionary
var features: Dictionary
var map_runtime
var workshop_runtime
var invention_runtime
var travel_runtime
var grid_side: int
var map_side_m: int
var sample_spacing_m: int
var tile_side_m: int
var height_origin_m: float
var height_min_m: float
var height_max_m: float
var tiles: Dictionary = {}
var pending_lod: Array[Vector2i] = []
var coarse_total_tiles := 0
var coarse_ready_tiles := 0
var coarse_initial_ms := 0.0
var coarse_base_resident_bytes := 0
var coarse_base_budget_floor_bytes := 0
var _lod_resident_budget_bytes := DEFAULT_RESIDENT_TILE_BUDGET_BYTES
var lod_resident_budget_bytes: int:
	get:
		return _lod_resident_budget_bytes
	set(value):
		_lod_resident_budget_bytes = maxi(value, coarse_base_budget_floor_bytes)
		if value < coarse_base_budget_floor_bytes:
			lod_budget_clamps += 1
		_enforce_resident_budget()
var lod_resident_bytes := 0
var lod_peak_resident_bytes := 0
var lod_evictions := 0
var lod_rejected_budget := 0
var lod_budget_clamps := 0
var lod_residency_revision := 0
var lod_job
var lod_task_id := -1
var lod_job_key := Vector2i(-1, -1)
var lod_job_step := 0
var lod_discarded := 0
var lod_last_build_ms := 0.0
var lod_last_commit_ms := 0.0
var camera: Camera3D
var player_body
var walk_camera: Camera3D
var terrain_colliders
var walking := false
var sunlight: DirectionalLight3D
var world_settings: Environment
var status_label: Label
var startup_cover: ColorRect
var startup_label: Label
var yaw := 0.0
var pitch := -0.5
var lod_timer := 0.0
var terrain_material: ShaderMaterial
var painterly_ground_enabled := false
var styles: Array
var style_index := 2 # Preserve the existing photo-guided viewer look by default.
var style_materials: Dictionary = {}
var capture_frame := 0
var capture_samples_ms: Array[float] = []
var photo_pilot: Dictionary
var mall_exterior_study: Dictionary
var photo_overlay_texture: Texture2D
var greenbelt_center := Vector2.ZERO
var greenbelt_radius := 0.0
var mall_center := Vector2.ZERO
var mall_radius := 0.0


func _ready() -> void:
	map_runtime = MAP_RUNTIME_SCRIPT.new()
	if not map_runtime.load_package(MAP_PATH, "barton_creek_v0"):
		push_error("Map package failed validation: " + map_runtime.last_error)
		get_tree().quit(1)
		return
	manifest = map_runtime.manifest
	features = map_runtime.features
	photo_pilot = JSON.parse_string(FileAccess.get_file_as_string(MAP_PATH + "photo_pilot.json"))
	mall_exterior_study = JSON.parse_string(FileAccess.get_file_as_string(MAP_PATH + "mall_exterior_study.json"))
	var style_catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(STYLE_PATH))
	if manifest.is_empty() or features.is_empty() or photo_pilot.is_empty() or mall_exterior_study.is_empty() or style_catalog.is_empty():
		push_error("Map package is missing or invalid")
		get_tree().quit(1)
		return
	styles = style_catalog.get("styles", [])
	if styles.size() != 3:
		push_error("Greenbelt style study must define three styles")
		get_tree().quit(1)
		return
	var requested_style := OS.get_environment("ENFRACTAL_STYLE")
	for i in range(styles.size()):
		if styles[i]["id"] == requested_style:
			style_index = i
			break
	photo_overlay_texture = load(MAP_PATH + "photo_overlay.png")
	greenbelt_center = Vector2(float(photo_pilot["greenbelt_pilot"]["center_x_m"]), float(photo_pilot["greenbelt_pilot"]["center_z_m"]))
	greenbelt_radius = float(photo_pilot["greenbelt_pilot"]["radius_m"])
	mall_center = Vector2(float(photo_pilot["mall_pilot"]["center_x_m"]), float(photo_pilot["mall_pilot"]["center_z_m"]))
	mall_radius = float(photo_pilot["mall_pilot"]["radius_m"])
	grid_side = int(manifest["grid_side"])
	map_side_m = int(manifest["side_m"])
	sample_spacing_m = int(manifest["sample_spacing_m"])
	tile_side_m = int(manifest["tile_side_m"])
	height_origin_m = float(manifest["height_origin_m"])
	height_min_m = float(manifest["height_min_m"])
	height_max_m = float(manifest["height_max_m"])
	_setup_scene()
	_setup_materials()
	_apply_style()
	_enable_painterly_ground()
	_jump_to_view(OS.get_environment("ENFRACTAL_VIEW"))
	_build_initial_tiles()
	_build_mapped_features()
	_build_parking_markings()
	_build_trees()
	_build_photo_rocks()
	workshop_runtime = WORKSHOP_RUNTIME_SCRIPT.new()
	workshop_runtime.configure(self)
	add_child(workshop_runtime)
	if OS.get_environment("ENFRACTAL_MANUAL_INVENTION") == "1":
		workshop_runtime.set_process(false)
		workshop_runtime.set_process_unhandled_key_input(false)
		workshop_runtime.hint.hide()
		status_label.hide()
		walk_camera.position.y = 0.82
		invention_runtime = INVENTION_RUNTIME_SCRIPT.new()
		invention_runtime.configure(self)
		var override_save := OS.get_environment("ENFRACTAL_INVENTION_SAVE")
		if override_save.begins_with("user://tests/"):
			invention_runtime.save_path = override_save
		add_child(invention_runtime)
		if OS.get_environment("ENFRACTAL_SAVE_TRAVEL") == "1":
			travel_runtime = TRAVEL_RUNTIME_SCRIPT.new()
			travel_runtime.configure(self,invention_runtime)
			add_child(travel_runtime)
	_refresh_lod_targets()
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	if OS.get_environment("ENFRACTAL_START_WALK") == "1" and travel_runtime == null:
		call_deferred("_start_workshop_walk")
	print("Barton Creek map loaded: ", manifest["feature_counts"])


func _setup_scene() -> void:
	camera = Camera3D.new()
	camera.name = "ExplorerCamera"
	camera.current = true
	camera.far = 7500.0
	camera.position = Vector3(0.0, 115.0, 350.0)
	camera.rotation = Vector3(pitch, yaw, 0.0)
	add_child(camera)
	player_body = PLAYER_TEST_SCENE.instantiate()
	add_child(player_body)
	player_body.configure(Callable(map_runtime, "surface_height_at"), float(map_side_m))
	walk_camera = player_body.get_node("TestCamera")
	walk_camera.current = false
	terrain_colliders = TERRAIN_COLLISION_SCRIPT.new()
	terrain_colliders.name = "ActiveTerrainColliders"
	add_child(terrain_colliders)
	terrain_colliders.configure(map_runtime, map_side_m, sample_spacing_m)
	player_body.recovered.connect(terrain_colliders.update_center)

	sunlight = DirectionalLight3D.new()
	sunlight.name = "Sun"
	sunlight.rotation = Vector3(-0.75, -0.65, 0.0)
	sunlight.light_energy = 1.1
	add_child(sunlight)

	var environment := WorldEnvironment.new()
	world_settings = Environment.new()
	world_settings.background_mode = Environment.BG_COLOR
	world_settings.background_color = Color(0.62, 0.76, 0.82)
	world_settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	world_settings.ambient_light_color = Color(0.78, 0.82, 0.78)
	world_settings.ambient_light_energy = 0.8
	environment.environment = world_settings
	add_child(environment)

	var canvas := CanvasLayer.new()
	add_child(canvas)
	status_label = Label.new()
	status_label.position = Vector2(18, 16)
	status_label.add_theme_font_size_override("font_size", 17)
	status_label.add_theme_color_override("font_color", Color(0.98, 0.96, 0.88))
	status_label.add_theme_color_override("font_shadow_color", Color(0.06, 0.11, 0.09))
	status_label.add_theme_constant_override("shadow_offset_x", 1)
	status_label.add_theme_constant_override("shadow_offset_y", 1)
	canvas.add_child(status_label)
	var credit := Label.new()
	credit.anchor_top = 1.0
	credit.anchor_bottom = 1.0
	credit.offset_left = 18
	credit.offset_right = 900
	credit.offset_top = -46
	credit.offset_bottom = -10
	credit.text = "Terrain: USGS 3DEP  •  Features: © OpenStreetMap contributors (ODbL)  •  Trees illustrative\nPhoto cues: Julia Duffy (public domain), Larry D. Moore (CC BY 4.0); mall facade/signs study the 2020 photo"
	credit.add_theme_font_size_override("font_size", 13)
	credit.add_theme_color_override("font_color", Color(0.98, 0.96, 0.88))
	credit.add_theme_color_override("font_shadow_color", Color(0.06, 0.11, 0.09))
	canvas.add_child(credit)
	# The world becomes visible only after every coarse terrain tile exists.
	# Phased startup therefore never exposes temporary holes in the landscape.
	startup_cover = ColorRect.new()
	startup_cover.name = "TerrainLoadingCover"
	startup_cover.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	startup_cover.color = Color(0.12, 0.19, 0.18)
	startup_cover.mouse_filter = Control.MOUSE_FILTER_STOP
	canvas.add_child(startup_cover)
	startup_label = Label.new()
	startup_label.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	startup_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	startup_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	startup_label.add_theme_font_size_override("font_size", 25)
	startup_label.add_theme_color_override("font_color", Color(0.97, 0.94, 0.84))
	startup_cover.add_child(startup_label)


func _setup_materials() -> void:
	terrain_material = ShaderMaterial.new()
	terrain_material.shader = load("res://shaders/photo_terrain.gdshader")
	terrain_material.set_shader_parameter("photo_overlay", photo_overlay_texture)
	terrain_material.set_shader_parameter("map_side_m", float(map_side_m))


func _enable_painterly_ground() -> void:
	var source_material := terrain_material
	var painted: ShaderMaterial = PAINTERLY_GROUND_KIT.make_terrain_material()
	PAINTERLY_GROUND_KIT.copy_terrain_source_parameters(painted, source_material)
	painted.set_shader_parameter("patch_center", PAINTERLY_PATCH_CENTER)
	painted.set_shader_parameter("patch_radius", 35.0)
	painted.set_shader_parameter("patch_blend_m", 10.0)
	terrain_material = painted
	painterly_ground_enabled = true
	for tile in tiles.values():
		var instance: MeshInstance3D = tile["instance"]
		instance.material_override = terrain_material
	_apply_painterly_light()


func _apply_painterly_light() -> void:
	var low_profile := OS.get_environment("ENFRACTAL_ART_PROFILE") == "low"
	get_viewport().msaa_3d = Viewport.MSAA_2X if low_profile else Viewport.MSAA_4X
	sunlight.light_color = Color(1.0, 0.92, 0.81)
	sunlight.light_energy = 0.55
	sunlight.shadow_enabled = true
	sunlight.shadow_opacity = 0.78
	sunlight.directional_shadow_max_distance = 34.0 if low_profile else 52.0
	world_settings.ambient_light_color = Color(0.69, 0.77, 0.86)
	world_settings.ambient_light_energy = 0.48
	world_settings.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	if world_settings.sky == null:
		var sky_paint := ShaderMaterial.new()
		sky_paint.shader = load("res://shaders/painterly_sky.gdshader")
		world_settings.sky = Sky.new()
		world_settings.sky.sky_material = sky_paint
	world_settings.background_mode = Environment.BG_SKY
	# Ordinary distance fog works in Compatibility; no volumetric effect.
	# The first 30 m stays clear while distant source terrain recedes.
	world_settings.fog_enabled = true
	world_settings.fog_light_color = Color(0.65, 0.73, 0.73)
	world_settings.fog_light_energy = 0.8
	world_settings.fog_density = 0.0013
	world_settings.fog_sky_affect = 0.0


func _rgb(rgb: Array) -> Color:
	return Color8(int(rgb[0]), int(rgb[1]), int(rgb[2]))


func _style_material(role: String) -> StandardMaterial3D:
	if style_materials.has(role):
		return style_materials[role]
	var material := _plain_material(_rgb(styles[style_index]["objects"][role]))
	style_materials[role] = material
	return material


func _apply_style() -> void:
	var style: Dictionary = styles[style_index]
	var terrain: Dictionary = style["terrain"]
	var photo: Dictionary = style["photo"]
	terrain_material.set_shader_parameter("low_color", _rgb(terrain["low"]))
	terrain_material.set_shader_parameter("high_color", _rgb(terrain["high"]))
	terrain_material.set_shader_parameter("steep_color", _rgb(terrain["steep"]))
	terrain_material.set_shader_parameter("color_bands", float(terrain["bands"]))
	terrain_material.set_shader_parameter("mottle_strength", float(terrain["mottle"]))
	terrain_material.set_shader_parameter("photo_strength", float(terrain["photo_strength"]))
	terrain_material.set_shader_parameter("limestone_color", _rgb(photo["limestone"]))
	terrain_material.set_shader_parameter("trail_soil_color", _rgb(photo["trail_soil"]))
	terrain_material.set_shader_parameter("pavement_color", _rgb(photo["pavement"]))
	for role in style_materials:
		var material: StandardMaterial3D = style_materials[role]
		material.albedo_color = _rgb(style["objects"][role])
	world_settings.background_color = _rgb(style["sky"])
	world_settings.ambient_light_color = _rgb(style["ambient"])
	sunlight.light_energy = float(style["sun_energy"])
	if painterly_ground_enabled:
		_apply_painterly_light()
	print("Greenbelt style: ", style["id"], " — ", style["name"])


func _height_at_grid(row: int, column: int) -> float:
	return map_runtime.height_at_grid(row, column)


func _height_at(x: float, z: float) -> float:
	# Viewer cameras may fly outside the map. The runtime's public query returns NAN
	# there; clamp only this presentation adapter to preserve its old horizon behavior.
	var half: float = map_side_m * 0.5
	return map_runtime.height_at(clampf(x, -half, half), clampf(z, -half, half))


func _tile_center(row: int, column: int) -> Vector2:
	var half: float = map_side_m * 0.5
	return Vector2(-half + (column + 0.5) * tile_side_m, -half + (row + 0.5) * tile_side_m)


func _target_step(row: int, column: int) -> int:
	var center := _tile_center(row, column)
	var distance := center.distance_to(Vector2(camera.position.x, camera.position.z))
	if distance < 500.0:
		return 2
	if distance < 1400.0:
		return 8
	return 32


func _build_initial_tiles() -> void:
	var started := Time.get_ticks_usec()
	var tile_count := map_side_m / tile_side_m
	var half: float = map_side_m * 0.5
	var focus_column := clampi(int(floor((camera.position.x + half) / tile_side_m)), 0, tile_count - 1)
	var focus_row := clampi(int(floor((camera.position.z + half) / tile_side_m)), 0, tile_count - 1)
	coarse_total_tiles = tile_count * tile_count
	for row in range(tile_count):
		for column in range(tile_count):
			var instance := MeshInstance3D.new()
			instance.name = "Terrain_%d_%d" % [row, column]
			instance.material_override = terrain_material
			add_child(instance)
			var key := Vector2i(column, row)
			tiles[key] = {"instance": instance, "step": 0, "coarse_mesh": null, "base_bytes": 0, "detail_bytes": 0}
			# Keep the launch position walkable visually while the remaining coarse
			# mesh arrays are prepared off-thread and committed one per frame.
			if absi(column - focus_column) <= INITIAL_COARSE_RADIUS_TILES and absi(row - focus_row) <= INITIAL_COARSE_RADIUS_TILES:
				var job = _new_terrain_job()
				job.run(row, column, 32)
				_install_base_mesh(key, _mesh_from_arrays(job.arrays), TERRAIN_MESH_JOB_SCRIPT.packed_array_bytes(job.arrays))
	coarse_initial_ms = float(Time.get_ticks_usec() - started) / 1000.0


func _install_base_mesh(key: Vector2i, mesh: ArrayMesh, packed_bytes: int) -> void:
	var tile: Dictionary = tiles[key]
	var instance: MeshInstance3D = tile["instance"]
	instance.mesh = mesh
	tile["coarse_mesh"] = mesh
	tile["step"] = 32
	tile["base_bytes"] = packed_bytes
	tile["detail_bytes"] = 0
	tiles[key] = tile
	coarse_ready_tiles += 1
	coarse_base_resident_bytes += packed_bytes
	lod_resident_bytes += packed_bytes
	lod_peak_resident_bytes = maxi(lod_peak_resident_bytes, lod_resident_bytes)
	lod_residency_revision += 1
	_raise_base_budget_floor(packed_bytes)
	if startup_cover != null:
		startup_label.text = "Preparing Barton Creek terrain  •  %d / %d" % [coarse_ready_tiles, coarse_total_tiles]
		if coarse_ready_tiles == coarse_total_tiles:
			startup_cover.visible = false


func _account_existing_base(key: Vector2i) -> void:
	# Headless tests and older scenes may seed a coarse tile directly.
	var tile: Dictionary = tiles[key]
	if tile.has("coarse_mesh"):
		return
	var instance: MeshInstance3D = tile["instance"]
	var mesh: ArrayMesh = instance.mesh
	var packed_bytes: int = TERRAIN_MESH_JOB_SCRIPT.packed_array_bytes(mesh.surface_get_arrays(0))
	tile["coarse_mesh"] = mesh
	tile["base_bytes"] = packed_bytes
	tile["detail_bytes"] = 0
	tiles[key] = tile
	coarse_base_resident_bytes += packed_bytes
	lod_resident_bytes += packed_bytes
	lod_peak_resident_bytes = maxi(lod_peak_resident_bytes, lod_resident_bytes)
	lod_residency_revision += 1
	_raise_base_budget_floor(packed_bytes)


func _raise_base_budget_floor(packed_bytes: int) -> void:
	# All stitched coarse tiles in this package share a topology. Reserve the
	# complete base set from the first tile, then never let later observations
	# lower that floor. A future map with a larger tile can raise it safely.
	var tile_count := maxi(coarse_total_tiles, tiles.size())
	coarse_base_budget_floor_bytes = maxi(coarse_base_budget_floor_bytes, maxi(coarse_base_resident_bytes, tile_count * packed_bytes))
	if _lod_resident_budget_bytes < coarse_base_budget_floor_bytes:
		lod_resident_budget_bytes = _lod_resident_budget_bytes


func _make_tile_mesh(tile_row: int, tile_column: int, step_m: int) -> ArrayMesh:
	var job = _new_terrain_job()
	job.run(tile_row, tile_column, step_m)
	return _mesh_from_arrays(job.arrays)


func _new_terrain_job():
	var job = TERRAIN_MESH_JOB_SCRIPT.new()
	job.configure(map_runtime, map_side_m, sample_spacing_m, tile_side_m, height_origin_m, height_min_m, height_max_m)
	return job


func _mesh_from_arrays(arrays: Array) -> ArrayMesh:
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _refresh_lod_targets() -> void:
	pending_lod.clear()
	var missing: Array[Vector2i] = []
	var upgrades: Array[Vector2i] = []
	for key in tiles:
		var step: int = int(tiles[key]["step"])
		if step == 0:
			# A camera refresh must not reinsert the active base job ahead of the
			# other missing tiles; that could turn its finished slot into detail.
			if not (lod_task_id >= 0 and key == lod_job_key and lod_job_step == 32):
				missing.push_back(key)
			continue
		if coarse_ready_tiles < coarse_total_tiles:
			continue
		var target := _target_step(key.y, key.x)
		if target > step:
			# Demotion is a cheap mesh pointer swap. Retain the always-visible
			# coarse mesh and release the large detail payload immediately.
			_evict_detail_tile(key)
			step = 32
		if target < step and not _budget_denial_still_applies(key, target):
			upgrades.push_back(key)
	var by_distance := func(a: Vector2i, b: Vector2i) -> bool:
		return _tile_center(a.y, a.x).distance_to(Vector2(camera.position.x, camera.position.z)) < _tile_center(b.y, b.x).distance_to(Vector2(camera.position.x, camera.position.z))
	missing.sort_custom(by_distance)
	upgrades.sort_custom(by_distance)
	pending_lod.append_array(missing)
	if coarse_ready_tiles == coarse_total_tiles:
		pending_lod.append_array(upgrades)
	if pending_lod.size() > MAX_PENDING_LOD_TILES:
		pending_lod.resize(MAX_PENDING_LOD_TILES)


func _budget_denial_still_applies(key: Vector2i, target: int) -> bool:
	var tile: Dictionary = tiles[key]
	if int(tile.get("denied_step", 0)) != target:
		return false
	if int(tile.get("denied_revision", -1)) != lod_residency_revision or int(tile.get("denied_budget", -1)) != lod_resident_budget_bytes:
		return false
	var anchor: Vector2 = tile["denied_anchor"]
	return anchor.distance_to(Vector2(camera.position.x, camera.position.z)) < tile_side_m * 0.25


func _evict_detail_tile(key: Vector2i) -> void:
	var tile: Dictionary = tiles[key]
	if int(tile["step"]) == 32 or tile.get("coarse_mesh") == null:
		return
	var instance: MeshInstance3D = tile["instance"]
	instance.mesh = tile["coarse_mesh"]
	lod_resident_bytes -= int(tile.get("detail_bytes", 0))
	tile["detail_bytes"] = 0
	tile["step"] = 32
	tiles[key] = tile
	lod_evictions += 1
	lod_residency_revision += 1


func _enforce_resident_budget() -> void:
	if lod_resident_bytes <= _lod_resident_budget_bytes:
		return
	var candidates: Array[Vector2i] = []
	for key in tiles:
		if int(tiles[key]["step"]) != 0 and int(tiles[key]["step"]) != 32:
			candidates.push_back(key)
	if camera != null:
		candidates.sort_custom(func(a: Vector2i, b: Vector2i) -> bool:
			return _tile_center(a.y, a.x).distance_to(Vector2(camera.position.x, camera.position.z)) > _tile_center(b.y, b.x).distance_to(Vector2(camera.position.x, camera.position.z))
		)
	for key in candidates:
		if lod_resident_bytes <= _lod_resident_budget_bytes:
			break
		_evict_detail_tile(key)
	# The base floor makes an over-budget state after all detail is evicted
	# impossible for a valid package. Keep the guard visible if it regresses.
	if lod_resident_bytes > _lod_resident_budget_bytes:
		push_error("Terrain base meshes exceeded the declared resident budget floor")


func _reserve_detail_bytes(key: Vector2i, packed_bytes: int, desired_step: int) -> bool:
	var old_bytes: int = int(tiles[key].get("detail_bytes", 0))
	var projected := lod_resident_bytes - old_bytes + packed_bytes
	if projected <= lod_resident_budget_bytes:
		return true
	var target_distance := _tile_center(key.y, key.x).distance_to(Vector2(camera.position.x, camera.position.z))
	var candidates: Array[Vector2i] = []
	var recoverable := 0
	for other in tiles:
		if other == key or int(tiles[other]["step"]) == 32 or int(tiles[other]["step"]) == 0:
			continue
		if _tile_center(other.y, other.x).distance_to(Vector2(camera.position.x, camera.position.z)) <= target_distance:
			continue
		candidates.push_back(other)
		recoverable += int(tiles[other].get("detail_bytes", 0))
	if projected - recoverable > lod_resident_budget_bytes:
		var tile: Dictionary = tiles[key]
		tile["denied_step"] = desired_step
		tile["denied_revision"] = lod_residency_revision
		tile["denied_budget"] = lod_resident_budget_bytes
		tile["denied_anchor"] = Vector2(camera.position.x, camera.position.z)
		tiles[key] = tile
		lod_rejected_budget += 1
		return false
	candidates.sort_custom(func(a: Vector2i, b: Vector2i) -> bool:
		return _tile_center(a.y, a.x).distance_to(Vector2(camera.position.x, camera.position.z)) > _tile_center(b.y, b.x).distance_to(Vector2(camera.position.x, camera.position.z))
	)
	for other in candidates:
		if projected <= lod_resident_budget_bytes:
			break
		projected -= int(tiles[other].get("detail_bytes", 0))
		_evict_detail_tile(other)
	return true


func _service_lod_job() -> void:
	if lod_task_id < 0 or not WorkerThreadPool.is_task_completed(lod_task_id):
		return
	# The completion check makes this wait nonblocking during normal frames.
	var outcome := WorkerThreadPool.wait_for_task_completion(lod_task_id)
	lod_task_id = -1
	var finished_job = lod_job
	lod_job = null
	if outcome != OK:
		push_error("Terrain worker task could not be joined")
		return
	lod_last_build_ms = float(finished_job.build_us) / 1000.0
	if not tiles.has(lod_job_key):
		lod_discarded += 1
		return
	var current_step: int = int(tiles[lod_job_key]["step"])
	if current_step == 0:
		# Base coverage is useful even when the camera moved during the build.
		var base_started := Time.get_ticks_usec()
		_install_base_mesh(lod_job_key, _mesh_from_arrays(finished_job.arrays), TERRAIN_MESH_JOB_SCRIPT.packed_array_bytes(finished_job.arrays))
		lod_last_commit_ms = float(Time.get_ticks_usec() - base_started) / 1000.0
		if coarse_ready_tiles == coarse_total_tiles:
			_refresh_lod_targets()
		return
	# Large jumps can change the desired LOD while this worker is building.
	# Leave the existing mesh in place and discard that now-stale result.
	if _target_step(lod_job_key.y, lod_job_key.x) != lod_job_step or current_step <= lod_job_step:
		lod_discarded += 1
		return
	_account_existing_base(lod_job_key)
	var packed_bytes: int = TERRAIN_MESH_JOB_SCRIPT.packed_array_bytes(finished_job.arrays)
	if not _reserve_detail_bytes(lod_job_key, packed_bytes, lod_job_step):
		return
	var started := Time.get_ticks_usec()
	var instance: MeshInstance3D = tiles[lod_job_key]["instance"]
	instance.mesh = _mesh_from_arrays(finished_job.arrays)
	lod_resident_bytes += packed_bytes - int(tiles[lod_job_key].get("detail_bytes", 0))
	lod_peak_resident_bytes = maxi(lod_peak_resident_bytes, lod_resident_bytes)
	tiles[lod_job_key]["detail_bytes"] = packed_bytes
	tiles[lod_job_key]["step"] = lod_job_step
	lod_residency_revision += 1
	lod_last_commit_ms = float(Time.get_ticks_usec() - started) / 1000.0


func _start_next_lod_job() -> void:
	if lod_task_id >= 0:
		return
	while not pending_lod.is_empty():
		var key: Vector2i = pending_lod.pop_front()
		if not tiles.has(key):
			continue
		var current_step: int = int(tiles[key]["step"])
		if coarse_ready_tiles < coarse_total_tiles and current_step != 0:
			continue
		var desired := 32 if current_step == 0 else _target_step(key.y, key.x)
		if current_step != 0 and desired >= current_step:
			continue
		if current_step != 0 and _budget_denial_still_applies(key, desired):
			continue
		lod_job = _new_terrain_job()
		lod_job_key = key
		lod_job_step = desired
		lod_task_id = WorkerThreadPool.add_task(Callable(lod_job, "run").bind(key.y, key.x, desired), false, "Enfractal terrain tile")
		if lod_task_id < 0:
			lod_job = null
			push_error("Terrain worker task could not be started")
		return


func _exit_tree() -> void:
	if lod_task_id >= 0:
		WorkerThreadPool.wait_for_task_completion(lod_task_id)
		lod_task_id = -1
		lod_job = null


func _plain_material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.roughness = 1.0
	return material


func _multimesh_instances(name: String, mesh: Mesh, material: Material, transforms: Array) -> void:
	if transforms.is_empty():
		return
	var multi := MultiMesh.new()
	multi.transform_format = MultiMesh.TRANSFORM_3D
	multi.mesh = mesh
	multi.instance_count = transforms.size()
	for i in range(transforms.size()):
		multi.set_instance_transform(i, transforms[i])
	var instance := MultiMeshInstance3D.new()
	instance.name = name
	instance.multimesh = multi
	instance.material_override = material
	add_child(instance)


func _build_draped_lines(name: String, items: Array, width_m: float, role: String, y_offset: float) -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for item in items:
		var points: Array = item["points"]
		var bridge_height := 0.0
		if item["tags"].get("bridge", "") in ["yes", "viaduct"]:
			bridge_height = 4.0 * clampf(float(item["tags"].get("layer", "1")), 1.0, 3.0)
		for i in range(points.size() - 1):
			var a := Vector2(float(points[i][0]), float(points[i][1]))
			var b := Vector2(float(points[i+1][0]), float(points[i+1][1]))
			var length := a.distance_to(b)
			if length < 0.1:
				continue
			var sections := maxi(1, int(ceil(length / MAX_SEGMENT_M)))
			for section in range(sections):
				var start := a.lerp(b, float(section) / sections)
				var end := a.lerp(b, float(section+1) / sections)
				var direction := (end - start).normalized()
				var perpendicular := Vector2(-direction.y, direction.x) * (width_m * 0.5)
				var t0 := (float(i) + float(section) / sections) / maxf(1.0, points.size() - 1)
				var t1 := (float(i) + float(section + 1) / sections) / maxf(1.0, points.size() - 1)
				var y0 := _height_at(start.x, start.y) + y_offset + bridge_height * sin(PI * t0)
				var y1 := _height_at(end.x, end.y) + y_offset + bridge_height * sin(PI * t1)
				var base := vertices.size()
				vertices.append_array(PackedVector3Array([
					Vector3(start.x + perpendicular.x, y0, start.y + perpendicular.y),
					Vector3(start.x - perpendicular.x, y0, start.y - perpendicular.y),
					Vector3(end.x + perpendicular.x, y1, end.y + perpendicular.y),
					Vector3(end.x - perpendicular.x, y1, end.y - perpendicular.y),
				]))
				normals.append_array(PackedVector3Array([Vector3.UP, Vector3.UP, Vector3.UP, Vector3.UP]))
				indices.append_array(PackedInt32Array([base, base+2, base+1, base+1, base+2, base+3]))
	if vertices.is_empty():
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var material := _style_material(role)
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	var instance := MeshInstance3D.new()
	instance.name = name
	instance.mesh = mesh
	instance.material_override = material
	add_child(instance)


func _build_mapped_features() -> void:
	var cube := BoxMesh.new()
	cube.size = Vector3.ONE
	var highways := []
	var streets := []
	for road in features["roads"]:
		if road["tags"].get("service", "") == "parking_aisle":
			var in_mall_pilot := false
			for point in road["points"]:
				if Vector2(float(point[0]), float(point[1])).distance_squared_to(mall_center) < mall_radius * mall_radius:
					in_mall_pilot = true
					break
			if in_mall_pilot:
				continue # The mapped pavement area already covers these aisles.
		if road["tags"].get("highway", "") in ["motorway", "trunk", "primary", "secondary", "motorway_link", "trunk_link", "primary_link", "secondary_link"]:
			highways.append(road)
		else:
			streets.append(road)
	_build_draped_lines("Highways", highways, 13.0, "highway", 0.4)
	_build_draped_lines("Streets", streets, 5.0, "street", 0.25)
	_build_draped_lines("Trails", features["trails"], 1.7, "trail", 0.22)
	_build_draped_lines("Creeks", features["waterways"], 3.5, "creek", 0.28)
	_build_water_areas()
	var buildings := []
	var landmarks := []
	var mall_landmarks := []
	for building in features["buildings"]:
		var proxy: Dictionary = building["proxy"]
		if not building["name"].is_empty() and float(proxy["width_m"]) * float(proxy["length_m"]) >= 4000.0:
			if building["name"] == "Barton Creek Square Mall":
				mall_landmarks.append(building)
			else:
				landmarks.append(building)
			continue
		var height: float = float(building["height_m"])
		var x: float = float(proxy["x"])
		var z: float = float(proxy["z"])
		var basis := Basis(Vector3.UP, float(proxy["yaw_rad"])).scaled(Vector3(float(proxy["width_m"]), height, float(proxy["length_m"])))
		buildings.append(Transform3D(basis, Vector3(x, _height_at(x,z) + height * 0.5, z)))
	_multimesh_instances("Building proxies", cube, _style_material("building"), buildings)
	_build_landmark_buildings(landmarks, "Other mapped landmarks", "building")
	_build_landmark_buildings(mall_landmarks, "Barton Creek Square outline", "mall")
	_build_mall_glazing(mall_landmarks)
	_build_mall_exterior_study()


func _build_landmark_buildings(items: Array, name: String, role: String) -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for building in items:
		var ring := PackedVector2Array()
		for point in building["footprint"]:
			ring.push_back(Vector2(float(point[0]), float(point[1])))
		if ring.size() > 2 and ring[0].distance_to(ring[ring.size()-1]) < 0.01:
			ring.remove_at(ring.size()-1)
		if ring.size() < 3:
			continue
		var triangles := Geometry2D.triangulate_polygon(ring)
		if triangles.is_empty():
			continue
		var ground_sum := 0.0
		for point in ring:
			ground_sum += _height_at(point.x, point.y)
		var roof_y: float = ground_sum / ring.size() + float(building["height_m"])
		var roof_base := vertices.size()
		for point in ring:
			vertices.push_back(Vector3(point.x, roof_y, point.y))
			normals.push_back(Vector3.UP)
		for index in triangles:
			indices.push_back(roof_base + index)
		for i in range(ring.size()):
			var a := ring[i]
			var b := ring[(i+1) % ring.size()]
			var edge := b - a
			var wall_normal := Vector3(edge.y, 0, -edge.x).normalized()
			var base := vertices.size()
			vertices.append_array(PackedVector3Array([
				Vector3(a.x, _height_at(a.x,a.y), a.y),
				Vector3(b.x, _height_at(b.x,b.y), b.y),
				Vector3(a.x, roof_y, a.y),
				Vector3(b.x, roof_y, b.y),
			]))
			normals.append_array(PackedVector3Array([wall_normal, wall_normal, wall_normal, wall_normal]))
			indices.append_array(PackedInt32Array([base, base+2, base+1, base+1, base+2, base+3]))
	if vertices.is_empty():
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var material := _style_material(role)
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	var instance := MeshInstance3D.new()
	instance.name = name
	instance.mesh = mesh
	instance.material_override = material
	add_child(instance)


func _build_mall_glazing(items: Array) -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for building in items:
		var ring := PackedVector2Array()
		for point in building["footprint"]:
			ring.push_back(Vector2(float(point[0]), float(point[1])))
		if ring.size() > 2 and ring[0].distance_to(ring[ring.size()-1]) < 0.01:
			ring.remove_at(ring.size()-1)
		var center_x := float(building["proxy"]["x"])
		var west_limit := center_x - float(building["proxy"]["width_m"]) * 0.18
		var roof_height := float(building["height_m"])
		for i in range(ring.size()):
			var a := ring[i]
			var b := ring[(i+1) % ring.size()]
			var edge := b - a
			var length := edge.length()
			if length < 12.0 or (a.x + b.x) * 0.5 > west_limit:
				continue
			var outward := Vector2(-1.0, 0.0)
			var direction := edge / length
			var sections := int(floor(length / 10.0))
			for section in range(sections):
				var start := a + direction * (section * 10.0 + 1.4) + outward * 0.25
				var end := a + direction * (section * 10.0 + 8.6) + outward * 0.25
				var bottom_a := _height_at(start.x, start.y) + 1.1
				var bottom_b := _height_at(end.x, end.y) + 1.1
				var top_a := _height_at(start.x, start.y) + minf(roof_height * 0.72, 4.8)
				var top_b := _height_at(end.x, end.y) + minf(roof_height * 0.72, 4.8)
				var base := vertices.size()
				vertices.append_array(PackedVector3Array([
					Vector3(start.x, bottom_a, start.y), Vector3(end.x, bottom_b, end.y),
					Vector3(start.x, top_a, start.y), Vector3(end.x, top_b, end.y),
				]))
				normals.append_array(PackedVector3Array([Vector3.LEFT, Vector3.LEFT, Vector3.LEFT, Vector3.LEFT]))
				indices.append_array(PackedInt32Array([base, base+2, base+1, base+1, base+2, base+3]))
	if vertices.is_empty():
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var material := _style_material("glass")
	material.metallic = 0.15
	material.roughness = 0.35
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	var instance := MeshInstance3D.new()
	instance.name = "Illustrative west mall glazing"
	instance.mesh = mesh
	instance.material_override = material
	add_child(instance)


func _build_mall_exterior_study() -> void:
	# These independent components can be revised without changing the mapped footprint.
	# Their placement and heights are visual interpretation of the credited 2020 photo.
	for volume in mall_exterior_study["volumes"]:
		var a := Vector2(float(volume["front_a"][0]), float(volume["front_a"][1]))
		var b := Vector2(float(volume["front_b"][0]), float(volume["front_b"][1]))
		var edge := b - a
		var length := edge.length()
		var direction := edge / length
		var outward := Vector2(-direction.y, direction.x)
		if outward.x > 0.0:
			outward = -outward
		var depth := float(volume["depth_m"])
		var yaw_rad := -atan2(direction.y, direction.x)
		var front_mid := (a + b) * 0.5
		var body_center := front_mid - outward * (depth * 0.5 - 0.1)
		var ground_a := _height_at(a.x, a.y)
		var ground_b := _height_at(b.x, b.y)
		var base_y := minf(ground_a, ground_b) - 2.0
		var roof_y := (ground_a + ground_b) * 0.5 + float(volume["height_m"])
		var body_height := roof_y - base_y
		var stem := "Illustrative mall %s" % volume["id"]
		_add_mall_box(stem + " mass", Vector3(body_center.x, base_y + body_height * 0.5, body_center.y), Vector3(length, body_height, depth), yaw_rad, volume["body_role"])
		_add_mall_box(stem + " parapet", Vector3(body_center.x, roof_y, body_center.y), Vector3(length + 0.8, 0.9, depth + 0.8), yaw_rad, "mall_trim")

		var entrance := a.lerp(b, float(volume["entrance_fraction"]))
		var entry_ground := _height_at(entrance.x, entrance.y)
		var entry_height := float(volume["entrance_height_m"])
		var entry_width := float(volume["entrance_width_m"])
		var front := entrance + outward * 0.28
		_add_mall_box(stem + " glazed entrance", Vector3(front.x, entry_ground + entry_height * 0.5, front.y), Vector3(entry_width, entry_height, 0.3), yaw_rad, "glass")
		var frame_front := entrance + outward * 0.53
		for side in [-1.0, 1.0]:
			var post: Vector2 = frame_front + direction * (float(side) * (entry_width * 0.5 + 0.6))
			_add_mall_box(stem + " entry pier", Vector3(post.x, entry_ground + entry_height * 0.5, post.y), Vector3(1.2, entry_height + 0.6, 0.75), yaw_rad, "mall_trim")
		_add_mall_box(stem + " entry lintel", Vector3(frame_front.x, entry_ground + entry_height + 0.25, frame_front.y), Vector3(entry_width + 2.5, 0.7, 0.85), yaw_rad, "mall_trim")
		for section in range(1, 4):
			var mullion := front + direction * ((float(section) / 4.0 - 0.5) * entry_width) + outward * 0.18
			_add_mall_box(stem + " glass mullion", Vector3(mullion.x, entry_ground + entry_height * 0.5, mullion.y), Vector3(0.18, entry_height, 0.2), yaw_rad, "mall_trim")
		var sign_position := entrance + outward * 0.9
		var sign := Label3D.new()
		sign.name = stem + " historical sign"
		sign.text = volume["sign"]
		sign.font_size = 128
		sign.pixel_size = 0.03
		sign.billboard = BaseMaterial3D.BILLBOARD_DISABLED
		sign.double_sided = true
		sign.modulate = _rgb(volume["sign_color"])
		sign.position = Vector3(sign_position.x, roof_y - 2.35, sign_position.y)
		sign.rotation.y = yaw_rad if Vector2(-direction.y, direction.x).dot(outward) > 0.0 else yaw_rad + PI
		add_child(sign)
	_build_mall_parking_study()


func _build_mall_parking_study() -> void:
	var trunk_mesh := CylinderMesh.new()
	trunk_mesh.top_radius = 0.11
	trunk_mesh.bottom_radius = 0.22
	trunk_mesh.height = 1.0
	trunk_mesh.radial_segments = 5
	var canopy_mesh := SphereMesh.new()
	canopy_mesh.radius = 1.0
	canopy_mesh.height = 2.0
	canopy_mesh.radial_segments = 6
	canopy_mesh.rings = 3
	var pole_mesh := CylinderMesh.new()
	pole_mesh.top_radius = 0.08
	pole_mesh.bottom_radius = 0.16
	pole_mesh.height = 1.0
	pole_mesh.radial_segments = 5
	var trunks := []
	var canopies := []
	var poles := []
	for tree in mall_exterior_study["parking_trees"]:
		var x := float(tree[0])
		var z := float(tree[1])
		var height := float(tree[2])
		var ground := _height_at(x, z)
		var trunk_height := height * 0.46
		trunks.append(Transform3D(Basis.IDENTITY.scaled(Vector3(1.0, trunk_height, 1.0)), Vector3(x, ground + trunk_height * 0.5, z)))
		canopies.append(Transform3D(Basis.IDENTITY.scaled(Vector3(height * 0.23, height * 0.29, height * 0.23)), Vector3(x, ground + height * 0.72, z)))
	for pole in mall_exterior_study["light_poles"]:
		var x := float(pole[0])
		var z := float(pole[1])
		var ground := _height_at(x, z)
		poles.append(Transform3D(Basis.IDENTITY.scaled(Vector3(1.0, 11.0, 1.0)), Vector3(x, ground + 5.5, z)))
		_add_mall_box("Illustrative parking light", Vector3(x + 0.65, ground + 11.0, z), Vector3(1.3, 0.32, 0.45), 0.0, "mall_trim")
	_multimesh_instances("Illustrative mall parking tree trunks", trunk_mesh, _style_material("trunk"), trunks)
	_multimesh_instances("Illustrative mall parking tree canopies", canopy_mesh, _style_material("canopy"), canopies)
	_multimesh_instances("Illustrative mall parking light poles", pole_mesh, _style_material("mall_trim"), poles)


func _add_mall_box(name: String, center: Vector3, size: Vector3, yaw_rad: float, role: String) -> void:
	var box := BoxMesh.new()
	box.size = size
	var instance := MeshInstance3D.new()
	instance.name = name
	instance.mesh = box
	instance.material_override = _style_material(role)
	instance.position = center
	instance.rotation.y = yaw_rad
	add_child(instance)


func _build_water_areas() -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for area in features["water_areas"]:
		var ring := PackedVector2Array()
		for point in area["outline"]:
			ring.push_back(Vector2(float(point[0]), float(point[1])))
		if ring.size() > 2 and ring[0].distance_to(ring[ring.size()-1]) < 0.01:
			ring.remove_at(ring.size()-1)
		if ring.size() < 3:
			continue
		var triangles := Geometry2D.triangulate_polygon(ring)
		if triangles.is_empty():
			continue
		var elevation := 0.0
		for point in ring:
			elevation += _height_at(point.x, point.y)
		elevation = elevation / ring.size() + 0.4
		var base := vertices.size()
		for point in ring:
			vertices.push_back(Vector3(point.x, elevation, point.y))
			normals.push_back(Vector3.UP)
		for index in triangles:
			indices.push_back(base + index)
	if vertices.is_empty():
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var material := _style_material("water")
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	var instance := MeshInstance3D.new()
	instance.name = "Mapped water areas"
	instance.mesh = mesh
	instance.material_override = material
	add_child(instance)


func _build_trees() -> void:
	var trunk_mesh := CylinderMesh.new()
	trunk_mesh.top_radius = 0.11
	trunk_mesh.bottom_radius = 0.22
	trunk_mesh.height = 1.0
	trunk_mesh.radial_segments = 5
	var canopy_mesh := SphereMesh.new()
	canopy_mesh.radius = 1.0
	canopy_mesh.height = 2.0
	canopy_mesh.radial_segments = 6
	canopy_mesh.rings = 3
	var trunks := []
	var canopies := []
	var pilot_canopies := []
	var authored_surround_count := 0
	for tree in features["trees"]:
		var x: float = float(tree["x"])
		var z: float = float(tree["z"])
		# The v0 points are representative illustrations. Inside the authored
		# proof patch, the versioned oak/juniper composition supplies that layer.
		if painterly_ground_enabled and Vector2(x, z).distance_to(PAINTERLY_PATCH_CENTER) < 31.0:
			continue
		var base_y := _height_at(x,z)
		var height: float = float(tree["height_m"])
		# Replace only the bounded pilot surroundings. These source points are
		# already labeled representative vegetation, never surveyed tree IDs.
		if painterly_ground_enabled and Vector2(x, z).distance_to(PAINTERLY_PATCH_CENTER) < 145.0 and authored_surround_count < 36:
			var species := "juniper" if authored_surround_count % 4 == 1 else "oak"
			var original_tree := PAINTERLY_ASSETS.make_tree(species, false)
			original_tree.name = "IllustrativeSurroundTree_%d" % authored_surround_count
			original_tree.position = Vector3(x, base_y - 0.07, z)
			original_tree.rotation.y = fposmod(x * 0.73 + z * 0.41, TAU)
			original_tree.scale = Vector3.ONE * clampf(height / 7.5, 0.68, 1.3)
			original_tree.set_meta("collision_scope", "visual surround; only the central study has trunk contacts")
			add_child(original_tree)
			authored_surround_count += 1
			continue
		var trunk_height := height * 0.48
		trunks.append(Transform3D(Basis.IDENTITY.scaled(Vector3(1.0, trunk_height, 1.0)), Vector3(x, base_y + trunk_height * 0.5, z)))
		var canopy := Transform3D(Basis.IDENTITY.scaled(Vector3(height * 0.21, height * 0.30, height * 0.21)), Vector3(x, base_y + height * 0.73, z))
		if Vector2(x,z).distance_squared_to(greenbelt_center) < greenbelt_radius * greenbelt_radius:
			pilot_canopies.append(canopy)
		else:
			canopies.append(canopy)
	_multimesh_instances("Tree trunks", trunk_mesh, _style_material("trunk"), trunks)
	_multimesh_instances("Illustrative canopies", canopy_mesh, _style_material("canopy"), canopies)
	_multimesh_instances("Photo-informed pilot canopies", canopy_mesh, _style_material("pilot_canopy"), pilot_canopies)
	set_meta("authored_surround_tree_count", authored_surround_count)


func _build_photo_rocks() -> void:
	var rock_mesh := SphereMesh.new()
	rock_mesh.radius = 0.5
	rock_mesh.height = 1.0
	rock_mesh.radial_segments = 7
	rock_mesh.rings = 4
	var shelf_mesh := CylinderMesh.new()
	shelf_mesh.top_radius = 0.50
	shelf_mesh.bottom_radius = 0.62
	shelf_mesh.height = 1.0
	shelf_mesh.radial_segments = 7
	var bank_stones := []
	var shelf_stones := []
	for rock in photo_pilot["rock_instances"]:
		var x := float(rock["x"])
		var z := float(rock["z"])
		var height := float(rock["height_m"])
		var basis := Basis(Vector3.UP, float(rock["yaw_rad"])).scaled(Vector3(float(rock["length_m"]), height, float(rock["width_m"])))
		var placement := Transform3D(basis, Vector3(x, _height_at(x,z) + height * 0.2, z))
		if rock["kind"] == "illustrative_shelf":
			shelf_stones.append(placement)
		else:
			bank_stones.append(placement)
	var rock_material := _style_material("rock")
	_multimesh_instances("Illustrative Greenbelt bank stones", rock_mesh, rock_material, bank_stones)
	_multimesh_instances("Illustrative Greenbelt limestone shelf", shelf_mesh, rock_material, shelf_stones)


func _build_parking_markings() -> void:
	var items := []
	for marking in photo_pilot["parking_markings"]:
		items.append({"points": [[marking[0], marking[1]], [marking[2], marking[3]]], "tags": {}})
	_build_draped_lines("Schematic mall parking markings", items, 0.13, "parking", 0.46)


func _jump_to_view(view: String) -> void:
	camera.fov = 75.0
	match view:
		"pin":
			camera.position = Vector3(0.0, _height_at(0, 350) + 55.0, 350.0)
			yaw = 0.0
			pitch = -0.5
		"greenbelt":
			camera.position = Vector3(-130.0, _height_at(-130, 280) + 48.0, 280.0)
			yaw = 0.52
			pitch = -0.42
		"greenbelt_study":
			camera.position = Vector3(-155.0, _height_at(-155, 225) + 20.0, 225.0)
			camera.look_at(Vector3(-250.0, _height_at(-250, 105) + 4.0, 105.0), Vector3.UP)
			yaw = camera.rotation.y
			pitch = camera.rotation.x
		"mall":
			camera.position = Vector3(65.0, _height_at(65, -820) + 8.0, -820.0)
			camera.fov = 58.0
			camera.look_at(Vector3(175.0, _height_at(175, -815) + 7.0, -815.0), Vector3.UP)
			yaw = camera.rotation.y
			pitch = camera.rotation.x
		_:
			return
	camera.rotation = Vector3(pitch, yaw, 0.0)
	if not tiles.is_empty():
		_refresh_lod_targets()


func _set_walking_mode(enable: bool) -> void:
	if walking == enable:
		return
	if not enable and invention_runtime != null and invention_runtime.third_person:
		invention_runtime.set_third_person(false)
	if enable:
		if not player_body.spawn_at(camera.position.x, camera.position.z, yaw, false):
			return
		terrain_colliders.activate(player_body.global_position)
		player_body.activate()
		walk_camera.rotation.x = pitch
		camera.current = false
		walk_camera.current = true
		walking = true
	else:
		player_body.suspend()
		camera.position = walk_camera.global_position
		yaw = player_body.rotation.y
		pitch = walk_camera.rotation.x
		camera.rotation = Vector3(pitch, yaw, 0.0)
		walk_camera.current = false
		camera.current = true
		terrain_colliders.deactivate()
		walking = false


func _start_workshop_walk() -> void:
	# A direct walking-height entry into the authored edit plot. The geographic
	# terrain and saved structured workshop still initialize through _ready().
	var eye := Vector2(-5.0, 337.0)
	var focus := Vector2(0.0, 350.0)
	camera.position = Vector3(eye.x, _height_at(eye.x, eye.y) + 1.67, eye.y)
	camera.look_at(Vector3(focus.x, _height_at(focus.x, focus.y) + 1.67, focus.y))
	yaw = camera.rotation.y
	pitch = camera.rotation.x
	_set_walking_mode(true)


func _input(event: InputEvent) -> void:
	if travel_runtime != null and travel_runtime.panel_open:
		if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
			travel_runtime.panel._unhandled_input(event)
		elif event is InputEventKey and event.pressed and event.keycode == KEY_T:
			travel_runtime.panel.close_panel()
			get_viewport().set_input_as_handled()
		return
	if travel_runtime != null and travel_runtime.transitioning:
		return
	if invention_runtime != null and invention_runtime.editor_open:
		if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
			invention_runtime.editor.close_editor()
			get_viewport().set_input_as_handled()
		return
	if startup_cover != null and startup_cover.visible:
		# Workshop actions use _unhandled_key_input. Consume loading-time
		# events here without disabling its collision-bearing child nodes.
		get_viewport().set_input_as_handled()
		return
	if event is InputEventKey and event.pressed and not event.echo:
		match event.keycode:
			KEY_ESCAPE:
				Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
			KEY_TAB:
				_set_walking_mode(not walking)
			KEY_R:
				if walking:
					player_body.recover()
			KEY_1:
				_set_walking_mode(false)
				_jump_to_view("pin")
			KEY_2:
				_set_walking_mode(false)
				_jump_to_view("greenbelt")
			KEY_3:
				_set_walking_mode(false)
				_jump_to_view("mall")
			KEY_4, KEY_5, KEY_6:
				style_index = int(event.keycode) - KEY_4
				_apply_style()
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	if event is InputEventMouseMotion and Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
		if walking:
			player_body.rotation.y -= event.relative.x * 0.0025
			if invention_runtime != null and invention_runtime.third_person:
				invention_runtime.camera_boom.rotation.x = clampf(invention_runtime.camera_boom.rotation.x - event.relative.y * 0.0025, -1.2, 0.6)
			else:
				walk_camera.rotation.x = clampf(walk_camera.rotation.x - event.relative.y * 0.0025, -1.45, 1.2)
		else:
			yaw -= event.relative.x * 0.0025
			pitch = clampf(pitch - event.relative.y * 0.0025, -1.55, 1.35)
			camera.rotation = Vector3(pitch, yaw, 0)


func _process(delta: float) -> void:
	if camera == null:
		return
	if walking:
		terrain_colliders.update_center(player_body.global_position)
		camera.position = walk_camera.global_position # Preserve visual LOD's camera anchor.
	elif (startup_cover == null or not startup_cover.visible) and (invention_runtime == null or not invention_runtime.editor_open) and (travel_runtime == null or (not travel_runtime.panel_open and not travel_runtime.transitioning)):
		var direction := Vector3.ZERO
		if Input.is_key_pressed(KEY_W):
			direction -= camera.global_basis.z
		if Input.is_key_pressed(KEY_S):
			direction += camera.global_basis.z
		if Input.is_key_pressed(KEY_D):
			direction += camera.global_basis.x
		if Input.is_key_pressed(KEY_A):
			direction -= camera.global_basis.x
		if Input.is_key_pressed(KEY_SPACE):
			direction.y += 1.0
		if Input.is_key_pressed(KEY_CTRL):
			direction.y -= 1.0
		var speed := 90.0 if Input.is_key_pressed(KEY_SHIFT) else 35.0
		if direction.length_squared() > 0:
			camera.position += direction.normalized() * speed * delta
	var ground := _height_at(camera.position.x, camera.position.z)
	if not walking:
		camera.position.y = maxf(camera.position.y, ground + 2.0)
	lod_timer += delta
	if lod_timer >= 0.75:
		lod_timer = 0.0
		_refresh_lod_targets()
	_service_lod_job()
	_start_next_lod_job()
	var outstanding_lod := pending_lod.size() + (1 if lod_task_id >= 0 else 0)
	var controls := "WASD walk  •  Space jump / hold to glide  •  Shift run  •  R recover  •  Tab fly" if walking else "WASD fly  •  Space/Ctrl up/down  •  Shift faster  •  Tab walk"
	status_label.text = "BARTON CREEK  •  30.250924, -97.810494  •  %s\n%s  •  1 pin / 2 greenbelt / 3 mall  •  4/5/6 styles  •  Esc release\nLocal X %.0f m  Z %.0f m  •  ground %.0f m  •  coarse %d/%d  •  LOD %d  •  tile %.1f/%.0f MiB" % [styles[style_index]["name"], controls, camera.position.x, camera.position.z, ground, coarse_ready_tiles, coarse_total_tiles, outstanding_lod, float(lod_resident_bytes) / 1048576.0, float(lod_resident_budget_bytes) / 1048576.0]
	var capture_path := OS.get_environment("ENFRACTAL_CAPTURE")
	if not capture_path.is_empty():
		capture_frame += 1
		if capture_frame >= 80 and outstanding_lod == 0 and coarse_ready_tiles == coarse_total_tiles:
			capture_samples_ms.append(delta * 1000.0)
		if capture_frame >= 180 and coarse_ready_tiles == coarse_total_tiles and outstanding_lod == 0 and capture_samples_ms.size() >= 30:
			capture_samples_ms.sort()
			var sample_count := capture_samples_ms.size()
			var median_ms := capture_samples_ms[sample_count / 2] if sample_count > 0 else 0.0
			var p95_ms := capture_samples_ms[mini(sample_count - 1, int(ceil(sample_count * 0.95)) - 1)] if sample_count > 0 else 0.0
			var metrics := {
				"style": styles[style_index]["id"],
				"view": OS.get_environment("ENFRACTAL_VIEW"),
				"sample_count": sample_count,
				"median_frame_ms": snappedf(median_ms, 0.01),
				"p95_frame_ms": snappedf(p95_ms, 0.01),
				"remaining_lod_tiles": outstanding_lod,
				"last_lod_build_ms": snappedf(lod_last_build_ms, 0.01),
				"last_lod_commit_ms": snappedf(lod_last_commit_ms, 0.01),
				"discarded_lod_jobs": lod_discarded,
				"coarse_ready_tiles": coarse_ready_tiles,
				"coarse_total_tiles": coarse_total_tiles,
				"coarse_initial_ms": snappedf(coarse_initial_ms, 0.01),
				"resident_tile_payload_bytes": lod_resident_bytes,
				"resident_tile_payload_budget_bytes": lod_resident_budget_bytes,
				"coarse_base_budget_floor_bytes": coarse_base_budget_floor_bytes,
				"peak_resident_tile_payload_bytes": lod_peak_resident_bytes,
				"tile_detail_evictions": lod_evictions,
				"tile_detail_budget_rejections": lod_rejected_budget,
				"resident_budget_clamps": lod_budget_clamps,
				"viewport_px": [get_viewport().get_visible_rect().size.x, get_viewport().get_visible_rect().size.y],
				"note": "Diagnostic capture in a hidden window; not a low-hardware benchmark."
			}
			print("Capture metrics: ", JSON.stringify(metrics))
			var metrics_path := OS.get_environment("ENFRACTAL_METRICS")
			if not metrics_path.is_empty():
				var file := FileAccess.open(metrics_path, FileAccess.WRITE)
				if file != null:
					file.store_string(JSON.stringify(metrics, "  ") + "\n")
			var result := get_viewport().get_texture().get_image().save_png(capture_path)
			print("Map screenshot saved: ", capture_path, " (", result, ")")
			get_tree().quit(0 if result == OK else 2)
		elif capture_frame >= 1800:
			push_error("Terrain did not settle for capture within 1800 frames")
			get_tree().quit(2)
