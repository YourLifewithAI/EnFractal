extends RefCounted
## Shared original artwork, independent from object placement and permissions.

const TEXTURE_ROOT := "res://assets/art/barton/textures/"
const TREE_ROOT := "res://assets/art/barton/trees/"
const STYLE_VERSION := "barton_painterly_v1"
static var _materials: Dictionary = {}


static func foliage_material(species := "oak") -> ShaderMaterial:
	var key := "foliage_" + species
	if _materials.has(key):
		return _materials[key]
	var material := ShaderMaterial.new()
	material.resource_name = key + "_" + STYLE_VERSION
	material.shader = load("res://shaders/painterly_foliage.gdshader")
	var texture_name := "juniper-spray-v1.png" if species == "juniper" else "oak-leaf-cluster-v1.png"
	material.set_shader_parameter("leaf_paint", load(TEXTURE_ROOT + texture_name))
	material.set_shader_parameter("leaf_tint", Color(0.82, 0.94, 0.93) if species == "juniper" else Color(0.95, 1.0, 0.89))
	material.set_shader_parameter("paint_mid", Color(0.28, 0.42, 0.33) if species == "juniper" else Color(0.39, 0.48, 0.25))
	_materials[key] = material
	return material


static func bark_material() -> ShaderMaterial:
	if _materials.has("bark"):
		return _materials["bark"]
	var material := ShaderMaterial.new()
	material.resource_name = "Painted oak bark " + STYLE_VERSION
	material.shader = load("res://shaders/painterly_bark.gdshader")
	material.set_shader_parameter("bark_paint", load(TEXTURE_ROOT + "bark-paint-v1.png"))
	_materials["bark"] = material
	return material


static func groundcover_material() -> ShaderMaterial:
	if _materials.has("groundcover"):
		return _materials["groundcover"]
	var material := ShaderMaterial.new()
	material.resource_name = "Painted bunchgrass " + STYLE_VERSION
	material.shader = load("res://shaders/painterly_foliage.gdshader")
	material.set_shader_parameter("leaf_paint", load(TEXTURE_ROOT + "grass-tuft-v1.png"))
	material.set_shader_parameter("paint_mid", Color(0.43, 0.53, 0.27))
	material.set_shader_parameter("contrast", 0.42)
	material.set_shader_parameter("leaf_tint", Color(1.0, 1.02, 0.86))
	_materials["groundcover"] = material
	return material


static func make_tree(species := "oak", collider := true) -> Node3D:
	var filename := "ashe-juniper-v1.glb" if species == "juniper" else "live-oak-v1.glb"
	var source: PackedScene = load(TREE_ROOT + filename)
	var tree: Node3D = source.instantiate()
	tree.set_meta("asset_id", "barton_ashe_juniper_v1" if species == "juniper" else "barton_live_oak_v1")
	tree.set_meta("style_version", STYLE_VERSION)
	tree.set_meta("provenance", "original illustrative tree; placement is not a surveyed individual")
	_bind_tree_materials(tree, species)
	if collider:
		var body := StaticBody3D.new()
		body.name = "TrunkContact"
		var shape := CapsuleShape3D.new()
		shape.radius = 0.22 if species == "juniper" else 0.42
		shape.height = 2.5
		var collision := CollisionShape3D.new()
		collision.shape = shape
		collision.position.y = 1.25
		body.add_child(collision)
		tree.add_child(body)
	return tree


static func _bind_tree_materials(node: Node, species: String) -> void:
	if node is MeshInstance3D:
		var is_leaves := str(node.name).begins_with("Leaves")
		node.material_override = foliage_material(species) if is_leaves else bark_material()
	for child in node.get_children():
		_bind_tree_materials(child, species)


static func triangle_count(node: Node) -> int:
	var total := 0
	if node is MeshInstance3D and node.mesh != null:
		total += mesh_triangles(node.mesh)
	if node is MultiMeshInstance3D and node.multimesh != null:
		total += mesh_triangles(node.multimesh.mesh) * node.multimesh.instance_count
	for child in node.get_children():
		total += triangle_count(child)
	return total


static func mesh_triangles(mesh: Mesh) -> int:
	var result := 0
	for surface in range(mesh.get_surface_count()):
		var count: int = mesh.surface_get_array_index_len(surface)
		result += (count if count > 0 else mesh.surface_get_array_len(surface)) / 3
	return result
