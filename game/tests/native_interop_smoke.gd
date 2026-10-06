extends SceneTree
## Exercises the compiled C# boundary using real GDScript Variants.


func _initialize() -> void:
	var contract_script = load("res://native/NativeWorldContract.cs")
	if contract_script == null or not contract_script.can_instantiate():
		_fail("C# assembly missing; run tools/build-native.ps1 with Godot .NET")
		return
	var contract: RefCounted = contract_script.new()
	var profile: Dictionary = contract.call("GetDefaultProfile")
	if not contract.call("ValidateProfile", profile):
		_fail("C# profile did not survive its GDScript round trip")
		return
	if profile["schema_version"] != 1 or not is_equal_approx(profile["meters_per_world_unit"], 1.0) \
			or not is_equal_approx(profile["height_m"], 0.10) or not is_equal_approx(profile["radius_m"], 0.02) \
			or not is_equal_approx(profile["eye_height_m"], 0.087) or not is_equal_approx(profile["interaction_reach_m"], 0.15):
		_fail("unit or 10 cm body contract drifted")
		return
	for invalid in [null, [], "profile", {"height_m": 0.10}]:
		if contract.call("ValidateProfile", invalid):
			_fail("malformed profile accepted")
			return
	var mutations := [
		["schema_version", 1.0],
		["schema_version", 2],
		["meters_per_world_unit", 6.0],
		["height_m", NAN],
		["height_m", INF],
		["height_m", "0.10"],
		["height_m", 0.0],
		["height_m", 0.04],
		["radius_m", 0.06],
		["eye_height_m", 0.11],
		["interaction_reach_m", -1.0],
		["unknown_authority", true],
	]
	for mutation in mutations:
		var changed := profile.duplicate(true)
		changed[mutation[0]] = mutation[1]
		if contract.call("ValidateProfile", changed):
			_fail("invalid " + String(mutation[0]) + " accepted")
			return
	var second: Dictionary = contract.call("GetDefaultProfile")
	profile["height_m"] = 2.0
	if not is_equal_approx(second["height_m"], 0.10):
		_fail("caller mutation changed the canonical profile")
		return
	print("Native interop smoke passed: C# 0.10 m profile, meter units, Variant validation, and caller isolation")
	quit(0)


func _fail(message: String) -> void:
	push_error("Native interop smoke: " + message)
	quit(1)
