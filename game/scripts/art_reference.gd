extends Node3D
## A bounded, source-anchored walking-height art reference. It is an illustrative
## design fixture, not a surveyed reconstruction or the shared game world.

const MAP_RUNTIME_SCRIPT := preload("res://scripts/map_runtime.gd")
const CENTER := Vector2(-280.0, 70.0)
const HALF := 96.0
const CAMERA_POINT := Vector2(-334.0, 82.0)
const LOOK_POINT := Vector2(-326.0, 80.0)
const LOOKOUT_SPEC := {
	"id": "illustrative_trail_lookout_v1",
	"center": Vector2(-326.0, 80.0),
	"width_m": 5.2,
	"depth_m": 3.4,
}

var map_runtime
var profile := "standard"
var recipe: Dictionary = {}
var colors: Dictionary = {}
var materials: Dictionary = {}
var content: Node3D
var camera: Camera3D
var sunlight: DirectionalLight3D
var stats: Dictionary = {}


func _ready() -> void:
	map_runtime = MAP_RUNTIME_SCRIPT.new()
	if not map_runtime.load_package("res://maps/barton_creek/", "barton_creek_v0"):
		push_error("Art reference map failed validation: " + map_runtime.last_error)
		get_tree().quit(1)
		return
	recipe = JSON.parse_string(FileAccess.get_file_as_string("res://styles/art_reference.json"))
	var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://styles/greenbelt_styles.json"))
	for style in catalog["styles"]:
		if style["id"] == "natural":
			for role in style["objects"]:
				var rgb: Array = style["objects"][role]
				colors[role] = Color8(int(rgb[0]), int(rgb[1]), int(rgb[2]))
			break
	if colors.is_empty() or not recipe is Dictionary:
		push_error("Art reference style recipe is missing")
		get_tree().quit(1)
		return
	_make_materials()
	_make_atmosphere()
	var requested := OS.get_environment("ENFRACTAL_ART_PROFILE")
	if requested == "low":
		profile = "low"
	set_profile(profile)
	_make_camera()
	print("Art reference loaded: ", profile, " ", stats)


func set_profile(requested: String) -> void:
	if requested != "standard" and requested != "low":
		push_warning("Unknown art profile: " + requested)
		return
	profile = requested
	if content != null:
		remove_child(content)
		content.queue_free()
	content = Node3D.new()
	content.name = "GeneratedReference_" + profile
	content.set_meta("source_map", "barton_creek_v0")
	content.set_meta("interpretation", "illustrative art reference")
	add_child(content)
	stats = {"terrain_triangles": 0, "distant_terrain_triangles": 0, "trail_segments": 0, "creek_segments": 0, "trees": 0, "shrubs": 0, "rocks": 0, "limestone_ledges": 0, "grass_tufts": 0}
	_make_distant_terrain()
	_make_terrain()
	_make_ribbons()
	_make_lookout_connector()
	_make_rocks()
	_make_limestone_ledges()
	_make_trees()
	_make_shrubs()
	_make_grass()
	_make_lookout()
	stats["triangle_upper_bound"] = _triangle_upper_bound(content)
	sunlight.shadow_enabled = bool(recipe["profiles"][profile]["sun_shadows"])


func _triangle_upper_bound(node: Node) -> int:
	var triangles := 0
	if node is MeshInstance3D:
		triangles += _mesh_triangles(node.mesh)
	elif node is MultiMeshInstance3D:
		triangles += _mesh_triangles(node.multimesh.mesh) * node.multimesh.instance_count
	for child in node.get_children():
		triangles += _triangle_upper_bound(child)
	return triangles


func _mesh_triangles(mesh: Mesh) -> int:
	if mesh == null:
		return 0
	return int(mesh.get_faces().size() / 3)


func _make_materials() -> void:
	materials["terrain"] = _material(Color.WHITE, 1.0, true)
	materials["trail"] = _material(colors["trail"].lightened(0.08), 1.0)
	materials["bank"] = _material(colors["rock"].darkened(0.14), 1.0)
	materials["water"] = _material(colors["water"].lightened(0.17), 0.42)
	materials["water_highlight"] = _material(colors["creek"].lightened(0.12), 0.58)
	materials["rock"] = _material(colors["rock"].darkened(0.19), 1.0)
	materials["rock_shade"] = _material(colors["rock"].darkened(0.13), 1.0)
	materials["trunk"] = _material(colors["trunk"].lightened(0.06), 1.0)
	materials["canopy"] = _material(Color.WHITE, 1.0, true)
	materials["shrub"] = _material(colors["canopy"].lightened(0.08), 1.0)
	materials["grass"] = _material(Color(0.19, 0.30, 0.17), 1.0)
	materials["wood"] = _material(colors["trunk"].lightened(0.16), 1.0)
	materials["metal"] = _material(colors["glass"].darkened(0.10), 0.55)
	materials["sign"] = _material(colors["building"].lightened(0.35), 0.95)


func _material(tint: Color, roughness: float, vertex_color := false) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = tint
	material.roughness = roughness
	material.vertex_color_use_as_albedo = vertex_color
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	return material


func _make_atmosphere() -> void:
	sunlight = DirectionalLight3D.new()
	sunlight.name = "WarmAfternoonSun"
	sunlight.rotation = Vector3(-0.75, -0.74, 0.0)
	sunlight.light_color = Color(1.0, 0.90, 0.76)
	sunlight.light_energy = 0.86
	sunlight.directional_shadow_max_distance = 130.0
	add_child(sunlight)
	var world := WorldEnvironment.new()
	world.name = "SoftSkyAndAmbient"
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.69, 0.78, 0.78)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.73, 0.79, 0.75)
	environment.ambient_light_energy = 0.70
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	world.environment = environment
	add_child(world)


func _make_camera() -> void:
	camera = Camera3D.new()
	camera.name = "WalkingHeightCamera"
	var eye := CAMERA_POINT
	var focus := LOOK_POINT
	if OS.get_environment("ENFRACTAL_ART_VIEW") == "creek":
		eye = Vector2(-291.0, 55.0)
		focus = Vector2(-253.0, 144.0)
	var eye_height: float = map_runtime.surface_height_at(eye.x, eye.y) + 1.67
	camera.position = Vector3(eye.x, eye_height, eye.y)
	add_child(camera)
	var target_height: float = map_runtime.surface_height_at(focus.x, focus.y) + 1.35
	camera.look_at(Vector3(focus.x, target_height, focus.y))
	camera.fov = 58.0
	camera.near = 0.08
	camera.far = 550.0
	camera.current = true


func _make_terrain() -> void:
	var step := float(recipe["profiles"][profile]["terrain_spacing_m"])
	var cells := int(HALF * 2.0 / step)
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var vertex_colors := PackedColorArray()
	var indices := PackedInt32Array()
	for row in range(cells + 1):
		for column in range(cells + 1):
			var x := CENTER.x - HALF + column * step
			var z := CENTER.y - HALF + row * step
			var height: float = map_runtime.surface_height_at(x, z)
			var normal: Vector3 = map_runtime.normal_at(x, z)
			vertices.append(Vector3(x, height, z))
			normals.append(normal)
			var rock_weight := smoothstep(0.13, 0.43, 1.0 - normal.y)
			var variation := sin(x * 0.19 + z * 0.13) * 0.035 + sin(x * 0.49 - z * 0.31) * 0.018
			var grass: Color = colors["canopy"].lightened(0.31 + variation)
			var stone: Color = colors["rock"].lightened(0.02 + variation)
			vertex_colors.append(grass.lerp(stone, rock_weight))
	for row in range(cells):
		for column in range(cells):
			var a := row * (cells + 1) + column
			var b := a + 1
			var c := a + cells + 1
			var d := c + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, d]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = vertex_colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var instance := MeshInstance3D.new()
	instance.name = "USGSHeightPatch"
	instance.mesh = mesh
	instance.material_override = materials["terrain"]
	instance.set_meta("provenance", "USGS 3DEP sample heights; vertex colors are illustrative")
	content.add_child(instance)
	stats["terrain_triangles"] = indices.size() / 3


func _make_distant_terrain() -> void:
	# A coarse outer ring keeps the walking view from ending at the 192 m art
	# patch. It is source elevation, but its color/detail are interpretation.
	var outer_half := 256.0
	var step := 16.0 if profile == "standard" else 32.0
	var cells := int(outer_half * 2.0 / step)
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var vertex_colors := PackedColorArray()
	var indices := PackedInt32Array()
	for row in range(cells + 1):
		for column in range(cells + 1):
			var x := CENTER.x - outer_half + column * step
			var z := CENTER.y - outer_half + row * step
			var height: float = map_runtime.surface_height_at(x, z)
			var normal: Vector3 = map_runtime.normal_at(x, z)
			vertices.append(Vector3(x, height, z))
			normals.append(normal)
			var rock_weight := smoothstep(0.16, 0.48, 1.0 - normal.y)
			vertex_colors.append(colors["canopy"].lightened(0.41).lerp(colors["rock"].lightened(0.20), rock_weight))
	for row in range(cells):
		for column in range(cells):
			var center_x := CENTER.x - outer_half + (column + 0.5) * step
			var center_z := CENTER.y - outer_half + (row + 0.5) * step
			if absf(center_x - CENTER.x) < HALF and absf(center_z - CENTER.y) < HALF:
				continue
			var a := row * (cells + 1) + column
			var b := a + 1
			var c := a + cells + 1
			var d := c + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, d]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = vertex_colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var instance := MeshInstance3D.new()
	instance.name = "CoarseUSGSBackdrop"
	instance.mesh = mesh
	instance.material_override = materials["terrain"]
	instance.set_meta("provenance", "USGS 3DEP sampled at coarse spacing; color illustrative")
	content.add_child(instance)
	stats["distant_terrain_triangles"] = indices.size() / 3


func _make_ribbons() -> void:
	var bounds := Rect2(CENTER - Vector2.ONE * HALF, Vector2.ONE * (HALF * 2.0))
	var trail_arrays := _ribbon_arrays("trails", bounds, 2.5, 0.16)
	_add_ribbon_mesh("MappedTrailSoil", trail_arrays, "trail", "OSM trail centerline; width and color illustrative")
	var bank_arrays := _ribbon_arrays("waterways", bounds, 9.0, 0.10)
	_add_ribbon_mesh("CreekBank", bank_arrays, "bank", "OSM waterway centerline; bank width illustrative")
	var water_arrays := _ribbon_arrays("waterways", bounds, 6.6, 0.14)
	_add_ribbon_mesh("CreekWater", water_arrays, "water", "OSM waterway centerline; ribbon is illustrative, not a water simulation")
	_make_water_riffles(bounds)


func _make_water_riffles(bounds: Rect2) -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for feature in map_runtime.query_features("waterways", bounds):
		if str(feature.get("name", "")) != "Barton Creek":
			continue
		var points: Array = feature.get("points", [])
		for i in range(points.size() - 1):
			var clipped := _clip_segment(Vector2(float(points[i][0]), float(points[i][1])), Vector2(float(points[i + 1][0]), float(points[i + 1][1])))
			if clipped.size() != 2:
				continue
			var a := clipped[0]
			var b := clipped[1]
			var count := int(floor(a.distance_to(b) / 8.0))
			if count < 1:
				continue
			var flow := (b - a).normalized()
			var across := flow.orthogonal()
			for stroke in range(count):
				var center := a.lerp(b, (float(stroke) + 0.5) / count)
				var reach := 1.0 + 0.32 * sin(float(stroke * 7 + i * 3))
				var corners := [center - across * reach - flow * 0.035, center + across * reach - flow * 0.035, center - across * reach + flow * 0.035, center + across * reach + flow * 0.035]
				var base := vertices.size()
				for point in corners:
					vertices.append(Vector3(point.x, map_runtime.surface_height_at(point.x, point.y) + 0.22, point.y))
					normals.append(Vector3.UP)
				indices.append_array(PackedInt32Array([base, base + 2, base + 1, base + 1, base + 2, base + 3]))
	_add_ribbon_mesh("IllustrativeCreekRiffles", {"vertices": vertices, "normals": normals, "indices": indices}, "water_highlight", "authored shallow-water highlights on mapped Barton Creek centerline")


func _ribbon_arrays(kind: String, bounds: Rect2, width: float, height_offset: float) -> Dictionary:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for feature in map_runtime.query_features(kind, bounds):
		var points: Array = feature.get("points", [])
		for i in range(points.size() - 1):
			var raw_a := Vector2(float(points[i][0]), float(points[i][1]))
			var raw_b := Vector2(float(points[i + 1][0]), float(points[i + 1][1]))
			var clipped := _clip_segment(raw_a, raw_b)
			if clipped.size() != 2:
				continue
			var a := clipped[0]
			var b := clipped[1]
			var length := a.distance_to(b)
			if length < 0.02:
				continue
			var lateral := (b - a).normalized().orthogonal() * width * 0.5
			var steps := maxi(1, int(ceil(length / 2.0)))
			for j in range(steps):
				var start := a.lerp(b, float(j) / steps)
				var end := a.lerp(b, float(j + 1) / steps)
				var corners := [start - lateral, start + lateral, end - lateral, end + lateral]
				var base := vertices.size()
				for corner in corners:
					var y: float = map_runtime.surface_height_at(corner.x, corner.y) + height_offset
					vertices.append(Vector3(corner.x, y, corner.y))
					normals.append(Vector3.UP)
				indices.append_array(PackedInt32Array([base, base + 2, base + 1, base + 1, base + 2, base + 3]))
				if kind == "trails":
					stats["trail_segments"] += 1
				else:
					stats["creek_segments"] += 1
	return {"vertices": vertices, "normals": normals, "indices": indices}


func _add_ribbon_mesh(name: String, parts: Dictionary, material_role: String, provenance: String) -> void:
	if parts["vertices"].is_empty():
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = parts["vertices"]
	arrays[Mesh.ARRAY_NORMAL] = parts["normals"]
	arrays[Mesh.ARRAY_INDEX] = parts["indices"]
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var instance := MeshInstance3D.new()
	instance.name = name
	instance.mesh = mesh
	instance.material_override = materials[material_role]
	instance.set_meta("provenance", provenance)
	content.add_child(instance)


func _make_lookout_connector() -> void:
	# The authored lookout joins a mapped trail through an explicit, separately
	# named part. Its position is illustrative and should later use E08A joins.
	var a: Vector2 = LOOKOUT_SPEC["center"]
	var b := Vector2(-318.45, 78.92)
	var lateral := (b - a).normalized().orthogonal() * 0.95
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	var steps := maxi(1, int(ceil(a.distance_to(b) / 2.0)))
	for i in range(steps):
		var start := a.lerp(b, float(i) / steps)
		var end := a.lerp(b, float(i + 1) / steps)
		var base := vertices.size()
		for point in [start - lateral, start + lateral, end - lateral, end + lateral]:
			vertices.append(Vector3(point.x, map_runtime.surface_height_at(point.x, point.y) + 0.18, point.y))
			normals.append(Vector3.UP)
		indices.append_array(PackedInt32Array([base, base + 2, base + 1, base + 1, base + 2, base + 3]))
	_add_ribbon_mesh("IllustrativeLookoutTrailJoin", {"vertices": vertices, "normals": normals, "indices": indices}, "trail", "authored connector from modern lookout to OSM trail")


func _clip_segment(a: Vector2, b: Vector2) -> PackedVector2Array:
	var delta := b - a
	var p := [-delta.x, delta.x, -delta.y, delta.y]
	var q := [a.x - CENTER.x + HALF, CENTER.x + HALF - a.x, a.y - CENTER.y + HALF, CENTER.y + HALF - a.y]
	var low := 0.0
	var high := 1.0
	for i in range(4):
		if absf(p[i]) < 0.00001:
			if q[i] < 0.0:
				return PackedVector2Array()
			continue
		var t: float = q[i] / p[i]
		if p[i] < 0.0:
			low = maxf(low, t)
		else:
			high = minf(high, t)
		if low > high:
			return PackedVector2Array()
	return PackedVector2Array([a + delta * low, a + delta * high])


func _make_rocks() -> void:
	var rock_mesh := _rock_mesh()
	var count := int(recipe["profiles"][profile]["rocks"])
	var rocks := MultiMesh.new()
	rocks.transform_format = MultiMesh.TRANSFORM_3D
	rocks.mesh = rock_mesh
	rocks.instance_count = count
	for i in range(count):
		var x := CENTER.x - 82.0 + fposmod(float(i * 43), 167.0)
		var z := CENTER.y - 76.0 + fposmod(float(i * 67), 151.0)
		var radius := 0.8 + fposmod(float(i * 17), 11.0) * 0.11
		var height := 0.45 + fposmod(float(i * 13), 9.0) * 0.12
		var basis := Basis(Vector3.UP, float(i) * 1.71).scaled(Vector3(radius, height, radius * (0.85 + fposmod(float(i * 7), 5.0) * 0.06)))
		rocks.set_instance_transform(i, Transform3D(basis, Vector3(x, map_runtime.surface_height_at(x, z) - 0.13, z)))
	var instance := MultiMeshInstance3D.new()
	instance.name = "IllustrativeLimestoneOutcrops"
	instance.multimesh = rocks
	instance.material_override = materials["rock"]
	instance.set_meta("provenance", "authored illustrative limestone locations")
	content.add_child(instance)
	stats["rocks"] = count


func _make_limestone_ledges() -> void:
	var count := int(recipe["profiles"][profile]["limestone_ledges"])
	var rock_mesh := _rock_mesh()
	var lower := MultiMesh.new()
	lower.transform_format = MultiMesh.TRANSFORM_3D
	lower.mesh = rock_mesh
	lower.instance_count = count
	var upper := MultiMesh.new()
	upper.transform_format = MultiMesh.TRANSFORM_3D
	upper.mesh = rock_mesh
	upper.instance_count = count
	for i in range(count):
		var x := -240.0 + 5.5 * sin(float(i) * 2.17)
		var z := 18.0 + (float(i) + 0.3 * sin(float(i) * 1.6)) * (123.0 / count)
		var y: float = map_runtime.surface_height_at(x, z)
		var yaw := 0.14 * sin(float(i) * 1.83)
		var length := 1.45 + 0.60 * sin(float(i) * 2.4 + 0.8)
		lower.set_instance_transform(i, Transform3D(Basis(Vector3.UP, yaw).scaled(Vector3(1.12, 0.58, length)), Vector3(x, y - 0.23, z)))
		upper.set_instance_transform(i, Transform3D(Basis(Vector3.UP, yaw).scaled(Vector3(0.91, 0.22, length * 0.87)), Vector3(x, y + 0.16, z)))
	_add_multimesh("LimestoneLedgeShade", lower, "rock_shade", "seeded illustrative strata on mapped bank terrain")
	_add_multimesh("LimestoneLedgeCaps", upper, "rock", "seeded illustrative strata on mapped bank terrain")
	stats["limestone_ledges"] = count


func _rock_mesh() -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for ring in range(3):
		var y: float = [0.0, 0.68, 1.0][ring]
		var radius: float = [0.8, 1.12, 0.65][ring]
		for k in range(9):
			var angle := TAU * k / 8.0
			var radial: float = radius * (1.0 + 0.07 * sin(float(k * 19 + ring * 7)))
			vertices.append(Vector3(cos(angle) * radial, y + 0.04 * sin(float(k * 13)), sin(angle) * radial))
			normals.append(Vector3(cos(angle), 0.2, sin(angle)).normalized())
	for ring in range(2):
		for k in range(8):
			var a := ring * 9 + k
			indices.append_array(PackedInt32Array([a, a + 9, a + 1, a + 1, a + 9, a + 10]))
	vertices.append(Vector3(0, 1.04, 0))
	normals.append(Vector3.UP)
	for k in range(8):
		indices.append_array(PackedInt32Array([27, 18 + k, 19 + k]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _make_trees() -> void:
	var bounds := Rect2(CENTER - Vector2.ONE * (HALF - 8.0), Vector2.ONE * ((HALF - 8.0) * 2.0))
	var source_trees: Array[Dictionary] = map_runtime.query_features("trees", bounds)
	var trees: Array[Dictionary] = source_trees.duplicate()
	# The map's sparse tree points are representative, not surveyed individuals.
	# Additional seeded crowns test forest composition while staying separately
	# identified as an illustration in the scene and recipe.
	var random := RandomNumberGenerator.new()
	random.seed = 71309241
	for i in range(int(recipe["profiles"][profile]["authored_trees"])):
		var x := 0.0
		var z := 0.0
		for attempt in range(20):
			x = random.randf_range(CENTER.x - HALF + 9.0, CENTER.x + HALF - 9.0)
			z = random.randf_range(CENTER.y - HALF + 9.0, CENTER.y + HALF - 9.0)
			var site := Vector2(x, z)
			if site.distance_to(CAMERA_POINT) > 9.0 and site.distance_to(LOOKOUT_SPEC["center"]) > 11.0 and _distance_to_segment(site, CAMERA_POINT, LOOK_POINT) > 8.5:
				break
		trees.append({"x": x, "z": z, "height_m": random.randf_range(7.5, 12.0), "kind": "cedar_like" if i % 3 == 1 else "oak_like"})
	var trunk_mesh := _trunk_mesh()
	var branch_mesh := CylinderMesh.new()
	branch_mesh.top_radius = 0.72
	branch_mesh.bottom_radius = 1.0
	branch_mesh.height = 1.0
	branch_mesh.radial_segments = 7
	var trunk_instances := MultiMesh.new()
	trunk_instances.transform_format = MultiMesh.TRANSFORM_3D
	trunk_instances.mesh = trunk_mesh
	trunk_instances.instance_count = trees.size()
	var branches := MultiMesh.new()
	branches.transform_format = MultiMesh.TRANSFORM_3D
	branches.mesh = branch_mesh
	branches.instance_count = trees.size() * (0 if profile == "low" else 2)
	var canopy_groups: Array = [[], [], []]
	for i in range(trees.size()):
		var tree: Dictionary = trees[i]
		var variant := 1 if tree.get("kind", "") == "cedar_like" else (2 if i % 4 == 0 else 0)
		canopy_groups[variant].append(i)
		var x := float(tree["x"])
		var z := float(tree["z"])
		var y: float = map_runtime.surface_height_at(x, z)
		var total_height := clampf(float(tree["height_m"]), 5.0, 13.0)
		var trunk_height := total_height * (0.52 if variant == 1 else 0.34)
		var trunk_radius := total_height * (0.030 if variant == 1 else 0.044)
		var trunk_basis := Basis(Vector3.FORWARD, 0.055 * sin(float(i) * 2.1)).scaled(Vector3(trunk_radius, trunk_height, trunk_radius))
		trunk_instances.set_instance_transform(i, Transform3D(trunk_basis, Vector3(x, y, z)))
		var radius := total_height * (0.26 if variant == 1 else 0.47)
		if profile != "low":
			for branch in range(2):
				var branch_angle := float(i * 11 + branch * 3) * 2.39996
				var direction := Vector3(cos(branch_angle) * radius * 0.40, radius * 0.24, sin(branch_angle) * radius * 0.40)
				var start := Vector3(x, y + trunk_height * 0.83, z)
				var branch_basis := Basis(Quaternion(Vector3.UP, direction.normalized())).scaled(Vector3(trunk_radius * 0.56, direction.length(), trunk_radius * 0.56))
				branches.set_instance_transform(i * 2 + branch, Transform3D(branch_basis, start + direction * 0.5))
	_add_multimesh("RepresentativeAndAuthoredTrunks", trunk_instances, "trunk", "first %d centers from representative map trees; remainder are seeded illustration" % source_trees.size())
	if profile != "low":
		_add_multimesh("BranchingForms", branches, "trunk", "illustrative live-oak-like branch forms")
	for variant in range(3):
		var group: Array = canopy_groups[variant]
		if group.is_empty():
			continue
		var lobes := 1 if profile == "low" or variant == 1 else (2 if variant == 0 else 3)
		var crowns := MultiMesh.new()
		crowns.transform_format = MultiMesh.TRANSFORM_3D
		crowns.mesh = _foliage_mesh(variant)
		crowns.instance_count = group.size() * lobes
		for index in range(group.size()):
			var tree_index: int = group[index]
			var tree: Dictionary = trees[tree_index]
			var x := float(tree["x"])
			var z := float(tree["z"])
			var y: float = map_runtime.surface_height_at(x, z)
			var total_height := clampf(float(tree["height_m"]), 5.0, 13.0)
			var trunk_height := total_height * (0.52 if variant == 1 else 0.34)
			var radius := total_height * (0.26 if variant == 1 else (0.47 if variant == 0 else 0.42))
			for lobe in range(lobes):
				var angle := float(tree_index * 17 + lobe * 5) * 2.39996
				var spread := 0.0 if lobe == 0 else radius * (0.52 if variant == 0 else 0.66)
				var crown_y := y + trunk_height + (total_height * 0.12 if variant == 1 else radius * 0.13)
				var center := Vector3(x + cos(angle) * spread, crown_y, z + sin(angle) * spread)
				var size := radius * (1.0 if lobe == 0 else 0.65)
				var vertical := total_height * 0.33 if variant == 1 else size * (0.48 if variant == 0 else 0.57)
				crowns.set_instance_transform(index * lobes + lobe, Transform3D(Basis(Vector3.UP, angle).scaled(Vector3(size, vertical, size * 0.88)), center))
		_add_multimesh("CanopySilhouette%d" % variant, crowns, "canopy", "source tags select oak-like or cedar-like silhouette; all crown forms and authored centers illustrative")
	stats["trees"] = trees.size()


func _distance_to_segment(point: Vector2, a: Vector2, b: Vector2) -> float:
	var direction := b - a
	var denominator := direction.length_squared()
	if denominator <= 0.0:
		return point.distance_to(a)
	var t := clampf((point - a).dot(direction) / denominator, 0.0, 1.0)
	return point.distance_to(a + direction * t)


func _trunk_mesh() -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	var columns := 8
	var rows := 4
	for row in range(rows + 1):
		var t := float(row) / rows
		var radius := 1.18 - t * 0.53
		for column in range(columns + 1):
			var angle := TAU * float(column) / columns
			var irregularity := 1.0 + 0.06 * sin(angle * 3.0 + t * 5.0)
			vertices.append(Vector3(cos(angle) * radius * irregularity + 0.19 * t * t, t, sin(angle) * radius * irregularity + 0.11 * sin(t * PI)))
			normals.append(Vector3(cos(angle), 0.15, sin(angle)).normalized())
	for row in range(rows):
		for column in range(columns):
			var a := row * (columns + 1) + column
			var b := a + 1
			var c := a + columns + 1
			var d := c + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, d]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _foliage_mesh(variant: int) -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var vertex_colors := PackedColorArray()
	var indices := PackedInt32Array()
	var columns := 16
	var rows := 8
	for row in range(rows + 1):
		var latitude := PI * float(row) / rows
		for column in range(columns + 1):
			var longitude := TAU * float(column) / columns
			var irregularity := 1.0 + 0.10 * sin((3.0 + variant) * longitude + latitude * 2.0) + 0.06 * cos(7.0 * longitude - latitude * 3.0)
			var taper := 0.86 - 0.18 * cos(latitude) if variant == 1 else (1.0 + 0.10 * sin(longitude * 2.0) if variant == 2 else 1.0)
			var r := maxf(0.001, sin(latitude)) * irregularity * taper
			var vertical := cos(latitude) * (1.0 + 0.045 * sin(longitude * 5.0))
			var v := Vector3(cos(longitude) * r, vertical, sin(longitude) * r)
			vertices.append(v)
			normals.append(Vector3(v.x, v.y * 0.65, v.z).normalized())
			var lightness := 0.07 + 0.07 * maxf(v.y, 0.0) + 0.025 * sin(longitude * 5.0 + latitude * 3.0)
			var base_color: Color = colors["pilot_canopy"] if variant == 1 else colors["canopy"]
			vertex_colors.append(base_color.lightened(lightness))
	for row in range(rows):
		for column in range(columns):
			var a := row * (columns + 1) + column
			var b := a + 1
			var c := a + columns + 1
			var d := c + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, d]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = vertex_colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _make_shrubs() -> void:
	var count := int(recipe["profiles"][profile]["shrubs"])
	var mesh := SphereMesh.new()
	mesh.radius = 1.0
	mesh.height = 2.0
	mesh.radial_segments = 7
	mesh.rings = 4
	var group := MultiMesh.new()
	group.transform_format = MultiMesh.TRANSFORM_3D
	group.mesh = mesh
	group.instance_count = count
	var random := RandomNumberGenerator.new()
	random.seed = 884421
	for i in range(count):
		var x := random.randf_range(CENTER.x - HALF + 8.0, CENTER.x + HALF - 8.0)
		var z := random.randf_range(CENTER.y - HALF + 8.0, CENTER.y + HALF - 8.0)
		var radius := random.randf_range(0.55, 1.3)
		var height := random.randf_range(0.35, 0.9)
		var y: float = map_runtime.surface_height_at(x, z)
		group.set_instance_transform(i, Transform3D(Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3(radius, height, radius * random.randf_range(0.7, 1.1))), Vector3(x, y + height * 0.45, z)))
	_add_multimesh("IllustrativeUnderstory", group, "shrub", "seeded illustrative shrubs")
	stats["shrubs"] = count


func _make_grass() -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for blade in range(3):
		var angle := TAU * float(blade) / 3.0
		var direction := Vector3(cos(angle), 0.0, sin(angle))
		var side := Vector3(-direction.z, 0.0, direction.x)
		var base := vertices.size()
		vertices.append(-side * 0.10)
		vertices.append(side * 0.10)
		vertices.append(direction * 0.15 + Vector3(0.0, 1.0 - 0.10 * blade, 0.0))
		for i in range(3):
			normals.append(direction)
		indices.append_array(PackedInt32Array([base, base + 1, base + 2]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var count := int(recipe["profiles"][profile]["grass_tufts"])
	var group := MultiMesh.new()
	group.transform_format = MultiMesh.TRANSFORM_3D
	group.mesh = mesh
	group.instance_count = count
	var random := RandomNumberGenerator.new()
	random.seed = 890210
	for i in range(count):
		var focus := i < count / 2
		var x := random.randf_range(CAMERA_POINT.x - 27.0, CAMERA_POINT.x + 37.0) if focus else random.randf_range(CENTER.x - HALF + 3.0, CENTER.x + HALF - 3.0)
		var z := random.randf_range(CAMERA_POINT.y - 25.0, CAMERA_POINT.y + 26.0) if focus else random.randf_range(CENTER.y - HALF + 3.0, CENTER.y + HALF - 3.0)
		var size := random.randf_range(0.16, 0.32)
		group.set_instance_transform(i, Transform3D(Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3(size, size, size)), Vector3(x, map_runtime.surface_height_at(x, z) + 0.02, z)))
	_add_multimesh("IllustrativeGrassTufts", group, "grass", "seeded low-poly grass clusters; no alpha transparency")
	stats["grass_tufts"] = count


func _add_multimesh(name: String, group: MultiMesh, role: String, provenance: String) -> void:
	var instance := MultiMeshInstance3D.new()
	instance.name = name
	instance.multimesh = group
	instance.material_override = materials[role]
	instance.set_meta("provenance", provenance)
	content.add_child(instance)


func _make_lookout() -> void:
	var center: Vector2 = LOOKOUT_SPEC["center"]
	var y: float = map_runtime.surface_height_at(center.x, center.y)
	var assembly := Node3D.new()
	assembly.name = str(LOOKOUT_SPEC["id"])
	assembly.set_meta("provenance", "authored illustrative contemporary trail overlook; not surveyed")
	assembly.set_meta("semantic_parameters", LOOKOUT_SPEC)
	content.add_child(assembly)
	var width: float = LOOKOUT_SPEC["width_m"]
	var depth: float = LOOKOUT_SPEC["depth_m"]
	_box(assembly, "EmbeddedLimestonePlinth", Vector3(center.x, y - 0.10, center.y), Vector3(width, 0.58, depth), "rock_shade")
	_box(assembly, "LimestoneDeck", Vector3(center.x, y + 0.28, center.y), Vector3(width, 0.20, depth), "rock")
	_box(assembly, "TrailArrivalStep", Vector3(center.x - width * 0.5 - 0.36, y + 0.03, center.y), Vector3(0.76, 0.20, 2.14), "rock")
	for column in range(2):
		for row in range(2):
			var x := center.x + (float(column) - 0.5) * (width - 0.28)
			var z := center.y + (float(row) - 0.5) * (depth - 0.28)
			_box(assembly, "WeatheredSteelPost%d%d" % [column, row], Vector3(x, y + 1.65, z), Vector3(0.10, 2.58, 0.10), "metal")
	for side in range(2):
		var z := center.y + (float(side) - 0.5) * (depth - 0.28)
		_box(assembly, "CedarRoofBeam%d" % side, Vector3(center.x, y + 3.0, z), Vector3(width + 0.24, 0.14, 0.15), "wood")
	var slats := 7 if profile == "standard" else 3
	for index in range(slats):
		var x := center.x + (float(index) - (slats - 1) * 0.5) * ((width - 0.25) / maxi(1, slats - 1))
		_box(assembly, "CedarShadeSlat%d" % index, Vector3(x, y + 3.10, center.y), Vector3(0.20, 0.10, depth + 0.35), "wood")
	for index in range(3):
		var z := center.y + (float(index) - 1.0) * 1.27
		_box(assembly, "CreeksideGuardPost%d" % index, Vector3(center.x + width * 0.5 - 0.10, y + 0.92, z), Vector3(0.07, 1.12, 0.07), "metal")
	_box(assembly, "CreeksideGuardRail", Vector3(center.x + width * 0.5 - 0.10, y + 1.48, center.y), Vector3(0.07, 0.07, depth - 0.25), "metal")
	_box(assembly, "CedarBenchSeat", Vector3(center.x - 0.30, y + 0.74, center.y + 0.95), Vector3(1.65, 0.13, 0.44), "wood")
	for index in range(2):
		_box(assembly, "BenchFoot%d" % index, Vector3(center.x - 0.87 + index * 1.14, y + 0.53, center.y + 0.95), Vector3(0.10, 0.33, 0.36), "metal")
	var sign_x := center.x - width * 0.5 + 0.33
	var sign_z := center.y - depth * 0.5 + 0.25
	_box(assembly, "WayfindingPost", Vector3(sign_x, y + 1.12, sign_z), Vector3(0.08, 1.55, 0.08), "metal")
	_box(assembly, "WayfindingFace", Vector3(sign_x, y + 1.97, sign_z), Vector3(0.70, 0.40, 0.10), "sign")
	_box(assembly, "WayfindingStripe", Vector3(sign_x, y + 2.05, sign_z - 0.057), Vector3(0.52, 0.034, 0.012), "water")


func _box(parent: Node3D, name: String, location: Vector3, size: Vector3, role: String) -> void:
	var mesh := BoxMesh.new()
	mesh.size = size
	var instance := MeshInstance3D.new()
	instance.name = name
	instance.mesh = mesh
	instance.material_override = materials[role]
	instance.position = location
	parent.add_child(instance)
