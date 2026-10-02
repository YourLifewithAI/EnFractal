extends Node3D
## Small, illustrative near-field study around the developer workshop plot.
## Map terrain stays authoritative; no creek or surveyed trees are invented here.

const TREE_SITES := [
	Vector2(-14.0, 326.0), Vector2(15.0, 324.0), Vector2(-23.0, 340.0),
	Vector2(22.0, 344.0), Vector2(-18.0, 358.0), Vector2(18.0, 359.0),
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
	set_meta("interpretation", "illustrative fixed-seed trees, limestone and grass; no surveyed tree claim")
	_make_materials()
	_make_trees()
	_make_rocks()
	_make_grass()
	_refresh_style()
	stats["triangle_upper_bound"] = _triangle_count(self)


func _process(_delta: float) -> void:
	if viewer != null and viewer.style_index != _last_style_index:
		_refresh_style()


func _make_materials() -> void:
	for role in ["trunk", "canopy", "rock", "grass"]:
		var material := StandardMaterial3D.new()
		material.roughness = 1.0
		material.cull_mode = BaseMaterial3D.CULL_DISABLED
		material.vertex_color_use_as_albedo = role == "canopy"
		_materials[role] = material


func _refresh_style() -> void:
	_last_style_index = viewer.style_index
	var palette: Dictionary = viewer.styles[_last_style_index]["objects"]
	for role in _materials:
		var rgb: Array = palette["canopy"] if role == "grass" else palette[role]
		var tint := Color8(int(rgb[0]), int(rgb[1]), int(rgb[2]))
		if role == "canopy":
			tint = tint.lightened(0.21)
		elif role == "grass":
			var greens: Array = palette["canopy"]
			tint = Color8(int(greens[0]), int(greens[1]), int(greens[2])).lightened(0.18)
		elif role == "rock":
			tint = tint.lightened(0.12)
		_materials[role].albedo_color = tint


func _make_trees() -> void:
	var trunk_mesh := CylinderMesh.new()
	trunk_mesh.top_radius = 0.49
	trunk_mesh.bottom_radius = 0.88
	trunk_mesh.height = 1.0
	trunk_mesh.radial_segments = 7
	var trunks := _group(trunk_mesh, TREE_SITES.size())
	var branch_count := 0 if profile == "low" else TREE_SITES.size() * 2
	var branches := _group(trunk_mesh, branch_count)
	var crown_mesh := _crown_mesh()
	var lobes_per_tree := 1 if profile == "low" else 3
	var crowns := _group(crown_mesh, TREE_SITES.size() * lobes_per_tree)
	for i in range(TREE_SITES.size()):
		var site: Vector2 = TREE_SITES[i]
		var ground: float = viewer.map_runtime.surface_height_at(site.x, site.y)
		var cedar := i % 4 == 1
		var height := 8.4 + fposmod(float(i * 13), 5.0) * 0.75
		var stem := height * (0.49 if cedar else 0.35)
		var radius := height * (0.31 if cedar else 0.44)
		trunks.set_instance_transform(i, Transform3D(Basis.IDENTITY.scaled(Vector3(0.28, stem, 0.28)), Vector3(site.x, ground + stem * 0.5, site.y)))
		if profile != "low":
			for branch in range(2):
				var angle := float(i * 5 + branch * 7) * 2.39996
				var outward := Vector3(cos(angle) * radius * 0.43, stem * 0.25, sin(angle) * radius * 0.43)
				var basis := Basis(Quaternion(Vector3.UP, outward.normalized())).scaled(Vector3(0.13, outward.length(), 0.13))
				branches.set_instance_transform(i * 2 + branch, Transform3D(basis, Vector3(site.x, ground + stem * 0.78, site.y) + outward * 0.5))
		for lobe in range(lobes_per_tree):
			var angle := float(i * 17 + lobe * 7) * 2.39996
			var spread := 0.0 if lobe == 0 else radius * 0.75
			var size := radius * (0.82 if lobe == 0 else 0.69)
			var vertical := size * (0.90 if cedar else 0.52)
			var crown_lift := 0.20 * radius * sin(float(i * 5 + lobe * 3))
			var center := Vector3(site.x + cos(angle) * spread, ground + stem + vertical * (0.20 if cedar else 0.28) + crown_lift, site.y + sin(angle) * spread)
			crowns.set_instance_transform(i * lobes_per_tree + lobe, Transform3D(Basis(Vector3.UP, angle).scaled(Vector3(size, vertical, size * 0.85)), center))
	_add_group("AuthoredTrunks", trunks, "trunk", "illustrative centers near the developer plot, not individual mapped trees")
	if profile != "low":
		_add_group("AuthoredBranches", branches, "trunk", "illustrative broad-oak branch forms")
	_add_group("SculptedCrowns", crowns, "canopy", "illustrative oak/cedar-like silhouettes, shared mesh")
	stats["tree_centers"] = TREE_SITES.size()
	stats["canopy_lobes"] = crowns.instance_count


func _crown_mesh() -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	var columns := 12
	var rows := 7
	for row in range(rows + 1):
		var latitude := PI * float(row) / rows
		for column in range(columns + 1):
			var longitude := TAU * float(column) / columns
			var ripple := 1.0 + 0.095 * sin(longitude * 5.0 + latitude * 1.7) + 0.055 * cos(longitude * 9.0 - latitude * 2.0)
			var ring := maxf(0.001, sin(latitude)) * ripple
			var vertex := Vector3(cos(longitude) * ring, cos(latitude) * (1.0 + 0.03 * sin(longitude * 7.0)), sin(longitude) * ring)
			vertices.append(vertex)
			normals.append(Vector3(vertex.x, vertex.y * 0.68, vertex.z).normalized())
			var tone := clampf(0.72 + 0.19 * maxf(vertex.y, 0.0) + 0.04 * sin(longitude * 4.0 + latitude * 3.0), 0.67, 0.98)
			colors.append(Color(tone, tone, tone))
	for row in range(rows):
		for column in range(columns):
			var a := row * (columns + 1) + column
			var b := a + 1
			var c := a + columns + 1
			indices.append_array(PackedInt32Array([a, c, b, b, c, c + 1]))
	return _mesh(vertices, normals, indices, colors)


func _make_rocks() -> void:
	var count := 13 if profile == "low" else 34
	var rocks := _group(_rock_mesh(), count)
	var random := RandomNumberGenerator.new()
	random.seed = 33081264
	for i in range(count):
		var x := random.randf_range(-27.0, 27.0)
		var z := random.randf_range(320.0, 381.0)
		if absf(x) < 5.5 and z > 335.0 and z < 357.0:
			x += 7.0 if x >= 0.0 else -7.0
		var y: float = viewer.map_runtime.surface_height_at(x, z)
		var wide := random.randf_range(0.55, 1.65)
		var high := random.randf_range(0.19, 0.68)
		var basis := Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3(wide, high, wide * random.randf_range(0.75, 1.45)))
		rocks.set_instance_transform(i, Transform3D(basis, Vector3(x, y - 0.08, z)))
	_add_group("AuthoredLimestone", rocks, "rock", "seeded illustrative limestone stones on sampled USGS terrain")
	stats["limestone_stones"] = count


func _rock_mesh() -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	var sides := 8
	for ring in range(3):
		var height: float = [0.0, 0.56, 1.0][ring]
		var radius: float = [0.89, 1.12, 0.61][ring]
		for side in range(sides + 1):
			var angle := TAU * float(side) / sides
			var point := Vector3(cos(angle) * radius * (1.0 + 0.09 * sin(float(side * 11 + ring * 3))), height, sin(angle) * radius)
			vertices.append(point)
			normals.append(Vector3(point.x, 0.28, point.z).normalized())
	for ring in range(2):
		for side in range(sides):
			var a := ring * (sides + 1) + side
			indices.append_array(PackedInt32Array([a, a + sides + 1, a + 1, a + 1, a + sides + 1, a + sides + 2]))
	vertices.append(Vector3(0.0, 1.05, 0.0))
	normals.append(Vector3.UP)
	for side in range(sides):
		indices.append_array(PackedInt32Array([vertices.size() - 1, 2 * (sides + 1) + side, 2 * (sides + 1) + side + 1]))
	return _mesh(vertices, normals, indices)


func _make_grass() -> void:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for blade in range(3):
		var angle := TAU * float(blade) / 3.0
		var side := Vector3(-sin(angle), 0.0, cos(angle))
		var base := vertices.size()
		vertices.append(-side * 0.15)
		vertices.append(side * 0.15)
		vertices.append(Vector3(cos(angle) * 0.10, 0.58 - blade * 0.05, sin(angle) * 0.10))
		for j in range(3):
			normals.append(Vector3(cos(angle), 0.15, sin(angle)))
		indices.append_array(PackedInt32Array([base, base + 1, base + 2]))
	var count := 80 if profile == "low" else 320
	var group := _group(_mesh(vertices, normals, indices), count)
	var random := RandomNumberGenerator.new()
	random.seed = 4839042
	for i in range(count):
		var foreground := i < int(count * 0.65)
		var x := random.randf_range(-11.0, 11.0) if foreground else random.randf_range(-34.0, 34.0)
		var z := random.randf_range(331.0, 362.0) if foreground else random.randf_range(317.0, 383.0)
		if absf(x) < 3.0 and z > 336.0 and z < 357.0:
			x += 4.0 if x >= 0.0 else -4.0
		var size := random.randf_range(0.17, 0.29)
		group.set_instance_transform(i, Transform3D(Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3(size, size, size)), Vector3(x, viewer.map_runtime.surface_height_at(x, z) + 0.03, z)))
	_add_group("AuthoredGrass", group, "grass", "seeded illustrative grass tufts, no alpha texture")
	stats["grass_tufts"] = count


func _group(mesh: Mesh, count: int) -> MultiMesh:
	var group := MultiMesh.new()
	group.transform_format = MultiMesh.TRANSFORM_3D
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
	for child in node.get_children():
		count += _triangle_count(child)
	return count
