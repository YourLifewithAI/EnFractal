extends SceneTree

const Compiler = preload("res://scripts/creation_compiler.gd")

func _initialize() -> void:
	var path := "res://maps/barton_creek/manifest.json"
	var raw := FileAccess.get_file_as_string(path)
	var manifest: Variant = JSON.parse_string(raw)
	if not manifest is Dictionary:
		quit(1)
		return
	print("ENFRACTAL_BASE_PIN|" + JSON.stringify({"pin": Compiler.canonical_json(manifest).sha256_text(), "raw_sha256": FileAccess.get_sha256(path)}))
	quit(0)
