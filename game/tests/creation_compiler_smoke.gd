extends SceneTree
## Exercises portable recipes, graph semantics and hostile manifest admission.

const COMPILER := preload("res://scripts/creation_compiler.gd")
var failed: Array[String] = []
var valid_count := 0
var invalid_count := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var templates := COMPILER.templates()
	_check(templates.size() == 5, "five data-only templates are present")
	for source in templates:
		_valid(source, "template " + source.name)
	var minimal := _minimal()
	_valid(minimal, "inert plinth")
	var multi := minimal.duplicate(true)
	multi.parts.append(_part("wing", "wing", [1, 1, 0], [2, 0.1, 1]))
	_valid(multi, "multipart editable source")
	var switched := _switched()
	_valid(switched, "manual switch light")
	var angled := minimal.duplicate(true)
	angled.parts[0].rotation_deg = [0, 45, 0]
	angled.parts[0].size_m = [4, 1, 2]
	var angled_result := _valid(angled, "rotated bounds")
	if angled_result.ok:
		var extent := (2.0 + 1.0) / sqrt(2.0)
		_check(absf(float(angled_result.artifact.bounds.max[0]) - extent) < 0.00001, "rotated footprint includes the four-metre long edge")
		_check(absf(float(angled_result.artifact.bounds.min[2]) + extent) < 0.00001, "rotated footprint protects the opposite corner")
	var boundaries: Dictionary = templates[1].duplicate(true)
	boundaries.seed = 2147483647
	for node in boundaries.nodes:
		if node.op == "wind":
			node.params = {"direction": [0.6, 0.8, 0], "acceleration_mps2": 0.1, "radius_m": 8, "duration_s": 0.1}
		if node.op == "spin":
			node.params = {"speed_rpm": -120, "duration_s": 5}
	_valid(boundaries, "inclusive capability bounds and signed rotor")
	var diamond := _diamond()
	var diamond_result := _valid(diamond, "diamond DAG")
	if diamond_result.ok:
		_check(diamond_result.artifact.order == ["use", "left", "right", "end"], "topological order is stable and joined node appears only once")
		_check(diamond_result.artifact.cost == {"parts":1,"nodes":4,"edges":4,"fields":0,"lights":3,"rotors":0}, "cost counts source components independently of graph reachability")
	var max_parts := minimal.duplicate(true)
	for i in range(1, 24):
		max_parts.parts.append(_part("p%d" % i, "box", [0, 0.5, 0], [1, 1, 1]))
	_valid(max_parts, "maximum supported part count")
	var max_nodes := minimal.duplicate(true)
	for i in range(16):
		max_nodes.nodes.append(_node("light%d" % i, "light", {"intensity": 0, "duration_s": 0.1}))
	_valid(max_nodes, "maximum supported node count")
	var max_edges := minimal.duplicate(true)
	for i in range(12):
		max_edges.nodes.append(_node("light%d" % i, "light", {"intensity": 1, "duration_s": 1}))
	for i in range(12):
		for j in range(i + 1, 12):
			if max_edges.edges.size() < 32:
				max_edges.edges.append({"from": "light%d" % i, "to": "light%d" % j})
	_valid(max_edges, "maximum supported edge count")
	# Numeric normalization and source arrays cannot make equivalent designs diverge.
	var before := COMPILER.canonical_json(diamond)
	var reordered := diamond.duplicate(true)
	reordered.parts[0].position_m = [-0.0, 0.5, 0.0]
	reordered.version = 1.0
	reordered.seed = 1.0
	reordered.nodes.reverse()
	reordered.edges.reverse()
	var reordered_result := _valid(reordered, "numeric and graph order normalization")
	_check(reordered_result.ok and reordered_result.artifact.hash == diamond_result.artifact.hash, "equivalent source has the same hash")
	_check(COMPILER.canonical_json(diamond) == before, "compiler preserves the caller's editable draft")
	var decoded: Variant = JSON.parse_string(COMPILER.canonical_json(diamond_result.artifact.source))
	var round_trip := _valid(decoded, "portable JSON round trip")
	_check(round_trip.ok and round_trip.artifact.hash == diamond_result.artifact.hash, "JSON reload preserves the normalized hash")
	_check(COMPILER.canonical_json({"b": 2.0,"a": -0.0}) == "{\"a\":0,\"b\":2}", "canonical JSON sorts keys and normalizes integers and negative zero")
	var changed := diamond.duplicate(true)
	changed.nodes[1].params.intensity = 1.25
	_check(COMPILER.compile(changed).artifact.hash != diamond_result.artifact.hash, "functional source edits change the hash")
	# Invalid cases are independent mutations, each with a stable code and field.
	_invalid(_changed(minimal, "version", 2), "version", "$.version", "future version")
	_invalid(_changed(minimal, "owner_id", "host"), "unknown_field", "$.owner_id", "forged owner")
	_invalid(_changed(minimal, "script", "res://unsafe.gd"), "unknown_field", "$.script", "arbitrary script")
	_invalid(_changed(minimal, "name", "x".repeat(65)), "name", "$.name", "long name")
	_invalid(_changed(minimal, "name", "bad\nname"), "name", "$.name", "control character")
	_invalid(_changed(minimal, "seed", true), "seed", "$.seed", "boolean seed")
	_invalid(_changed(minimal, "seed", 1.5), "seed", "$.seed", "fractional seed")
	_invalid(_changed(minimal, "mount", "world"), "mount", "$.mount", "unknown mount")
	var bad := minimal.duplicate(true)
	bad.erase("parts")
	_invalid(bad, "missing_field", "$.parts", "missing collection")
	_invalid(_changed(minimal, "parts", {}), "type", "$.parts", "object instead of parts")
	_invalid(_changed(minimal, "parts", []), "part_budget", "$.parts", "empty assembly")
	bad = max_parts.duplicate(true)
	bad.parts.append(_part("overflow", "box", [0, 0.5, 0], [1, 1, 1]))
	_invalid(bad, "part_budget", "$.parts", "part overflow")
	bad = max_nodes.duplicate(true)
	bad.nodes.append(_node("overflow", "light", {"intensity": 1,"duration_s": 1}))
	_invalid(bad, "node_budget", "$.nodes", "node overflow")
	bad = max_edges.duplicate(true)
	bad.edges.append({"from":"light10","to":"light11"})
	_invalid(bad, "edge_budget", "$.edges", "edge overflow")
	bad = minimal.duplicate(true)
	bad.parts.append(bad.parts[0].duplicate(true))
	_invalid(bad, "duplicate_part", "$.parts[1].id", "duplicate part ID")
	bad = minimal.duplicate(true)
	bad.parts[0].id = "../world"
	_invalid(bad, "id", "$.parts[0].id", "external-looking ID")
	bad = minimal.duplicate(true)
	bad.parts[0].shape = "scripted_mesh"
	_invalid(bad, "shape", "$.parts[0].shape", "unknown shape")
	bad = minimal.duplicate(true)
	bad.parts[0].material = "custom_shader"
	_invalid(bad, "material", "$.parts[0].material", "unknown material")
	bad = minimal.duplicate(true)
	bad.parts[0].size_m[0] = 4.01
	_invalid(bad, "range", "$.parts[0].size_m[0]", "oversized geometry")
	bad = minimal.duplicate(true)
	bad.parts[0].size_m[1] = 0.09
	_invalid(bad, "range", "$.parts[0].size_m[1]", "undersized geometry")
	bad = minimal.duplicate(true)
	bad.parts[0].position_m = [0, 0.5]
	_invalid(bad, "vector", "$.parts[0].position_m", "short vector")
	bad = minimal.duplicate(true)
	bad.parts[0].position_m[1] = "0.5"
	_invalid(bad, "number", "$.parts[0].position_m[1]", "numeric string")
	bad = minimal.duplicate(true)
	bad.parts[0].size_m[0] = NAN
	_invalid(bad, "number", "$.parts[0].size_m[0]", "nonfinite number")
	bad = minimal.duplicate(true)
	bad.parts[0].position_m[1] = 0
	_invalid(bad, "below_ground", "$.parts", "unrotated underground part")
	bad = minimal.duplicate(true)
	bad.parts[0].rotation_deg[2] = 45
	_invalid(bad, "below_ground", "$.parts", "rotated corner underground")
	bad = switched.duplicate(true)
	bad.nodes[1].op = "spawn"
	_invalid(bad, "operation", "$.nodes[1].op", "recursive event spawning")
	bad = switched.duplicate(true)
	bad.nodes[1].part_id = "foreign"
	_invalid(bad, "part_reference", "$.nodes[1].part_id", "missing part reference")
	bad = switched.duplicate(true)
	bad.nodes[1].params["target_avatar"] = "guest"
	_invalid(bad, "unknown_field", "$.nodes[1].params.target_avatar", "caller force target")
	bad = switched.duplicate(true)
	bad.nodes[1].params.intensity = true
	_invalid(bad, "number", "$.nodes[1].params.intensity", "boolean capability parameter")
	bad = switched.duplicate(true)
	bad.nodes[1].params.duration_s = 5.01
	_invalid(bad, "range", "$.nodes[1].params.duration_s", "unbounded lifetime")
	bad = switched.duplicate(true)
	bad.nodes[1].params.erase("intensity")
	_invalid(bad, "missing_field", "$.nodes[1].params.intensity", "missing exact parameter")
	bad = switched.duplicate(true)
	bad.nodes.append(bad.nodes[1].duplicate(true))
	_invalid(bad, "duplicate_node", "$.nodes[2].id", "duplicate node")
	bad = switched.duplicate(true)
	bad.nodes.append(_node("use_two", "interact", {}))
	_invalid(bad, "single_capability", "$.nodes[2].op", "multiple interact triggers")
	bad = minimal.duplicate(true)
	bad.nodes.append(_node("glide", "glide", {"fall_speed_mps":3}))
	_invalid(bad, "operation_mount", "$.nodes[0].op", "ground glide")
	bad = minimal.duplicate(true)
	bad.nodes.append(_node("spin", "spin", {"speed_rpm":20,"duration_s":1}))
	_invalid(bad, "part_kind", "$.nodes[0].part_id", "spinning nonrotor")
	bad = templates[1].duplicate(true)
	for node in bad.nodes:
		if node.op == "wind":
			node.params.direction = [0, 0.5, 0]
	_invalid(bad, "unit_vector", "$.nodes[2].params.direction", "nonnormalized wind")
	bad = switched.duplicate(true)
	bad.edges[0].to = "missing"
	_invalid(bad, "node_reference", "$.edges[0].to", "missing wire endpoint")
	bad = switched.duplicate(true)
	bad.edges.append(bad.edges[0].duplicate())
	_invalid(bad, "duplicate_edge", "$.edges[1]", "duplicate wire")
	bad = switched.duplicate(true)
	bad.edges.append({"from":"light","to":"use"})
	_invalid(bad, "incoming_trigger", "$.edges[1].to", "trigger reentry")
	bad = diamond.duplicate(true)
	bad.edges.append({"from":"end","to":"left"})
	_invalid(bad, "cycle", "$.edges", "effect graph cycle")
	bad = minimal.duplicate(true)
	bad.nodes.append(_node("self", "light", {"intensity":1,"duration_s":1}))
	bad.edges.append({"from":"self","to":"self"})
	_invalid(bad, "cycle", "$.edges", "self cycle")
	bad = _changed(minimal, "padding", "x".repeat(32768))
	_invalid(bad, "json_budget", "$", "UTF-8 source budget")
	bad = minimal.duplicate(true)
	bad.parts[0].position_m = Vector3.ZERO
	_invalid(bad, "json_type", "$.parts[0].position_m", "engine object is not portable JSON")
	var nesting: Variant = 1
	for i in range(14):
		nesting = [nesting]
	_invalid({"nested": nesting}, "json_complexity", "$.nested", "deep JSON", true)
	# A reference cycle is rejected by the same finite-depth guard without recursion overflow.
	var recursive: Dictionary = {}
	recursive["self"] = recursive
	_invalid(recursive, "json_complexity", "$.self", "cyclic Variant", true)
	recursive.clear()
	_check(valid_count >= 10 and invalid_count >= 20, "required fixture coverage")
	if failed.is_empty():
		print("Creation compiler smoke passed: %d valid, %d invalid fixtures; stable hashes, rotated bounds, exact schemas and DAG order." % [valid_count, invalid_count])
		quit(0)
	else:
		for failure in failed:
			push_error(failure)
		quit(1)


func _valid(source: Variant, label: String) -> Dictionary:
	valid_count += 1
	var result := COMPILER.compile(source)
	_check(result.get("ok", false), label + ": " + str(result))
	return result


func _invalid(source: Variant, code: String, path: String, label: String, prefix := false) -> void:
	invalid_count += 1
	var result := COMPILER.compile(source)
	_check(not result.get("ok", true) and result.get("code") == code, label + ": expected " + code + ", got " + str(result))
	_check(str(result.get("path", "")).begins_with(path) if prefix else result.get("path") == path, label + ": incorrect field path " + str(result))
	_check(not str(result.get("message", "")).is_empty(), label + ": rejection needs a readable message")


func _check(condition: bool, label: String) -> void:
	if not condition:
		failed.append(label)


func _minimal() -> Dictionary:
	return {"schema":"enfractal.creation","version":1,"name":"Test invention","seed":1,"mount":"ground",
		"parts":[_part("base", "box", [0,0.5,0], [1,1,1])],"nodes":[],"edges":[]}


func _part(id: String, shape: String, position: Array, size: Array) -> Dictionary:
	return {"id":id,"shape":shape,"position_m":position,"rotation_deg":[0,0,0],"size_m":size,"material":"stone"}


func _node(id: String, op: String, params: Dictionary) -> Dictionary:
	return {"id":id,"op":op,"part_id":"base","params":params}


func _switched() -> Dictionary:
	var source := _minimal()
	source.nodes = [_node("use", "interact", {}), _node("light", "light", {"intensity":1,"duration_s":1})]
	source.edges = [{"from":"use","to":"light"}]
	return source


func _diamond() -> Dictionary:
	var source := _minimal()
	source.nodes = [_node("use", "interact", {}), _node("left", "light", {"intensity":1,"duration_s":1}),
		_node("right", "light", {"intensity":1,"duration_s":1}), _node("end", "light", {"intensity":1,"duration_s":1})]
	source.edges = [{"from":"use","to":"left"},{"from":"use","to":"right"},{"from":"left","to":"end"},{"from":"right","to":"end"}]
	return source


func _changed(source: Dictionary, key: String, value: Variant) -> Dictionary:
	var changed := source.duplicate(true)
	changed[key] = value
	return changed
