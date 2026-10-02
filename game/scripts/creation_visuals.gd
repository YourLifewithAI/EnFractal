extends RefCounted
## Rendering adapter for approved compiler parts. It never recognizes recipe names.
const GROUND := preload("res://scripts/painterly_ground_kit.gd")
const PAINT := preload("res://scripts/painterly_assets.gd")
static var materials: Dictionary = {}

static func build(artifact: Dictionary, collision := false) -> Node3D:
	var assembly := Node3D.new()
	assembly.name = "CompiledInvention"
	assembly.set_meta("artifact_hash", artifact["hash"])
	var parts := {}
	for definition in artifact["source"]["parts"]:
		var part := Node3D.new()
		part.name = definition["id"]
		part.position = vector(definition["position_m"])
		part.rotation = vector(definition["rotation_deg"]) * PI / 180.0
		part.set_meta("authored_basis", part.basis)
		part.set_meta("part_id", definition["id"])
		assembly.add_child(part)
		parts[definition["id"]] = part
		var size := vector(definition["size_m"])
		var material := _material(definition["material"])
		if definition["shape"] == "rotor":
			for blade in range(4):
				var arm := MeshInstance3D.new()
				var box := BoxMesh.new()
				box.size = Vector3(size.x * 0.43, size.y, size.z * 0.16) if blade % 2 == 0 else Vector3(size.x * 0.16, size.y, size.z * 0.43)
				arm.mesh = box
				arm.material_override = material
				arm.position = Vector3(cos(blade * PI * 0.5) * size.x * 0.23, 0, -sin(blade * PI * 0.5) * size.z * 0.23)
				part.add_child(arm)
		else:
			var instance := MeshInstance3D.new()
			instance.mesh = _mesh(definition["shape"], size)
			instance.material_override = material
			part.add_child(instance)
			if collision:
				var body := StaticBody3D.new()
				body.collision_layer = 1
				body.collision_mask = 0
				var collider := CollisionShape3D.new()
				collider.shape = instance.mesh.create_convex_shape(true, false)
				body.add_child(collider)
				part.add_child(body)
	for node in artifact["source"]["nodes"]:
		if node["op"] == "light":
			var lamp := OmniLight3D.new()
			lamp.name = "Light_" + node["id"]
			lamp.light_color = Color("ffe3a4")
			lamp.omni_range = 4.0
			lamp.light_energy = 0.0
			lamp.shadow_enabled = false
			parts[node["part_id"]].add_child(lamp)
	assembly.set_meta("parts", parts)
	return assembly

static func vector(value: Array) -> Vector3:
	return Vector3(float(value[0]), float(value[1]), float(value[2]))

static func _material(role: String) -> Material:
	if materials.has(role):
		return materials[role]
	var result: Material
	if role == "stone":
		result = GROUND.make_surface_material()
	elif role == "wood":
		result = PAINT.bark_material()
	else:
		var paint := StandardMaterial3D.new()
		paint.albedo_color = Color("b4824e") if role == "copper" else Color("638d75")
		paint.roughness = 0.83
		paint.metallic = 0.20 if role == "copper" else 0.0
		result = paint
	materials[role] = result
	return result

static func _mesh(kind: String, size: Vector3) -> Mesh:
	if kind == "sphere":
		var sphere := SphereMesh.new()
		sphere.radius = 0.5
		sphere.height = 1.0
		sphere.radial_segments = 16
		sphere.rings = 8
		var arrays := sphere.get_mesh_arrays()
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		for index in range(vertices.size()):
			vertices[index] *= size
			normals[index] = (normals[index] / size).normalized()
		arrays[Mesh.ARRAY_VERTEX] = vertices
		arrays[Mesh.ARRAY_NORMAL] = normals
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		return mesh
	if kind == "cylinder":
		var cylinder := CylinderMesh.new()
		cylinder.top_radius = 0.5
		cylinder.bottom_radius = 0.5
		cylinder.height = 1.0
		cylinder.radial_segments = 12
		var arrays := cylinder.get_mesh_arrays()
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		for index in range(vertices.size()):
			vertices[index] *= size
			normals[index] = (normals[index] / size).normalized()
		arrays[Mesh.ARRAY_VERTEX] = vertices
		arrays[Mesh.ARRAY_NORMAL] = normals
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		return mesh
	if kind == "wing":
		var vertices := [Vector3(-0.5,-0.5,-0.5), Vector3(0.5,-0.5,0), Vector3(-0.5,-0.5,0.5), Vector3(-0.5,0.5,-0.5), Vector3(0.5,0.5,0), Vector3(-0.5,0.5,0.5)]
		var faces := [[0,1,2],[3,5,4],[0,3,4],[0,4,1],[1,4,5],[1,5,2],[2,5,3],[2,3,0]]
		var surface := SurfaceTool.new()
		surface.begin(Mesh.PRIMITIVE_TRIANGLES)
		var center := Vector3(-1.0/6.0,0,0) * size
		for face in faces:
			var a: Vector3 = vertices[face[0]] * size
			var b: Vector3 = vertices[face[1]] * size
			var c: Vector3 = vertices[face[2]] * size
			if (b-a).cross(c-a).dot((a+b+c)/3.0-center) > 0.0:
				var swap := b
				b = c
				c = swap
			var normal := -(b-a).cross(c-a).normalized()
			for vertex in [a,b,c]:
				surface.set_normal(normal)
				surface.set_uv(Vector2(vertex.x,vertex.z))
				surface.add_vertex(vertex)
		return surface.commit()
	var box := BoxMesh.new()
	box.size = size
	return box
