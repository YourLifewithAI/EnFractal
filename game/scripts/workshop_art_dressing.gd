extends Node3D
## Small, illustrative near-field study around the developer workshop plot.
## Map terrain stays authoritative; no creek or surveyed trees are invented here.

const TREE_SITES := [
	Vector2(3.0, 363.0), Vector2(15.0, 324.0), Vector2(-23.0, 340.0),
	Vector2(22.0, 366.0), Vector2(-18.0, 358.0), Vector2(18.0, 359.0),
	Vector2(-29.0, 368.0), Vector2(28.0, 371.0), Vector2(-9.0, 380.0),
	Vector2(12.0, 383.0), Vector2(-34.0, 329.0), Vector2(34.0, 335.0),
]

var viewer: Node3D
var profile := "standard"
var stats := {}
var _last_style_index := -1
var _materials := {}


func configure(map_viewer: Node3D) -> void:
	viewer = map_viewer


func _ready() -> void:
	if viewer == null or viewer.map_runtime == null:
		push_error("Workshop art dressing needs a loaded map viewer")
		return
	profile = "low" if OS.get_environment("ENFRACTAL_ART_PROFILE") == "low" else "standard"
	set_meta("source_map", "barton_creek_v0")
	set_meta("source_style", "res://styles/greenbelt_styles.json")
	set_meta("interpretation", "illustrative fixed-seed live oak, Ashe juniper, limestone and native ground cover; no surveyed tree claim")
	_make_materials()
	_make_ground_strokes()
	_make_trees()
	_make_rocks()
	_make_grass()
	_refresh_style()
	stats["triangle_upper_bound"] = _triangle_count(self)


func _process(_delta: float) -> void:
	if viewer != null and viewer.style_index != _last_style_index:
		_refresh_style()


func _make_materials() -> void:
	for role in ["trunk", "canopy", "rock", "grass", "ground"]:
		var material := StandardMaterial3D.new()
		material.roughness = 1.0
		material.cull_mode = BaseMaterial3D.CULL_DISABLED
		material.vertex_color_use_as_albedo = role in ["canopy", "rock", "grass", "ground"]
		_materials[role] = material
	var sky_material := ProceduralSkyMaterial.new()
	sky_material.sky_top_color = Color(0.34, 0.53, 0.68)
	sky_material.sky_horizon_color = Color(0.67, 0.77, 0.74)
	sky_material.ground_horizon_color = Color(0.59, 0.68, 0.65)
	sky_material.ground_bottom_color = Color(0.30, 0.39, 0.39)
	var sky := Sky.new()
	sky.sky_material = sky_material
	viewer.world_settings.sky = sky
	viewer.world_settings.background_mode = Environment.BG_SKY


func _refresh_style() -> void:
	_last_style_index = viewer.style_index
	var palette: Dictionary = viewer.styles[_last_style_index]["objects"]
	for role in _materials:
		var rgb: Array = palette["canopy"] if role in ["grass", "ground"] else palette[role]
		var tint := Color8(int(rgb[0]), int(rgb[1]), int(rgb[2]))
		if role == "canopy":
			tint = tint.lightened(0.36)
		elif role == "grass":
			tint = tint.lightened(0.31)
		elif role == "rock":
			tint = tint.lightened(0.38)
		elif role == "ground":
			tint = Color(1.0, 1.0, 1.0)
		_materials[role].albedo_color = tint
	# The editable path, generated join, and platform all share this same
	# catalog role; a modest value reduction restores limestone course depth.
	var stone_rgb: Array = palette["rock"]
	viewer._style_material("rock").albedo_color = Color8(int(stone_rgb[0]), int(stone_rgb[1]), int(stone_rgb[2])).darkened(0.18)
	# A restrained afternoon key helps the authored foliage read as layered
	# planes. The shared environment keeps the map's style switch behavior.
	viewer.sunlight.light_color = Color(1.0, 0.91, 0.79)
	viewer.sunlight.rotation = Vector3(-0.82, -0.89, 0.0)
	viewer.sunlight.light_energy = float(viewer.styles[_last_style_index]["sun_energy"]) * 1.08
	viewer.sunlight.shadow_enabled = profile == "standard"
	viewer.sunlight.directional_shadow_max_distance = 125.0
	viewer.world_settings.ambient_light_energy = 0.66
	viewer.world_settings.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	viewer.world_settings.fog_enabled = profile == "standard"
	if profile == "standard":
		viewer.world_settings.fog_light_color = Color(0.60, 0.68, 0.68)
		viewer.world_settings.fog_density = 0.00005
		viewer.world_settings.fog_sun_scatter = 0.025


func _make_ground_strokes() -> void:
	# Fine opaque marks follow the 3DEP surface without changing height or
	# collision. Broad translucent overlays caused visible terrain seams.
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var random := RandomNumberGenerator.new()
	random.seed = 1200764
	var count := 110 if profile == "low" else 250
	for i in range(count):
		var x := random.randf_range(-38.0, 38.0)
		var z := random.randf_range(313.0, 391.0)
		if absf(x) < 4.5 and z > 333.0 and z < 361.0:
			x += 5.0 if x >= 0.0 else -5.0
		var long_axis := random.randf_range(0.16, 0.64)
		var short_axis := random.randf_range(0.035, 0.14)
		var bearing := random.randf_range(0.0, TAU)
		var along := Vector2(cos(bearing), sin(bearing))
		var across := Vector2(-along.y, along.x)
		var center := Vector2(x, z)
		var tone := fposmod(float(i * 17), 7.0) / 7.0
		var paint := Color(0.31, 0.34, 0.21).lerp(Color(0.41, 0.38, 0.24), tone)
		if i % 9 == 0:
			paint = Color(0.48, 0.46, 0.37)
		var shape := [center - along * long_axis, center + across * short_axis, center + along * long_axis, center - across * short_axis]
		for triangle in [[0, 1, 2], [0, 2, 3]]:
			for corner in triangle:
				var point: Vector2 = shape[corner]
				vertices.append(Vector3(point.x, viewer.map_runtime.surface_height_at(point.x, point.y) + 0.025, point.y))
				normals.append(viewer.map_runtime.normal_at(point.x, point.y))
				colors.append(paint)
	var paint_mesh := _mesh(vertices, normals, PackedInt32Array(), colors)
	var instance := MeshInstance3D.new()
	instance.name = "TerrainFollowingGroundStrokes"
	instance.mesh = paint_mesh
	instance.material_override = _materials["ground"]
	instance.set_meta("provenance", "seeded illustrative muted grass, leaf litter, and limestone strokes on sampled USGS terrain")
	add_child(instance)
	stats["ground_strokes"] = count


func _make_trees() -> void:
	var trunk_mesh := CylinderMesh.new()
	trunk_mesh.top_radius = 0.49
	trunk_mesh.bottom_radius = 0.88
	trunk_mesh.height = 1.0
	trunk_mesh.radial_segments = 7
	var trunks := _group(trunk_mesh, TREE_SITES.size())
	var branch_transforms: Array[Transform3D] = []
	var oak_transforms: Array[Transform3D] = []
	var cedar_transforms: Array[Transform3D] = []
	for i in range(TREE_SITES.size()):
		var site: Vector2 = TREE_SITES[i]
		var ground: float = viewer.map_runtime.surface_height_at(site.x, site.y)
		var cedar := i % 4 == 1
		var height := 8.4 + fposmod(float(i * 13), 5.0) * 0.75
		var stem := height * (0.46 if cedar else 0.34)
		var radius := height * (0.28 if cedar else 0.40)
		trunks.set_instance_transform(i, Transform3D(Basis.IDENTITY.scaled(Vector3(0.27, stem, 0.27)), Vector3(site.x, ground + stem * 0.5, site.y)))
		var lobe_count := (1 if cedar else 2) if profile == "low" else (2 if cedar else 3)
		for lobe in range(lobe_count):
			var angle := float(i * 17 + lobe * 7) * 2.39996
			var spread := 0.0 if lobe == 0 else radius * (0.70 if cedar else 1.03)
			var size := radius * (0.79 if lobe == 0 else 0.66)
			var vertical := size * (1.03 if cedar else 0.54)
			var crown_lift := 0.13 * radius * sin(float(i * 5 + lobe * 3))
			var center := Vector3(site.x + cos(angle) * spread, ground + stem + vertical * (0.21 if cedar else 0.31) + crown_lift, site.y + sin(angle) * spread)
			var transform := Transform3D(Basis(Vector3.UP, angle).scaled(Vector3(size, vertical, size * (0.77 if cedar else 0.91))), center)
			if cedar:
				cedar_transforms.append(transform)
			else:
				oak_transforms.append(transform)
			if lobe > 0:
				var origin := Vector3(site.x, ground + stem * 0.77, site.y)
				var branch_end := center - Vector3.UP * vertical * 0.30
				var reach := branch_end - origin
				branch_transforms.append(Transform3D(Basis(Quaternion(Vector3.UP, reach.normalized())).scaled(Vector3(0.16, reach.length(), 0.16)), origin + reach * 0.5))
	_add_group("AuthoredTrunks", trunks, "trunk", "illustrative live-oak and Ashe-juniper centers near the developer plot, not mapped individuals")
	if not branch_transforms.is_empty():
		var branches := _group(trunk_mesh, branch_transforms.size())
		for i in range(branch_transforms.size()):
			branches.set_instance_transform(i, branch_transforms[i])
		_add_group("AuthoredBranches", branches, "trunk", "illustrative open live-oak branching below layered foliage")
	var oaks := _group(_crown_mesh(false), oak_transforms.size(), true)
	for i in range(oak_transforms.size()):
		oaks.set_instance_transform(i, oak_transforms[i])
		oaks.set_instance_color(i, Color(0.93 + 0.06 * sin(float(i * 7)), 0.98, 0.88 + 0.08 * cos(float(i * 5))))
	_add_group("LiveOakBrushCrowns", oaks, "canopy", "illustrative broad, layered live-oak foliage and leaf clusters")
	var cedars := _group(_crown_mesh(true), cedar_transforms.size(), true)
	for i in range(cedar_transforms.size()):
		cedars.set_instance_transform(i, cedar_transforms[i])
		cedars.set_instance_color(i, Color(0.84, 0.94, 0.98))
	_add_group("AsheJuniperBrushCrowns", cedars, "canopy", "illustrative compact Ashe-juniper-like sprays; not surveyed specimens")
	stats["tree_centers"] = TREE_SITES.size()
	stats["canopy_lobes"] = oak_transforms.size() + cedar_transforms.size()
	stats["foliage_marks_per_oak_lobe"] = 16 if profile == "low" else 32


func _crown_mesh(cedar: bool) -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	var columns := 10
	var rows := 4
	for row in range(rows + 1):
		var latitude := PI * float(row) / rows
		for column in range(columns + 1):
			var longitude := TAU * float(column) / columns
			var ripple := 1.0 + 0.13 * sin(longitude * 5.0 + latitude * 1.7) + 0.07 * cos(longitude * 9.0 - latitude * 2.0)
			var ring := maxf(0.001, sin(latitude)) * ripple * (0.78 if cedar else 1.0)
			var vertex := Vector3(cos(longitude) * ring, cos(latitude) * (1.07 if cedar else 0.82), sin(longitude) * ring)
			vertices.append(vertex)
			normals.append(Vector3(vertex.x, vertex.y * (0.55 if cedar else 0.72), vertex.z).normalized())
			var tone := clampf(0.63 + 0.23 * maxf(vertex.y, 0.0) + 0.05 * sin(longitude * 4.0 + latitude * 3.0), 0.52, 0.94)
			colors.append(Color(tone * (0.88 if cedar else 1.0), tone, tone * (0.88 if cedar else 0.78)))
	for row in range(rows):
		for column in range(columns):
			var a := row * (columns + 1) + column
			var b := a + 1
			var c := a + columns + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, c + 1]))
	var marks := (12 if cedar else 16) if profile == "low" else (24 if cedar else 32)
	for mark in range(marks):
		var longitude := float(mark) * 2.399963 + (0.31 if cedar else 0.0)
		var latitude := -0.59 + 1.45 * fposmod(float(mark) * 0.6180339, 1.0)
		var radial := sqrt(maxf(0.18, 1.0 - latitude * latitude)) * (0.77 if cedar else 0.97)
		var outward := Vector3(cos(longitude), 0.20 + latitude * 0.46, sin(longitude)).normalized()
		var root := Vector3(cos(longitude) * radial, latitude * (1.08 if cedar else 0.84), sin(longitude) * radial) + outward * 0.025
		var side := Vector3(-sin(longitude), 0.0, cos(longitude))
		var width := (0.045 if cedar else 0.065) * (0.75 + 0.35 * sin(float(mark * 5)))
		var length := (0.19 if cedar else 0.23) * (0.78 + 0.20 * cos(float(mark * 11)))
		var growth := (outward * 0.52 + Vector3.UP * (0.64 if latitude > 0.0 else 0.24) + side * 0.16).normalized()
		var tip := root + growth * length
		var left := root + side * width + growth * length * 0.36
		var right := root - side * width + growth * length * 0.36
		var tone := 0.72 + 0.22 * maxf(latitude, 0.0) + 0.08 * sin(float(mark * 13))
		var leaf_tint := Color(tone * (0.82 if cedar else 1.05), tone, tone * (0.83 if cedar else 0.66))
		_append_mark(vertices, normals, colors, indices, root, left, tip, right, outward, leaf_tint)
	return _mesh(vertices, normals, indices, colors)


func _append_mark(vertices: PackedVector3Array, normals: PackedVector3Array, colors: PackedColorArray, indices: PackedInt32Array, root: Vector3, left: Vector3, tip: Vector3, right: Vector3, outward: Vector3, tint: Color) -> void:
	var first := vertices.size()
	for point in [root, left, tip, root, tip, right]:
		vertices.append(point)
		normals.append(outward)
		colors.append(tint)
	for offset in range(6):
		indices.append(first + offset)


func _make_rocks() -> void:
	var count := 13 if profile == "low" else 34
	var rocks := _group(_rock_mesh(), count, true)
	var random := RandomNumberGenerator.new()
	random.seed = 33081264
	for i in range(count):
		var x := random.randf_range(-27.0, 27.0)
		var z := random.randf_range(320.0, 381.0)
		if absf(x) < 5.5 and z > 335.0 and z < 357.0:
			x += 7.0 if x >= 0.0 else -7.0
		var y: float = viewer.map_runtime.surface_height_at(x, z)
		var wide := random.randf_range(0.62, 1.85)
		var high := random.randf_range(0.19, 0.61)
		var basis := Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3(wide, high, wide * random.randf_range(0.75, 1.45)))
		rocks.set_instance_transform(i, Transform3D(basis, Vector3(x, y - 0.08, z)))
		var tint := 0.88 + 0.18 * random.randf()
		rocks.set_instance_color(i, Color(tint, tint * 0.94, tint * 0.81))
	_add_group("AuthoredLimestone", rocks, "rock", "seeded illustrative limestone stones on sampled USGS terrain")
	stats["limestone_stones"] = count


func _rock_mesh() -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	var sides := 9
	for ring in range(4):
		var height: float = [0.0, 0.20, 0.72, 1.0][ring]
		var radius: float = [0.78, 1.13, 1.09, 0.75][ring]
		for side in range(sides + 1):
			var angle := TAU * float(side) / sides
			var point := Vector3(cos(angle) * radius * (1.0 + 0.09 * sin(float(side * 11 + ring * 3))), height + 0.045 * sin(float(side * 9 + ring * 4)), sin(angle) * radius)
			vertices.append(point)
			normals.append(Vector3(point.x * 0.35, 0.52 if ring > 1 else 0.12, point.z * 0.35).normalized())
			var shade: float = [0.54, 0.63, 0.87, 1.0][ring]
			colors.append(Color(shade, shade * 0.96, shade * 0.84))
	for ring in range(3):
		for side in range(sides):
			var a := ring * (sides + 1) + side
			indices.append_array(PackedInt32Array([a, a + sides + 1, a + 1, a + 1, a + sides + 1, a + sides + 2]))
	vertices.append(Vector3(0.0, 1.03, 0.0))
	normals.append(Vector3.UP)
	colors.append(Color(1.02, 0.99, 0.90))
	for side in range(sides):
		indices.append_array(PackedInt32Array([vertices.size() - 1, 3 * (sides + 1) + side, 3 * (sides + 1) + side + 1]))
	return _mesh(vertices, normals, indices, colors)


func _make_grass() -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	for blade in range(4):
		var angle := TAU * float(blade) / 4.0
		var side := Vector3(-sin(angle), 0.0, cos(angle))
		var base := vertices.size()
		vertices.append(-side * 0.15)
		vertices.append(side * 0.15)
		vertices.append(side * 0.095 + Vector3(cos(angle) * 0.12, 0.39, sin(angle) * 0.12))
		vertices.append(Vector3(cos(angle) * 0.23, 0.56 - blade * 0.05, sin(angle) * 0.23))
		for j in range(4):
			normals.append(Vector3(cos(angle), 0.28, sin(angle)))
			var tone := 0.70 if j < 2 else 0.93
			colors.append(Color(tone * 0.74, tone * 0.86, tone * 0.66))
		indices.append_array(PackedInt32Array([base, base + 1, base + 2, base, base + 2, base + 3]))
	var count := 85 if profile == "low" else 450
	var group := _group(_mesh(vertices, normals, indices, colors), count, true)
	var random := RandomNumberGenerator.new()
	random.seed = 4839042
	for i in range(count):
		var foreground := i < int(count * 0.65)
		var x := random.randf_range(-11.0, 11.0) if foreground else random.randf_range(-34.0, 34.0)
		var z := random.randf_range(331.0, 362.0) if foreground else random.randf_range(317.0, 383.0)
		if absf(x) < 3.0 and z > 336.0 and z < 357.0:
			x += 4.0 if x >= 0.0 else -4.0
		var size := random.randf_range(0.31, 0.68)
		group.set_instance_transform(i, Transform3D(Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3(size, size, size)), Vector3(x, viewer.map_runtime.surface_height_at(x, z) + 0.03, z)))
		var warm := random.randf_range(0.76, 0.99)
		group.set_instance_color(i, Color(warm, 0.80 + 0.14 * random.randf(), 0.78 + 0.16 * random.randf()))
	_add_group("AuthoredGrass", group, "grass", "seeded illustrative grass tufts, no alpha texture")
	stats["grass_tufts"] = count


func _group(mesh: Mesh, count: int, with_colors := false) -> MultiMesh:
	var group := MultiMesh.new()
	group.transform_format = MultiMesh.TRANSFORM_3D
	group.use_colors = with_colors
	group.mesh = mesh
	group.instance_count = count
	return group


func _add_group(name: String, group: MultiMesh, role: String, provenance: String) -> void:
	var instance := MultiMeshInstance3D.new()
	instance.name = name
	instance.multimesh = group
	instance.material_override = _materials[role]
	instance.set_meta("provenance", provenance)
	add_child(instance)


func _mesh(vertices: PackedVector3Array, normals: PackedVector3Array, indices: PackedInt32Array, colors := PackedColorArray()) -> ArrayMesh:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	if not indices.is_empty():
		arrays[Mesh.ARRAY_INDEX] = indices
	if not colors.is_empty():
		arrays[Mesh.ARRAY_COLOR] = colors
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _triangle_count(node: Node) -> int:
	var count := 0
	if node is MultiMeshInstance3D:
		count += int(node.multimesh.mesh.get_faces().size() / 3) * node.multimesh.instance_count
	elif node is MeshInstance3D:
		count += int(node.mesh.get_faces().size() / 3)
	for child in node.get_children():
		count += _triangle_count(child)
	return count
