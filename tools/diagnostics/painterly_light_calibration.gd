extends SceneTree
## Neutral renderer calibration. Outputs evidence, never production resources.

var output_directory: String
var camera: Camera3D
var sun: DirectionalLight3D
var caption: Label
var swatch_points := [Vector3(-0.8, 1.0, 0.0), Vector3(1.0, 1.0, 0.0), Vector3(2.8, 1.0, 0.0)]
var samples: Dictionary = {}


func _initialize() -> void:
	var repository := ProjectSettings.globalize_path("res://").trim_suffix("/").get_base_dir()
	output_directory = repository.path_join(".cache/painterly-light-calibration")
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--output-dir="):
			var requested := argument.trim_prefix("--output-dir=")
			output_directory = requested if requested.is_absolute_path() else repository.path_join(requested)
	if DisplayServer.get_name() == "headless":
		push_error("Calibration captures require a rendering display; omit --headless")
		quit(1)
		return
	if DirAccess.make_dir_recursive_absolute(output_directory) != OK:
		push_error("Could not create calibration output directory")
		quit(1)
		return
	call_deferred("_run")


func _run() -> void:
	var world := Node3D.new()
	root.add_child(world)
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.20, 0.20, 0.20)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color.WHITE
	environment.ambient_light_energy = 0.18
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	var settings := WorldEnvironment.new()
	settings.environment = environment
	world.add_child(settings)
	sun = DirectionalLight3D.new()
	sun.light_color = Color.WHITE
	sun.light_energy = 0.55
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 25.0
	world.add_child(sun)
	camera = Camera3D.new()
	camera.position = Vector3(0.0, 3.3, 10.0)
	world.add_child(camera)
	camera.look_at(Vector3(0.0, 0.9, 0.0))
	camera.fov = 48.0
	camera.current = true
	var floor_mesh := PlaneMesh.new()
	floor_mesh.size = Vector2(12.0, 8.0)
	_add_mesh(world, floor_mesh, _gray(0.28), Vector3.ZERO)
	var sphere := SphereMesh.new()
	sphere.radius = 0.90
	sphere.height = 1.8
	sphere.radial_segments = 48
	sphere.rings = 24
	_add_mesh(world, sphere, _gray(0.5), Vector3(-3.0, 0.90, 0.0))
	_add_mesh(world, _card(Color.WHITE), _gray(0.5), Vector3(-0.8, 0.0, 0.0))
	var vertex_gray := _gray(1.0)
	vertex_gray.vertex_color_use_as_albedo = true
	_add_mesh(world, _card(Color(0.5, 0.5, 0.5)), vertex_gray, Vector3(1.0, 0.0, 0.0))
	var two_sided := _gray(0.5)
	two_sided.cull_mode = BaseMaterial3D.CULL_DISABLED
	_add_mesh(world, _card(Color.WHITE), two_sided, Vector3(2.8, 0.0, 0.0))
	var layer := CanvasLayer.new()
	root.add_child(layer)
	caption = Label.new()
	caption.position = Vector2(24.0, 20.0)
	caption.add_theme_font_size_override("font_size", 22)
	caption.add_theme_color_override("font_shadow_color", Color.BLACK)
	caption.add_theme_constant_override("shadow_offset_x", 1)
	caption.add_theme_constant_override("shadow_offset_y", 1)
	layer.add_child(caption)
	for light_case in [{"name": "front", "yaw": 0.0}, {"name": "side", "yaw": PI / 2.0}, {"name": "back", "yaw": PI}]:
		sun.rotation = Vector3(-0.72, float(light_case["yaw"]), 0.0)
		caption.text = "ART-0 neutral calibration | light: " + light_case["name"] + "\nSphere | material gray | vertex gray | double-sided gray"
		if not await _capture(str(light_case["name"])):
			quit(1)
			return
	camera.position = Vector3(0.0, 3.3, -10.0)
	camera.look_at(Vector3(0.0, 0.9, 0.0))
	sun.rotation = Vector3(-0.72, PI, 0.0)
	caption.text = "ART-0 neutral calibration | reverse camera, rear light\nTwo one-sided cards should disappear; double-sided card remains"
	if not await _capture("reverse_camera"):
		quit(1)
		return
	var checks := {
		"material_vertex_gray_agree": true,
		"front_lighter_than_back": samples["front"][0][0] > samples["back"][0][0] + 0.2,
		"one_sided_rear_is_background": absf(samples["reverse_camera"][0][0] - 0.2) < 0.01 and absf(samples["reverse_camera"][1][0] - 0.2) < 0.01,
		"two_sided_rear_receives_light": samples["reverse_camera"][2][0] > 0.4,
	}
	for light_case in ["front", "side", "back"]:
		for channel in range(3):
			if absf(samples[light_case][0][channel] - samples[light_case][1][channel]) > 2.0 / 255.0:
				checks["material_vertex_gray_agree"] = false
	var result := {
		"engine": Engine.get_version_info()["string"],
		"renderer": RenderingServer.get_current_rendering_method(),
		"device": RenderingServer.get_video_adapter_name(),
		"gray_srgb": 0.5,
		"sun_energy": sun.light_energy,
		"ambient_energy": environment.ambient_light_energy,
		"tone_mapper": "filmic",
		"samples": samples,
		"checks": checks,
	}
	var output := FileAccess.open(output_directory.path_join("calibration.json"), FileAccess.WRITE)
	output.store_string(JSON.stringify(result, "\t") + "\n")
	output.close()
	print("Neutral calibration captured: ", output_directory, "; renderer=", result["renderer"], "; checks=", checks, "; samples=", samples)
	if false in checks.values():
		push_error("Neutral calibration response differs from the expected front/back and color behavior")
		quit(1)
		return
	quit(0)


func _gray(value: float) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(value, value, value)
	material.roughness = 1.0
	material.metallic_specular = 0.0
	material.cull_mode = BaseMaterial3D.CULL_BACK
	return material


func _add_mesh(parent: Node3D, mesh: Mesh, material: Material, location: Vector3) -> void:
	var instance := MeshInstance3D.new()
	instance.mesh = mesh
	instance.material_override = material
	instance.position = location
	parent.add_child(instance)


func _card(color: Color) -> ArrayMesh:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = PackedVector3Array([Vector3(-0.65, 0.0, 0.0), Vector3(0.65, 0.0, 0.0), Vector3(0.65, 2.0, 0.0), Vector3(-0.65, 2.0, 0.0)])
	arrays[Mesh.ARRAY_NORMAL] = PackedVector3Array([Vector3.BACK, Vector3.BACK, Vector3.BACK, Vector3.BACK])
	arrays[Mesh.ARRAY_COLOR] = PackedColorArray([color, color, color, color])
	arrays[Mesh.ARRAY_INDEX] = PackedInt32Array([0, 2, 1, 0, 3, 2])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _capture(name: String) -> bool:
	for frame in range(10):
		await process_frame
	await RenderingServer.frame_post_draw
	var capture := root.get_texture().get_image()
	var values: Array = []
	for point in swatch_points:
		var pixel := camera.unproject_position(point)
		var value := capture.get_pixel(roundi(pixel.x), roundi(pixel.y))
		values.append([value.r, value.g, value.b])
	samples[name] = values
	return capture.save_png(output_directory.path_join(name + ".png")) == OK
