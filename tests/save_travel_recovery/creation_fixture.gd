extends SceneTree

const Authority = preload("res://scripts/creation_authority.gd")
const Compiler = preload("res://scripts/creation_compiler.gd")

func _initialize() -> void:
	var manifest: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://maps/barton_creek/manifest.json"))
	var authority := Authority.new()
	authority.configure(manifest, func(_x: float, _z: float) -> float: return 0.0, "user://tests/recovery_fixture_%d.json" % OS.get_process_id())
	authority.persistence_sink = func(_envelope: Dictionary) -> Dictionary: return {"ok": true}
	var args := OS.get_cmdline_user_args()
	if args.size() == 2 and args[0] == "--verify-envelope":
		var loaded: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
		var result: Dictionary = authority.load_envelope(loaded)
		print("ENFRACTAL_CREATION_FIXTURE|" + JSON.stringify({"ok": result.get("ok", false), "instances": authority.snapshot("local_player").get("instances", {}).size()}))
		quit(0 if result.get("ok", false) else 1)
		return
	var empty: Dictionary = authority.export_envelope()
	var source: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://creation_templates/spinner.json"))
	var placed: Dictionary = authority.submit("local_player", {"op": "place", "action_id": "recovery_spinner", "expected_revision": 0, "expected_permission_revision": 0, "source": source, "x_m": -30.0, "z_m": 330.0, "yaw_deg": 15.0})
	if not placed.get("ok", false):
		printerr(placed)
		quit(1)
		return
	print("ENFRACTAL_CREATION_FIXTURE|" + Compiler.canonical_json({"empty": empty, "populated": authority.export_envelope()}))
	quit(0)
