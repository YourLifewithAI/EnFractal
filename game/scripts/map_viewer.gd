extends Node3D
## Lightweight 3D viewer for the offline-built map package. Geometry is rebuilt
## from one shared height grid, so every terrain LOD uses matching edge heights.

const MAP_PATH := "res://maps/barton_creek/"
const MAX_SEGMENT_M := 18.0

var manifest: Dictionary
var features: Dictionary
var height_bytes: PackedByteArray
var grid_side: int
var map_side_m: int
var sample_spacing_m: int
var tile_side_m: int
var height_offset_m: float
var height_scale_m: float
var height_origin_m: float
var height_min_m: float
var height_max_m: float
var tiles: Dictionary = {}
var pending_lod: Array[Vector2i] = []
var camera: Camera3D
var status_label: Label
var yaw := 0.0
var pitch := -0.5
var lod_timer := 0.0
var terrain_material: StandardMaterial3D
var capture_frame := 0


func _ready() -> void:
	manifest = JSON.parse_string(FileAccess.get_file_as_string(MAP_PATH + "manifest.json"))
	features = JSON.parse_string(FileAccess.get_file_as_string(MAP_PATH + "features.json"))
	if manifest.is_empty() or features.is_empty():
		push_error("Map package is missing or invalid")
		get_tree().quit(1)
		return
	height_bytes = FileAccess.get_file_as_bytes(MAP_PATH + "heights.r16")
	grid_side = int(manifest["grid_side"])
	map_side_m = int(manifest["side_m"])
	sample_spacing_m = int(manifest["sample_spacing_m"])
	tile_side_m = int(manifest["tile_side_m"])
	height_offset_m = float(manifest["height_offset_m"])
	height_scale_m = float(manifest["height_scale_m"])
	height_origin_m = float(manifest["height_origin_m"])
	height_min_m = float(manifest["height_min_m"])
	height_max_m = float(manifest["height_max_m"])
	if height_bytes.size() != grid_side * grid_side * 2:
		push_error("Height grid size does not match manifest")
		get_tree().quit(1)
		return
	_setup_scene()
	_setup_materials()
	_build_initial_tiles()
	_build_mapped_features()
	_build_trees()
	_refresh_lod_targets()
	Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	print("Barton Creek map loaded: ", manifest["feature_counts"])


func _setup_scene() -> void:
	camera = Camera3D.new()
	camera.name = "ExplorerCamera"
	camera.current = true
	camera.far = 7500.0
	camera.position = Vector3(0.0, 115.0, 350.0)
	camera.rotation = Vector3(pitch, yaw, 0.0)
	add_child(camera)

	var sunlight := DirectionalLight3D.new()
	sunlight.name = "Sun"
	sunlight.rotation = Vector3(-0.75, -0.65, 0.0)
	sunlight.light_energy = 1.1
	add_child(sunlight)

	var environment := WorldEnvironment.new()
	var settings := Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color(0.62, 0.76, 0.82)
	settings.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	settings.ambient_light_color = Color(0.78, 0.82, 0.78)
	settings.ambient_light_energy = 0.8
	environment.environment = settings
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
	credit.offset_top = -29
	credit.offset_bottom = -10
	credit.text = "Terrain: USGS 3DEP  •  Features: © OpenStreetMap contributors (ODbL)  •  Trees illustrative"
	credit.add_theme_font_size_override("font_size", 13)
	credit.add_theme_color_override("font_color", Color(0.98, 0.96, 0.88))
	credit.add_theme_color_override("font_shadow_color", Color(0.06, 0.11, 0.09))
	canvas.add_child(credit)


func _setup_materials() -> void:
	terrain_material = StandardMaterial3D.new()
	terrain_material.vertex_color_use_as_albedo = true
	terrain_material.roughness = 1.0
	terrain_material.cull_mode = BaseMaterial3D.CULL_DISABLED


func _height_at_grid(row: int, column: int) -> float:
	row = clampi(row, 0, grid_side - 1)
	column = clampi(column, 0, grid_side - 1)
	var index: int = (row * grid_side + column) * 2
	return height_offset_m + float(height_bytes.decode_u16(index)) * height_scale_m - height_origin_m


func _height_at(x: float, z: float) -> float:
	var half: float = map_side_m * 0.5
	var column: float = clampf((x + half) / sample_spacing_m, 0.0, grid_side - 1.0)
	var row: float = clampf((z + half) / sample_spacing_m, 0.0, grid_side - 1.0)
	var x0 := int(floor(column))
	var y0 := int(floor(row))
	var fx := column - x0
	var fy := row - y0
	var upper := lerpf(_height_at_grid(y0, x0), _height_at_grid(y0, x0 + 1), fx)
	var lower := lerpf(_height_at_grid(y0 + 1, x0), _height_at_grid(y0 + 1, x0 + 1), fx)
	return lerpf(upper, lower, fy)


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
	var tile_count := map_side_m / tile_side_m
	for row in range(tile_count):
		for column in range(tile_count):
			var instance := MeshInstance3D.new()
			instance.name = "Terrain_%d_%d" % [row, column]
			instance.mesh = _make_tile_mesh(row, column, 32)
			instance.material_override = terrain_material
			add_child(instance)
			tiles[Vector2i(column, row)] = {"instance": instance, "step": 32}


func _make_tile_mesh(tile_row: int, tile_column: int, step_m: int) -> ArrayMesh:
	var samples := tile_side_m / step_m
	var origin_col := tile_column * tile_side_m / sample_spacing_m
	var origin_row := tile_row * tile_side_m / sample_spacing_m
	var stride := step_m / sample_spacing_m
	var half: float = map_side_m * 0.5
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	for row in range(samples + 1):
		for column in range(samples + 1):
			var grid_row := origin_row + row * stride
			var grid_column := origin_col + column * stride
			var x: float = -half + grid_column * sample_spacing_m
			var z: float = -half + grid_row * sample_spacing_m
			var y := _height_at_grid(grid_row, grid_column)
			var dx := (_height_at_grid(grid_row, grid_column + 1) - _height_at_grid(grid_row, grid_column - 1)) / float(sample_spacing_m * 2)
			var dz := (_height_at_grid(grid_row + 1, grid_column) - _height_at_grid(grid_row - 1, grid_column)) / float(sample_spacing_m * 2)
			var slope := sqrt(dx * dx + dz * dz)
			var absolute_m := y + height_origin_m
			var relief: float = clampf((absolute_m - height_min_m) / maxf(1.0, height_max_m - height_min_m), 0.0, 1.0)
			var color := Color(0.23, 0.42, 0.25).lerp(Color(0.62, 0.55, 0.35), relief)
			if slope > 0.55:
				color = color.lerp(Color(0.67, 0.62, 0.48), clampf((slope - 0.55) / 1.5, 0.0, 0.8))
			vertices.push_back(Vector3(x, y, z))
			normals.push_back(Vector3(-dx, 1.0, -dz).normalized())
			colors.push_back(color)
	for row in range(samples):
		for column in range(samples):
			var a := row * (samples + 1) + column
			var b := a + 1
			var c := a + samples + 1
			var d := c + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, d]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _refresh_lod_targets() -> void:
	pending_lod.clear()
	for key in tiles:
		var target := _target_step(key.y, key.x)
		if target != int(tiles[key]["step"]):
			pending_lod.push_back(key)
	pending_lod.sort_custom(func(a: Vector2i, b: Vector2i) -> bool:
		return _tile_center(a.y, a.x).distance_to(Vector2(camera.position.x, camera.position.z)) < _tile_center(b.y, b.x).distance_to(Vector2(camera.position.x, camera.position.z))
	)


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


func _build_draped_lines(name: String, items: Array, width_m: float, color: Color, y_offset: float) -> void:
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
	var material := _plain_material(color)
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
		if road["tags"].get("highway", "") in ["motorway", "trunk", "primary", "secondary", "motorway_link", "trunk_link", "primary_link", "secondary_link"]:
			highways.append(road)
		else:
			streets.append(road)
	_build_draped_lines("Highways", highways, 13.0, Color(0.72, 0.68, 0.58), 0.4)
	_build_draped_lines("Streets", streets, 5.0, Color(0.61, 0.60, 0.53), 0.25)
	_build_draped_lines("Trails", features["trails"], 1.7, Color(0.67, 0.46, 0.27), 0.22)
	_build_draped_lines("Creeks", features["waterways"], 3.5, Color(0.18, 0.47, 0.57), 0.28)
	_build_water_areas()
	var buildings := []
	var landmarks := []
	for building in features["buildings"]:
		var proxy: Dictionary = building["proxy"]
		if not building["name"].is_empty() and float(proxy["width_m"]) * float(proxy["length_m"]) >= 4000.0:
			landmarks.append(building)
			continue
		var height: float = float(building["height_m"])
		var x: float = float(proxy["x"])
		var z: float = float(proxy["z"])
		var basis := Basis(Vector3.UP, float(proxy["yaw_rad"])).scaled(Vector3(float(proxy["width_m"]), height, float(proxy["length_m"])))
		buildings.append(Transform3D(basis, Vector3(x, _height_at(x,z) + height * 0.5, z)))
	_multimesh_instances("Building proxies", cube, _plain_material(Color(0.66, 0.62, 0.53)), buildings)
	_build_landmark_buildings(landmarks)


func _build_landmark_buildings(items: Array) -> void:
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
	var material := _plain_material(Color(0.69, 0.64, 0.55))
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	var instance := MeshInstance3D.new()
	instance.name = "Mapped landmark building footprints"
	instance.mesh = mesh
	instance.material_override = material
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
	var material := _plain_material(Color(0.16, 0.43, 0.52))
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
	for tree in features["trees"]:
		var x: float = float(tree["x"])
		var z: float = float(tree["z"])
		var base_y := _height_at(x,z)
		var height: float = float(tree["height_m"])
		var trunk_height := height * 0.48
		trunks.append(Transform3D(Basis.IDENTITY.scaled(Vector3(1.0, trunk_height, 1.0)), Vector3(x, base_y + trunk_height * 0.5, z)))
		canopies.append(Transform3D(Basis.IDENTITY.scaled(Vector3(height * 0.21, height * 0.30, height * 0.21)), Vector3(x, base_y + height * 0.73, z)))
	_multimesh_instances("Tree trunks", trunk_mesh, _plain_material(Color(0.29, 0.23, 0.17)), trunks)
	_multimesh_instances("Illustrative canopies", canopy_mesh, _plain_material(Color(0.18, 0.35, 0.22)), canopies)


func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	if event is InputEventMouseMotion and Input.get_mouse_mode() == Input.MOUSE_MODE_CAPTURED:
		yaw -= event.relative.x * 0.0025
		pitch = clampf(pitch - event.relative.y * 0.0025, -1.55, 1.35)
		camera.rotation = Vector3(pitch, yaw, 0)


func _process(delta: float) -> void:
	if camera == null:
		return
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
	camera.position.y = maxf(camera.position.y, ground + 2.0)
	lod_timer += delta
	if lod_timer >= 0.75:
		lod_timer = 0.0
		_refresh_lod_targets()
	if not pending_lod.is_empty():
		var key: Vector2i = pending_lod.pop_front()
		var desired := _target_step(key.y, key.x)
		if desired != int(tiles[key]["step"]):
			var instance: MeshInstance3D = tiles[key]["instance"]
			instance.mesh = _make_tile_mesh(key.y, key.x, desired)
			tiles[key]["step"] = desired
	status_label.text = "BARTON CREEK  •  30.250924, -97.810494\nWASD move  •  mouse look  •  Space/Ctrl up/down  •  Shift faster  •  Esc release mouse\nLocal X %.0f m  Z %.0f m  •  ground %.0f m above map origin  •  LOD queue %d" % [camera.position.x, camera.position.z, ground, pending_lod.size()]
	var capture_path := OS.get_environment("ENFRACTAL_CAPTURE")
	if not capture_path.is_empty():
		capture_frame += 1
		if capture_frame == 119:
			print("Frame rate before capture: ", Engine.get_frames_per_second(), " fps; remaining LOD tiles: ", pending_lod.size())
		if capture_frame == 120:
			var result := get_viewport().get_texture().get_image().save_png(capture_path)
			print("Map screenshot saved: ", capture_path, " (", result, ")")
			get_tree().quit(0 if result == OK else 2)
