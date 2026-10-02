extends Node3D
## ART-3: one composed, editable 30 m study around the existing local plot.
## Its geographic surface is the pinned map; plantings and the rain garden are
## original illustrations and are not mapped Barton Creek or surveyed trees.

const ASSETS := preload("res://scripts/painterly_assets.gd")
const GROUND_KIT := preload("res://scripts/painterly_ground_kit.gd")
const CENTER := Vector2(0.0, 350.0)
const GROUND_SEED := 3010250924
const TREE_SITES := [
	{"id": "canopy_left", "species": "oak", "xz": Vector2(-7.0, 348.0), "scale": 1.04, "yaw": 0.43},
	{"id": "canopy_right", "species": "oak", "xz": Vector2(9.0, 356.0), "scale": 0.96, "yaw": -0.77},
	{"id": "juniper_left", "species": "juniper", "xz": Vector2(-9.0, 365.0), "scale": 0.94, "yaw": 0.30},
	{"id": "grove_right", "species": "oak", "xz": Vector2(4.0, 369.0), "scale": 0.82, "yaw": 1.01},
	{"id": "juniper_right", "species": "juniper", "xz": Vector2(15.0, 361.0), "scale": 0.78, "yaw": -0.49},
	{"id": "grove_left", "species": "oak", "xz": Vector2(-17.0, 358.0), "scale": 0.77, "yaw": 1.26},
	{"id": "back_oak", "species": "oak", "xz": Vector2(1.0, 378.0), "scale": 0.72, "yaw": -0.51},
	{"id": "edge_juniper", "species": "juniper", "xz": Vector2(-16.0, 335.0), "scale": 0.70, "yaw": 0.84},
	{"id": "back_oak_right", "species": "oak", "xz": Vector2(25.0, 376.0), "scale": 0.67, "yaw": 0.15},
]
const PLANTING_CLUSTERS := [
	Vector2(-7.5, 338.0), Vector2(-12.0, 342.0), Vector2(-8.0, 347.0),
	Vector2(-15.0, 351.0), Vector2(-10.0, 357.0), Vector2(-7.5, 363.0),
	Vector2(-13.0, 369.0), Vector2(-20.0, 361.0),
	Vector2(7.5, 338.0), Vector2(9.0, 343.0), Vector2(8.0, 350.0),
	Vector2(17.5, 348.0), Vector2(8.5, 359.0), Vector2(17.0, 363.0),
	Vector2(7.0, 367.0), Vector2(21.0, 369.0),
]
const LEDGE_CLUSTERS := [
	{"xz": Vector2(-9.5, 353.0), "yaw": 0.19},
	{"xz": Vector2(16.5, 357.0), "yaw": -0.39},
	{"xz": Vector2(-12.5, 367.0), "yaw": 0.63},
]

var viewer: Node3D
var profile := "standard"
var stats: Dictionary = {}


func configure(map_viewer: Node3D) -> void:
	viewer = map_viewer


func _ready() -> void:
	if viewer == null or viewer.map_runtime == null:
		push_error("Painterly workshop needs a loaded map viewer")
		return
	profile = "low" if OS.get_environment("ENFRACTAL_ART_PROFILE") == "low" else "standard"
	set_meta("source_map", "barton_creek_v0")
	set_meta("source_style", ASSETS.STYLE_VERSION)
	set_meta("interpretation", "original illustrative planting and seasonal rain garden around a source-height plot")
	set_meta("geographic_center_local_xz_m", CENTER)
	_make_trees()
	_make_groundcover()
	_make_limestone_ledges()
	_make_seasonal_rill()
	stats["triangle_upper_bound"] = ASSETS.triangle_count(self)
	stats["source_terrain_modified"] = false
	print("Painterly workshop composition: ", profile, " ", stats)


func _make_trees() -> void:
	# The low profile keeps every major silhouette. ART-4 must provide measured
	# asset LODs before a device tier may simplify the tree architecture.
	var count := TREE_SITES.size()
	for index in range(count):
		var site: Dictionary = TREE_SITES[index]
		var species: String = site["species"]
		var at: Vector2 = site["xz"]
		var tree: Node3D = ASSETS.make_tree(species, true)
		tree.name = "IllustrativeTree_" + str(site["id"])
		tree.position = Vector3(at.x, viewer.map_runtime.surface_height_at(at.x, at.y) - 0.07, at.y)
		tree.rotation.y = float(site["yaw"])
		tree.scale = Vector3.ONE * float(site["scale"])
		tree.set_meta("site_id", site["id"])
		tree.set_meta("source_height_m", viewer.map_runtime.surface_height_at(at.x, at.y))
		add_child(tree)
	stats["tree_centers"] = count
	stats["asset_tree_triangles"] = ASSETS.triangle_count(self)


func _make_groundcover() -> void:
	var card := _groundcover_card_mesh()
	var per_cluster := 42 if profile == "low" else 102
	var count := PLANTING_CLUSTERS.size() * per_cluster * 2
	var instances := MultiMesh.new()
	instances.transform_format = MultiMesh.TRANSFORM_3D
	instances.mesh = card
	instances.instance_count = count
	var random := RandomNumberGenerator.new()
	random.seed = GROUND_SEED
	var slot := 0
	for cluster_index in range(PLANTING_CLUSTERS.size()):
		var center: Vector2 = PLANTING_CLUSTERS[cluster_index]
		for tuft in range(per_cluster):
			var angle := random.randf_range(0.0, TAU)
			var radius := sqrt(random.randf()) * (2.45 + float(cluster_index % 3) * 0.45)
			var at := center + Vector2(cos(angle), sin(angle)) * radius
			# Keep the central edit, spawn and its clear approach visually quiet.
			if absf(at.x) < 3.5 and at.y > 332.0 and at.y < 363.0:
				at.x = 3.5 if at.x >= 0.0 else -3.5
			var ground: float = viewer.map_runtime.surface_height_at(at.x, at.y)
			var height := random.randf_range(0.40, 0.78)
			var width := random.randf_range(0.50, 0.95)
			var yaw := random.randf_range(0.0, TAU)
			for cross in range(2):
				var basis := Basis(Vector3.UP, yaw + float(cross) * PI * 0.5)
				basis = basis.rotated(basis.x, -0.13)
				basis = basis.scaled(Vector3(width, height, 1.0))
				instances.set_instance_transform(slot, Transform3D(basis, Vector3(at.x, ground - 0.025, at.y)))
				slot += 1
	var patch := MultiMeshInstance3D.new()
	patch.name = "PaintedGroundcoverClusters"
	patch.multimesh = instances
	patch.material_override = ASSETS.groundcover_material()
	patch.set_meta("provenance", "original illustrative planted clusters; no surveyed vegetation")
	patch.set_meta("seed", GROUND_SEED)
	add_child(patch)
	stats["groundcover_clusters"] = PLANTING_CLUSTERS.size()
	stats["groundcover_cards"] = count


func _groundcover_card_mesh() -> ArrayMesh:
	# The original grass paint is rooted at V=1. Color channels carry the same
	# occlusion/wind/variation data contract used by the tree foliage shader.
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = PackedVector3Array([
		Vector3(-0.32, 0.0, 0.0), Vector3(0.32, 0.0, 0.0),
		Vector3(0.32, 0.72, 0.0), Vector3(-0.32, 0.72, 0.0),
	])
	arrays[Mesh.ARRAY_NORMAL] = PackedVector3Array([Vector3.BACK, Vector3.BACK, Vector3.BACK, Vector3.BACK])
	arrays[Mesh.ARRAY_TEX_UV] = PackedVector2Array([Vector2(0.0, 1.0), Vector2(1.0, 1.0), Vector2(1.0, 0.0), Vector2(0.0, 0.0)])
	arrays[Mesh.ARRAY_COLOR] = PackedColorArray([
		Color(0.85, 0.0, 0.50, 1.0), Color(0.85, 0.0, 0.50, 1.0),
		Color(0.96, 1.0, 0.50, 1.0), Color(0.96, 1.0, 0.50, 1.0),
	])
	arrays[Mesh.ARRAY_INDEX] = PackedInt32Array([0, 3, 1, 1, 3, 2])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _make_limestone_ledges() -> void:
	var stone_material := GROUND_KIT.make_surface_material("limestone")
	var stone_count := 0
	for cluster_index in range(LEDGE_CLUSTERS.size()):
		var cluster: Dictionary = LEDGE_CLUSTERS[cluster_index]
		var center: Vector2 = cluster["xz"]
		var assembly := Node3D.new()
		assembly.name = "IllustrativeLedgeCluster_%d" % cluster_index
		assembly.set_meta("provenance", "original illustrative limestone ledge; USGS heights are unchanged")
		add_child(assembly)
		var per_cluster := 4
		for index in range(per_cluster):
			var x := center.x + (float(index) - 1.4) * (1.46 + 0.19 * sin(float(cluster_index)))
			var z := center.y + 0.87 * sin(float(index) * 1.8 + float(cluster_index))
			var dimensions := Vector3(1.20 + 0.42 * float((index + cluster_index) % 3) / 2.0, 0.16 + 0.045 * float(index % 3), 0.75 + 0.25 * float((index + 2) % 3) / 2.0)
			var piece := GROUND_KIT.make_limestone_piece(dimensions, 4102 + cluster_index * 11 + index, stone_material)
			piece.position = Vector3(x, viewer.map_runtime.surface_height_at(x, z) - 0.06, z)
			piece.rotation.y = float(cluster["yaw"]) + 0.12 * index
			assembly.add_child(piece)
			stone_count += 1
	stats["limestone_stones"] = stone_count


func _make_seasonal_rill() -> void:
	# Seasonal stormwater garden inside the plot. This is deliberately away
	# from the authored path and is not a reconstruction of mapped Barton Creek.
	var centerline := PackedVector2Array([
		Vector2(14.0, 333.0), Vector2(13.4, 338.0), Vector2(14.5, 344.0),
		Vector2(12.5, 350.0), Vector2(13.3, 356.0), Vector2(11.6, 362.0),
		Vector2(13.0, 368.0),
	])
	var points := PackedVector3Array()
	for at in centerline:
		points.push_back(Vector3(at.x, viewer.map_runtime.surface_height_at(at.x, at.y), at.y))
	var rill := Node3D.new()
	rill.name = "IllustrativeSeasonalRainGarden"
	rill.set_meta("provenance", "illustrative seasonal rain garden; not mapped Barton Creek or surveyed water")
	rill.set_meta("source_height", "barton_creek_v0 USGS sample grid; unmodified")
	add_child(rill)
	var bank: Node3D = GROUND_KIT.make_creek_bank(points, 0.80, 82131, Callable(viewer.map_runtime, "surface_height_at"), false)
	bank.name = "IllustrativeRainGardenBanks"
	bank.set_meta("provenance", "illustrative seasonal rain-garden banks, not mapped Barton Creek")
	rill.add_child(bank)
	var water := MeshInstance3D.new()
	water.name = "IllustrativeShallowSeasonalWater"
	var smooth_centerline := _smoothed_rill_centerline(centerline)
	water.mesh = _make_rill_water(smooth_centerline)
	var water_material := StandardMaterial3D.new()
	water_material.resource_name = "Seasonal rain-garden water study"
	water_material.albedo_color = Color(0.36, 0.47, 0.43, 0.48)
	water_material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	water_material.roughness = 0.78
	water_material.metallic_specular = 0.14
	water_material.cull_mode = BaseMaterial3D.CULL_BACK
	water.material_override = water_material
	water.set_meta("provenance", "illustrative shallow seasonal water following source heights")
	rill.add_child(water)
	_make_rill_bank_detail(rill, smooth_centerline)
	stats["seasonal_rill_points"] = centerline.size()
	stats["seasonal_rill_is_mapped_creek"] = false


func _make_rill_water(points: PackedVector2Array) -> ArrayMesh:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var indices := PackedInt32Array()
	for index in range(points.size()):
		var at := points[index]
		var before := points[maxi(index - 1, 0)]
		var after := points[mini(index + 1, points.size() - 1)]
		var lateral := (after - before).normalized().orthogonal()
		var half_width := 0.28 + 0.045 * sin(float(index) * 0.47)
		for side in [-1.0, 1.0]:
			var shore: Vector2 = at + lateral * half_width * float(side)
			vertices.push_back(Vector3(shore.x, viewer.map_runtime.surface_height_at(shore.x, shore.y) + 0.045, shore.y))
			normals.push_back(Vector3.UP)
		if index > 0:
			var base := (index - 1) * 2
			# Godot's clockwise top face; back faces remain culled.
			indices.append_array(PackedInt32Array([base, base + 1, base + 2, base + 1, base + 3, base + 2]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _make_rill_bank_detail(rill: Node3D, points: PackedVector2Array) -> void:
	# Small, scattered wet limestone and sedge soften the narrow water edge.
	# Every contact samples the existing terrain; none alters the height grid.
	var random := RandomNumberGenerator.new()
	random.seed = 82132
	var sites := range(1, points.size() - 1, 3)
	var pebbles := MultiMesh.new()
	pebbles.transform_format = MultiMesh.TRANSFORM_3D
	pebbles.mesh = GROUND_KIT.make_limestone_mesh(Vector3(0.31, 0.10, 0.25), 82132)
	pebbles.instance_count = sites.size() * 2
	var tuft_per_bank := 1 if profile == "low" else 3
	var grasses := MultiMesh.new()
	grasses.transform_format = MultiMesh.TRANSFORM_3D
	grasses.mesh = _groundcover_card_mesh()
	grasses.instance_count = sites.size() * 2 * tuft_per_bank * 2
	var rock_slot := 0
	var grass_slot := 0
	for index in sites:
		var tangent: Vector2 = (points[index + 1] - points[index - 1]).normalized()
		var lateral: Vector2 = tangent.orthogonal()
		for side in [-1.0, 1.0]:
			var pebble_at: Vector2 = points[index] + lateral * float(side) * random.randf_range(0.56, 0.92) + tangent * random.randf_range(-0.28, 0.28)
			var pebble_y: float = viewer.map_runtime.surface_height_at(pebble_at.x, pebble_at.y)
			var rock_basis := Basis(Vector3.UP, random.randf_range(0.0, TAU)).scaled(Vector3.ONE * random.randf_range(0.68, 1.22))
			pebbles.set_instance_transform(rock_slot, Transform3D(rock_basis, Vector3(pebble_at.x, pebble_y - 0.025, pebble_at.y)))
			rock_slot += 1
			for tuft in range(tuft_per_bank):
				var plant_at: Vector2 = points[index] + lateral * float(side) * random.randf_range(1.00, 1.57) + tangent * random.randf_range(-0.45, 0.45)
				var plant_y: float = viewer.map_runtime.surface_height_at(plant_at.x, plant_at.y)
				var height := random.randf_range(0.29, 0.54)
				var width := random.randf_range(0.40, 0.66)
				var yaw := random.randf_range(0.0, TAU)
				for cross in range(2):
					var basis := Basis(Vector3.UP, yaw + float(cross) * PI * 0.5).scaled(Vector3(width, height, 1.0))
					grasses.set_instance_transform(grass_slot, Transform3D(basis, Vector3(plant_at.x, plant_y - 0.025, plant_at.y)))
					grass_slot += 1
	var rock_instance := MultiMeshInstance3D.new()
	rock_instance.name = "RainGardenScatteredPebbles"
	rock_instance.multimesh = pebbles
	rock_instance.material_override = GROUND_KIT.make_surface_material("wet_limestone")
	rock_instance.set_meta("provenance", "original illustrative pebbles, not surveyed water-bank objects")
	rill.add_child(rock_instance)
	var grass_instance := MultiMeshInstance3D.new()
	grass_instance.name = "RainGardenBankTufts"
	grass_instance.multimesh = grasses
	grass_instance.material_override = ASSETS.groundcover_material()
	grass_instance.set_meta("provenance", "original illustrative seasonal bank planting")
	rill.add_child(grass_instance)
	stats["bank_pebbles"] = pebbles.instance_count
	stats["bank_grass_cards"] = grasses.instance_count


func _smoothed_rill_centerline(points: PackedVector2Array) -> PackedVector2Array:
	var result := PackedVector2Array()
	for index in range(points.size() - 1):
		var p0 := points[maxi(0, index - 1)]
		var p1 := points[index]
		var p2 := points[index + 1]
		var p3 := points[mini(points.size() - 1, index + 2)]
		for step in range(6):
			var t := float(step) / 6.0
			var t2 := t * t
			var t3 := t2 * t
			result.push_back(0.5 * ((2.0 * p1) + (p2 - p0) * t + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3))
	result.push_back(points[points.size() - 1])
	return result
