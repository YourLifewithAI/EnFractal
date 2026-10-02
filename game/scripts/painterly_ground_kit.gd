extends RefCounted
## Authored material vocabulary and bounded illustrative bank geometry.
## Visual children are replaceable; callers retain semantic IDs and collision.

const GROUND_TEXTURE := "res://assets/art/barton/textures/ground-paint-v1.png"
const LIMESTONE_TEXTURE := "res://assets/art/barton/textures/limestone-paint-v1.png"
const MAX_BANK_POINTS := 128
const TERRAIN_SOURCE_PARAMETERS := [
	"photo_overlay", "low_color", "high_color", "steep_color", "limestone_color",
	"trail_soil_color", "pavement_color", "color_bands", "mottle_strength",
	"photo_strength", "map_side_m",
]


static func make_terrain_material(origin_xz := Vector2.ZERO) -> ShaderMaterial:
	var material := ShaderMaterial.new()
	material.resource_name = "Barton painted geographic terrain v1"
	material.shader = load("res://shaders/painterly_ground.gdshader")
	material.set_shader_parameter("ground_paint", _texture_or_white(GROUND_TEXTURE))
	material.set_shader_parameter("geographic_origin_xz", origin_xz)
	# No imagery is an all-zero evidence mask, not the GPU's white fallback.
	material.set_shader_parameter("photo_overlay", _solid_texture(Color.BLACK))
	return material


static func copy_terrain_source_parameters(target: ShaderMaterial, source: ShaderMaterial) -> void:
	if source == null:
		return
	for parameter in TERRAIN_SOURCE_PARAMETERS:
		var value = source.get_shader_parameter(parameter)
		if value != null:
			target.set_shader_parameter(parameter, value)


static func make_surface_material(role := "limestone") -> ShaderMaterial:
	var material := ShaderMaterial.new()
	material.resource_name = "Barton painted %s v1" % role
	material.shader = load("res://shaders/painterly_surface.gdshader")
	var texture_path := LIMESTONE_TEXTURE if ResourceLoader.exists(LIMESTONE_TEXTURE) else GROUND_TEXTURE
	material.set_shader_parameter("surface_paint", _texture_or_white(texture_path))
	if role == "soil":
		material.set_shader_parameter("base_color", Color("9d8056"))
		material.set_shader_parameter("shade_color", Color("695b41"))
		material.set_shader_parameter("strata_strength", 0.0)
		material.set_shader_parameter("material_roughness", 0.98)
	elif role == "wet_limestone":
		material.set_shader_parameter("base_color", Color("a39b77"))
		material.set_shader_parameter("shade_color", Color("676951"))
		material.set_shader_parameter("material_roughness", 0.68)
	return material


static func make_limestone_piece(size: Vector3, seed_value: int, material: Material = null) -> MeshInstance3D:
	var instance := MeshInstance3D.new()
	instance.name = "IllustrativeLimestone_%d" % seed_value
	instance.mesh = make_limestone_mesh(size, seed_value)
	instance.material_override = material if material != null else make_surface_material()
	instance.set_meta("provenance", "original illustrative limestone; not surveyed geometry")
	instance.set_meta("generator", "barton_limestone_v1")
	instance.set_meta("seed", seed_value)
	return instance


static func make_limestone_mesh(size: Vector3, seed_value: int) -> ArrayMesh:
	var safe_size := Vector3(maxf(size.x, 0.05), maxf(size.y, 0.03), maxf(size.z, 0.05))
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var faces := 9
	var ring_factors := [0.83, 1.0, 0.96, 0.84]
	var ring_heights := [0.0, 0.13, 0.78, 1.0]
	var footprint := PackedVector2Array()
	for side in range(faces):
		var angle := TAU * float(side) / faces
		var radius := rng.randf_range(0.89, 1.06)
		footprint.push_back(Vector2(cos(angle) * safe_size.x, sin(angle) * safe_size.z) * radius * 0.5)
	var rings: Array[PackedVector3Array] = []
	for level in range(ring_factors.size()):
		var ring := PackedVector3Array()
		for side in range(faces):
			var point := footprint[side] * float(ring_factors[level])
			var height := safe_size.y * float(ring_heights[level])
			if level == 3:
				height += rng.randf_range(-0.055, 0.035) * safe_size.y
			ring.push_back(Vector3(point.x, height, point.y))
		rings.push_back(ring)
	var output := _new_arrays()
	for level in range(rings.size() - 1):
		for side in range(faces):
			var next := (side + 1) % faces
			var normal_hint := Vector3(footprint[side].x + footprint[next].x, 0.0, footprint[side].y + footprint[next].y).normalized()
			var tone := rng.randf_range(0.91, 1.04)
			var shade := Color(tone, tone * 0.992, tone * 0.971, 0.72 + 0.09 * level)
			_append_triangle(output, rings[level][side], rings[level][next], rings[level + 1][side], normal_hint, shade)
			_append_triangle(output, rings[level][next], rings[level + 1][next], rings[level + 1][side], normal_hint, shade)
	for side in range(faces):
		var next := (side + 1) % faces
		_append_triangle(output, Vector3(0.0, safe_size.y, 0.0), rings[3][side], rings[3][next], Vector3.UP, Color(1.02, 1.01, 0.98, 1.0))
		_append_triangle(output, Vector3.ZERO, rings[0][side], rings[0][next], Vector3.DOWN, Color(0.88, 0.88, 0.85, 0.7))
	return _finish_mesh(output)


static func make_creek_bank(points: PackedVector3Array, half_width := 1.5, seed_value := 1, height_sampler := Callable(), include_bank_surface := false) -> Node3D:
	var root := Node3D.new()
	root.name = "IllustrativeCreekBank"
	root.set_meta("provenance", "illustrative creek-bank treatment; caller must anchor to mapped creek")
	root.set_meta("generator", "barton_creek_bank_v1")
	root.set_meta("seed", seed_value)
	if points.size() < 2 or points.size() > MAX_BANK_POINTS:
		push_warning("Painterly bank requires 2–128 source-anchored points.")
		return root
	var width := clampf(half_width, 0.3, 12.0)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var rock_material := make_surface_material("wet_limestone")
	var soil_material := make_surface_material("soil")
	for bank_side in [-1.0, 1.0]:
		var arrays := _new_arrays()
		var last_inner := Vector3.ZERO
		var last_outer := Vector3.ZERO
		for index in range(points.size()):
			var tangent := points[mini(index + 1, points.size() - 1)] - points[maxi(index - 1, 0)]
			tangent.y = 0.0
			if tangent.length_squared() < 0.00001:
				tangent = Vector3.FORWARD
			var side_direction := Vector3(-tangent.z, 0.0, tangent.x).normalized() * float(bank_side)
			var inner := points[index] + side_direction * width
			var outer := points[index] + side_direction * (width + rng.randf_range(0.6, 1.2))
			if height_sampler.is_valid():
				inner.y = float(height_sampler.call(inner.x, inner.z))
				outer.y = float(height_sampler.call(outer.x, outer.z))
			else:
				# Centerline elevations describe the shoreline; a later integration
				# must supply the real terrain sampler before this is placed there.
				outer.y += 0.18
			if index > 0:
				_append_triangle(arrays, last_inner, inner, last_outer, Vector3.UP, Color.WHITE)
				_append_triangle(arrays, inner, outer, last_outer, Vector3.UP, Color.WHITE)
			last_inner = inner
			last_outer = outer
			if index % 2 == 0:
				var piece := make_limestone_piece(Vector3(rng.randf_range(0.65, 1.2), rng.randf_range(0.16, 0.36), rng.randf_range(0.5, 0.9)), seed_value + index + int((bank_side + 1.0) * 100.0), rock_material)
				piece.position = inner.lerp(outer, 0.38) - Vector3(0.0, 0.04, 0.0)
				piece.rotation.y = rng.randf_range(-PI, PI)
				root.add_child(piece)
		# A soil ribbon may only replace an authored bank's surface; laying it
		# directly over existing terrain produces z-fighting and float seams.
		# Source-terrain integration defaults to embedded stones only, with
		# soil transitions painted by the terrain's own material.
		if include_bank_surface:
			var bank := MeshInstance3D.new()
			bank.name = "BankSoilLeft" if bank_side < 0.0 else "BankSoilRight"
			bank.mesh = _finish_mesh(arrays)
			bank.material_override = soil_material
			root.add_child(bank)
	return root


static func _texture_or_white(path: String) -> Texture2D:
	if ResourceLoader.exists(path):
		return load(path) as Texture2D
	# Placeholder is conspicuously plain; it is not presented as painted art.
	return _solid_texture(Color.WHITE)


static func _solid_texture(color: Color) -> ImageTexture:
	var image := Image.create(4, 4, false, Image.FORMAT_RGBA8)
	image.fill(color)
	return ImageTexture.create_from_image(image)


static func _new_arrays() -> Dictionary:
	return {"vertices": PackedVector3Array(), "normals": PackedVector3Array(), "colors": PackedColorArray(), "uvs": PackedVector2Array()}


static func _append_triangle(output: Dictionary, a: Vector3, b: Vector3, c: Vector3, outward: Vector3, color: Color) -> void:
	var clockwise_normal := (c - a).cross(b - a).normalized()
	if clockwise_normal.dot(outward) < 0.0:
		var temporary := b
		b = c
		c = temporary
		clockwise_normal = -clockwise_normal
	if clockwise_normal.length_squared() < 0.1:
		return
	for point in [a, b, c]:
		output["vertices"].push_back(point)
		output["normals"].push_back(clockwise_normal)
		output["colors"].push_back(color)
		output["uvs"].push_back(Vector2(point.x, point.z))


static func _finish_mesh(output: Dictionary) -> ArrayMesh:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = output["vertices"]
	arrays[Mesh.ARRAY_NORMAL] = output["normals"]
	arrays[Mesh.ARRAY_COLOR] = output["colors"]
	arrays[Mesh.ARRAY_TEX_UV] = output["uvs"]
	var mesh := ArrayMesh.new()
	if not output["vertices"].is_empty():
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh
