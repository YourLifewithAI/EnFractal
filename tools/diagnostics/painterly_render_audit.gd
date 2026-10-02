extends SceneTree
## Research diagnostic for the existing Barton art-reference fixtures.
## Run with the game project, --headless for counts only, and optional user arg
## --output-dir=<directory>. This never saves or edits production resources.
## Winding/normal agreement is not a universal mesh-validity test: deliberately
## stylized/custom normals can invalidate that interpretation on other meshes.

var output_directory: String
var observations: Dictionary = {}


func _initialize() -> void:
	var repository := ProjectSettings.globalize_path("res://").trim_suffix("/").get_base_dir()
	output_directory = repository.path_join(".cache/painterly-render-audit")
	for argument in OS.get_cmdline_user_args():
		if not argument.begins_with("--output-dir="):
			push_error("Supported user argument: --output-dir=<directory>")
			quit(1)
			return
		var requested := argument.trim_prefix("--output-dir=")
		if requested.is_empty():
			push_error("Output directory must not be empty")
			quit(1)
			return
		output_directory = requested if requested.is_absolute_path() else repository.path_join(requested)
	output_directory = output_directory.simplify_path()
	if DirAccess.make_dir_recursive_absolute(output_directory) != OK:
		push_error("Could not create diagnostic output directory: " + output_directory)
		quit(1)
		return
	call_deferred("_probe")


func _probe() -> void:
	# Fixed known fixture/camera/profile. These environment changes are local to
	# this diagnostic process and do not affect project configuration or saves.
	OS.set_environment("ENFRACTAL_ART_PROFILE", "standard")
	OS.set_environment("ENFRACTAL_ART_VIEW", "")
	var scene = load("res://scenes/art_reference.tscn").instantiate()
	root.add_child(scene)
	await process_frame
	_record("stock_box_control", BoxMesh.new())
	var dressing = load("res://scripts/workshop_art_dressing.gd").new()
	_record("workshop_oak", dressing._crown_mesh(false))
	_record("workshop_juniper", dressing._crown_mesh(true))
	_record("workshop_rock", dressing._rock_mesh())
	dressing.free()
	var manifest: Dictionary = scene.map_runtime.manifest
	var job = load("res://scripts/terrain_mesh_job.gd").new()
	job.configure(scene.map_runtime, int(manifest["side_m"]), int(manifest["sample_spacing_m"]), int(manifest["tile_side_m"]), float(manifest["height_origin_m"]), float(manifest["height_min_m"]), float(manifest["height_max_m"]))
	for step in [2, 16]:
		job.run(8, 8, step)
		var terrain_mesh := ArrayMesh.new()
		terrain_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, job.arrays)
		_record("map_tile_8_8_spacing_%dm" % step, terrain_mesh)
	_collect_known_scene(scene)
	var results := {
		"engine": Engine.get_version_info()["string"],
		"renderer": RenderingServer.get_current_rendering_method(),
		"display_server": DisplayServer.get_name(),
		"profile": "standard",
		"scope": "Known Barton art-reference scene, workshop mesh generators, and map tile (8,8); not a general mesh validator or visual acceptance test.",
		"project_msaa_3d": ProjectSettings.get_setting("rendering/anti_aliasing/quality/msaa_3d"),
		"viewport_msaa_3d": root.msaa_3d,
		"observations": observations,
	}
	var output := FileAccess.open(output_directory.path_join("counts.json"), FileAccess.WRITE)
	if output == null:
		push_error("Could not write diagnostic counts")
		quit(1)
		return
	output.store_string(JSON.stringify(results, "\t") + "\n")
	output.close()
	print("MSAA project=", results["project_msaa_3d"], "; viewport=", root.msaa_3d)
	print("Diagnostic counts: ", output_directory.path_join("counts.json"))
	if DisplayServer.get_name() != "headless":
		if not await _capture("before.png"):
			quit(1)
			return
		# Only these two known indexed terrain meshes are copied and reversed in
		# memory. Keep camera, normals, material colors, lighting and scene fixed.
		for node_name in ["USGSHeightPatch", "CoarseUSGSBackdrop"]:
			var node = scene.find_child(node_name, true, false)
			if not node is MeshInstance3D or not node.mesh is ArrayMesh:
				push_error("Known terrain fixture is missing: " + node_name)
				quit(1)
				return
			_flip_known_terrain(node)
			print("Temporary in-memory winding reversal: ", node_name)
		if not await _capture("terrain-winding-reversed.png"):
			quit(1)
			return
	print("Diagnostic completed; production resources were not saved or changed.")
	quit(0)


func _collect_known_scene(node: Node) -> void:
	if node is MeshInstance3D and node.mesh is ArrayMesh:
		_record("art_reference/" + str(node.name), node.mesh)
	if node is MultiMeshInstance3D and node.multimesh.mesh is ArrayMesh:
		_record("art_reference/" + str(node.name), node.multimesh.mesh)
	for child in node.get_children():
		_collect_known_scene(child)


func _record(label: String, mesh: Mesh) -> void:
	var counts := _winding_normal_agreement(mesh)
	observations[label] = counts
	print(label, " ", counts)


func _winding_normal_agreement(mesh: Mesh) -> Dictionary:
	var positive := 0
	var negative := 0
	var near_zero := 0
	for surface in range(mesh.get_surface_count()):
		var arrays := mesh.surface_get_arrays(surface)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		var indices = arrays[Mesh.ARRAY_INDEX]
		if indices == null or indices.is_empty():
			indices = PackedInt32Array()
			for i in range(vertices.size()):
				indices.append(i)
		for i in range(0, indices.size(), 3):
			var a: int = indices[i]
			var b: int = indices[i + 1]
			var c: int = indices[i + 2]
			var cross := (vertices[b] - vertices[a]).cross(vertices[c] - vertices[a])
			var supplied_normal := normals[a] + normals[b] + normals[c]
			var dot := cross.dot(supplied_normal)
			if absf(dot) < 0.000001:
				near_zero += 1
			elif dot > 0:
				positive += 1
			else:
				negative += 1
	return {"supplied_normals_oppose_front": positive, "supplied_normals_agree_with_front": negative, "near_zero_dot": near_zero}


func _flip_known_terrain(node: MeshInstance3D) -> void:
	var arrays := node.mesh.surface_get_arrays(0)
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	for i in range(0, indices.size(), 3):
		var temporary := indices[i + 1]
		indices[i + 1] = indices[i + 2]
		indices[i + 2] = temporary
	arrays[Mesh.ARRAY_INDEX] = indices
	var copy := ArrayMesh.new()
	copy.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	node.mesh = copy


func _capture(filename: String) -> bool:
	for frame in range(8):
		await process_frame
	await RenderingServer.frame_post_draw
	var path := output_directory.path_join(filename)
	var result := root.get_texture().get_image().save_png(path)
	if result != OK:
		push_error("Could not write diagnostic capture: " + path)
		return false
	print("Diagnostic capture: ", path)
	return true
