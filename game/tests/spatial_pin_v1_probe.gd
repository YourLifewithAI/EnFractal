extends SceneTree
## Independent Godot side of the Python/Godot spatial-pin v1 byte contract.

const PIN := preload("res://scripts/spatial_pin_v1.gd")


func _initialize() -> void:
	var fixture_path := OS.get_environment("ENFRACTAL_SPATIAL_V1_CASES")
	var output_path := OS.get_environment("ENFRACTAL_SPATIAL_V1_OUTPUT")
	if fixture_path.is_empty() or output_path.is_empty():
		_fail("spatial-pin fixture and output paths are required")
		return
	var fixture: Variant = JSON.parse_string(FileAccess.get_file_as_string(fixture_path))
	if not fixture is Dictionary or not fixture.get("base") is Dictionary or not fixture.get("cases") is Array:
		_fail("invalid spatial-pin fixture")
		return
	var base: Dictionary = fixture["base"]
	if not PIN.validate(base):
		_fail("invalid Barton base descriptor")
		return
	if _sha256_file("res://maps/barton_creek/heights.r16") != base["heights_sha256"]:
		_fail("Barton height payload hash mismatch")
		return
	if _sha256_file("res://maps/barton_creek/features.json") != base["features_sha256"]:
		_fail("Barton feature payload hash mismatch")
		return
	var results := []
	for entry in fixture["cases"]:
		if not entry is Dictionary or not entry.get("id") is String or not entry.get("changes") is Dictionary:
			_fail("invalid case entry")
			return
		var descriptor: Dictionary = base.duplicate(true)
		for key in entry["changes"]:
			descriptor[key] = entry["changes"][key]
		var raw: PackedByteArray = PIN.encode(descriptor)
		if raw.is_empty() or PIN.decode(raw) != descriptor:
			_fail("case failed round trip: " + entry["id"])
			return
		if entry.has("payloads_ascii"):
			var payloads: Dictionary = entry["payloads_ascii"]
			if str(payloads.get("heights", "")).sha256_text() != descriptor["heights_sha256"] or str(payloads.get("features", "")).sha256_text() != descriptor["features_sha256"]:
				_fail("synthetic payload hashes mismatch: " + entry["id"])
				return
		results.append({"id": entry["id"], "bytes_hex": raw.hex_encode(), "sha256": PIN.digest(descriptor)})
	if not _reject_unsafe(base):
		_fail("invalid input acceptance")
		return
	var output := FileAccess.open(output_path, FileAccess.WRITE)
	if output == null:
		_fail("cannot write spatial-pin result")
		return
	output.store_string(JSON.stringify({"engine": Engine.get_version_info()["string"], "results": results}, "\t", true))
	output.close()
	print("spatial-pin v1 Godot probe passed %d cases and unsafe-input checks" % results.size())
	quit(0)


func _sha256_file(path: String) -> String:
	return FileAccess.get_sha256(path)


func _reject_unsafe(base: Dictionary) -> bool:
	var invalid := [
		{"frame_id": "café"},
		{"frame_id": "line\nbreak"},
		{"center_lat_deg": "9e1"},
		{"center_lat_deg": "90.0000000000000001"},
		{"height_offset_m": "-0"},
		{"height_scale_m": "-0.0"},
		{"grid_side_samples": "9007199254740991"},
		{"grid_side_samples": 2049},
		{"heights_sha256": "A15d168d5d84061e05b609b875ea82e306a539b3ad96c881528f96d06abe55d7"},
		{"sample_spacing_m": "0.0001"},
		{"vertical_scope": "source_declared"},
		{"surface_outside_id": "clamp"},
		{"side_m": "4095"},
	]
	for changes in invalid:
		var descriptor: Dictionary = base.duplicate(true)
		for key in changes:
			descriptor[key] = changes[key]
		if PIN.validate(descriptor) or not PIN.encode(descriptor).is_empty():
			return false
	var missing := base.duplicate(true)
	missing.erase("frame_id")
	if PIN.validate(missing):
		return false
	var unknown := base.duplicate(true)
	unknown["unrecognized"] = "value"
	if PIN.validate(unknown):
		return false
	var raw: PackedByteArray = PIN.encode(base)
	var lines := raw.get_string_from_ascii().split("\n", true)
	lines[2] = lines[1] # duplicate axis_x; axis_y is absent.
	if not PIN.decode("\n".join(lines).to_utf8_buffer()).is_empty():
		return false
	lines = raw.get_string_from_ascii().split("\n", true)
	lines[1] = "axis_x=café"
	return PIN.decode("\n".join(lines).to_utf8_buffer()).is_empty()


func _fail(message: String) -> void:
	printerr(message)
	quit(1)
