extends SceneTree
## Read-only probe for the *existing* WorldState spatial-pin expression.
## Paths are supplied by the runner; this does not load or rewrite player saves.

const SPATIAL_KEYS := [
	"format", "crs", "axes", "center_lat", "center_lon",
	"center_projected_m", "side_m", "grid_side", "sample_spacing_m",
	"tile_side_m", "height_encoding", "height_offset_m",
	"height_scale_m", "height_origin_m", "height_min_m", "height_max_m",
]
const WORLD_STATE := preload("res://scripts/world_state.gd")


func _initialize() -> void:
	var cases_path := OS.get_environment("ENFRACTAL_CANON_CASES")
	var output_path := OS.get_environment("ENFRACTAL_CANON_OUTPUT")
	if cases_path.is_empty() or output_path.is_empty():
		_fail("Set ENFRACTAL_CANON_CASES and ENFRACTAL_CANON_OUTPUT")
		return
	var cases_file := FileAccess.open(cases_path, FileAccess.READ)
	if cases_file == null:
		_fail("Cannot open cases file")
		return
	var fixture: Variant = JSON.parse_string(cases_file.get_as_text())
	if not fixture is Dictionary or not fixture.get("cases") is Array:
		_fail("Invalid cases fixture")
		return
	var manifest: Variant = JSON.parse_string(FileAccess.get_file_as_string("res://maps/barton_creek/manifest.json"))
	if not manifest is Dictionary:
		_fail("Invalid Barton manifest")
		return
	var state := WORLD_STATE.new()
	if not state.initialize("phase0_canonical_probe", "barton_creek_v0_local", manifest):
		_fail("WorldState rejected Barton manifest")
		return
	var results := []
	for entry in fixture["cases"]:
		if not entry is Dictionary or not entry.get("id") is String:
			_fail("Invalid case entry")
			return
		var value: Variant = entry.get("value")
		if entry.get("kind") == "barton_spatial":
			var spatial := {}
			for key in SPATIAL_KEYS:
				spatial[key] = manifest[key]
			value = spatial
		var serialized := JSON.stringify(value, "", true, true)
		results.append({
			"id": entry["id"],
			"json_utf8_hex": serialized.to_utf8_buffer().hex_encode(),
			"sha256": serialized.sha256_text(),
		})
	if results[0]["sha256"] != state.base_map["spatial_manifest_sha256"]:
		_fail("Probe hash differs from actual WorldState base pin")
		return
	var result := {
		"engine": Engine.get_version_info()["string"],
		"world_state_spatial_sha256": state.base_map["spatial_manifest_sha256"],
		"results": results,
	}
	var output := FileAccess.open(output_path, FileAccess.WRITE)
	if output == null:
		_fail("Cannot open output file")
		return
	output.store_string(JSON.stringify(result, "\t", true))
	output.close()
	print("phase0 canonical probe wrote %d cases" % results.size())
	quit(0)


func _fail(message: String) -> void:
	printerr(message)
	quit(1)
