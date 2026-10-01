extends Node3D
## Collider patches are always built from one validated MapRuntime height grid,
## independent of visual tile LOD. Only the player's 3 x 3 neighborhood resides.
## The source height SHA-256 is pinned for this immutable prototype; revision 0
## means no live terrain edits have been activated.

const PATCH_SIDE_M := 64
const ACTIVE_RADIUS := 1

var source: MapRuntime
var map_side_m := 0
var sample_spacing_m := 0
var patch_count := 0
var source_height_sha256 := ""
var collision_revision := 0
var active_patches: Dictionary = {}
var center_patch := Vector2i(-1, -1)
var enabled := false


func configure(height_source: MapRuntime, side_m: int, spacing_m: int) -> void:
	source = height_source
	map_side_m = side_m
	sample_spacing_m = spacing_m
	source_height_sha256 = str(source.manifest.get("heights_sha256", ""))
	collision_revision = 0
	assert(map_side_m % PATCH_SIDE_M == 0)
	assert(PATCH_SIDE_M % sample_spacing_m == 0)
	patch_count = map_side_m / PATCH_SIDE_M


func activate(center: Vector3) -> void:
	enabled = true
	update_center(center)


func deactivate() -> void:
	enabled = false
	for key in active_patches:
		var body: StaticBody3D = active_patches[key]
		body.collision_layer = 0
		body.queue_free()
	active_patches.clear()
	center_patch = Vector2i(-1, -1)


func update_center(point: Vector3) -> void:
	if not enabled:
		return
	var next_center := Vector2i(
		clampi(int(floor((point.x + map_side_m * 0.5) / PATCH_SIDE_M)), 0, patch_count - 1),
		clampi(int(floor((point.z + map_side_m * 0.5) / PATCH_SIDE_M)), 0, patch_count - 1)
	)
	if next_center == center_patch:
		return
	center_patch = next_center
	var wanted := {}
	for row in range(maxi(0, center_patch.y - ACTIVE_RADIUS), mini(patch_count, center_patch.y + ACTIVE_RADIUS + 1)):
		for column in range(maxi(0, center_patch.x - ACTIVE_RADIUS), mini(patch_count, center_patch.x + ACTIVE_RADIUS + 1)):
			var key := Vector2i(column, row)
			wanted[key] = true
			if not active_patches.has(key):
				active_patches[key] = _make_patch(row, column)
	for key in active_patches.keys():
		if not wanted.has(key):
			var body: StaticBody3D = active_patches[key]
			body.collision_layer = 0
			body.queue_free()
			active_patches.erase(key)


func _make_patch(row: int, column: int) -> StaticBody3D:
	var cells := PATCH_SIDE_M / sample_spacing_m
	var grid_row := row * cells
	var grid_column := column * cells
	var half := map_side_m * 0.5
	var stride := cells + 1
	var heights := PackedFloat32Array()
	heights.resize(stride * stride)
	for local_row in range(stride):
		for local_column in range(stride):
			heights[local_row * stride + local_column] = source.height_at_grid(grid_row + local_row, grid_column + local_column)
	var faces := PackedVector3Array()
	faces.resize(cells * cells * 6)
	var face_index := 0
	for local_row in range(cells):
		for local_column in range(cells):
			var x: float = -half + (grid_column + local_column) * sample_spacing_m
			var z: float = -half + (grid_row + local_row) * sample_spacing_m
			var vertex_index := local_row * stride + local_column
			var a := Vector3(x, heights[vertex_index], z)
			var b := Vector3(x + sample_spacing_m, heights[vertex_index + 1], z)
			var c := Vector3(x, heights[vertex_index + stride], z + sample_spacing_m)
			var d := Vector3(x + sample_spacing_m, heights[vertex_index + stride + 1], z + sample_spacing_m)
			faces[face_index] = a
			faces[face_index + 1] = c
			faces[face_index + 2] = b
			faces[face_index + 3] = b
			faces[face_index + 4] = c
			faces[face_index + 5] = d
			face_index += 6
	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(faces)
	shape.backface_collision = true
	var collider := CollisionShape3D.new()
	collider.shape = shape
	var body := StaticBody3D.new()
	body.name = "TerrainCollider_%d_%d" % [row, column]
	body.collision_layer = 1
	body.collision_mask = 0
	body.add_child(collider)
	add_child(body)
	return body
