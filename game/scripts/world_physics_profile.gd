class_name WorldPhysicsProfile
extends RefCounted
## Bounded world physics for the room. Gravity, the air (how fast things fall at most and how much
## a body can steer in the air) and wind are world properties a trusted host supplies with a revision;
## clients cannot grant one. Body properties (size, step, jump height, speeds) live in the C#
## controller, so changing gravity changes how long a jump lasts, not how high it is.
## These presets are game feel, not physical accuracy; see docs/engine/phase3/body-and-physics.md.

const DEFAULT := {
	"id": "room_tuned",
	"revision": 0,
	"gravity_mps2": 3.5,
	# The world's fall limit; the body's own terminal fall (6 m/s) still applies.
	"terminal_fall_mps": 6.0,
	# Multiplier on the body's air acceleration.
	"air_control": 1.0,
	"wind_x_mps": 0.0,
	"wind_z_mps": 0.0,
}
## Presets the playtest cycles through, in order.
const PRESET_IDS := ["room_tuned", "room_real", "room_floaty"]
## Floaty (0.6 m/s2) sits above this; anything lower makes a 6.5 cm jump last well over a second.
const MIN_GRAVITY_MPS2 := 0.5
const MAX_GRAVITY_MPS2 := 30.0
const MIN_TERMINAL_FALL_MPS := 0.3
const MAX_TERMINAL_FALL_MPS := 12.0
const MIN_AIR_CONTROL := 0.25
const MAX_AIR_CONTROL := 3.0
const MAX_WIND_MPS := 1.0


static func preset(id: String, revision: int) -> Dictionary:
	var values: Dictionary = DEFAULT.duplicate(true)
	match id:
		"room_tuned":
			pass
		"room_real":
			values["gravity_mps2"] = 9.8
		"room_floaty":
			# Founder playtest, 6 October: "make the floaty version even more pronounced". A 6.5 cm jump
			# lasts about 0.93 s, falls drift down at no more than 0.6 m/s, and air steering is doubled.
			values["gravity_mps2"] = 0.6
			values["terminal_fall_mps"] = 0.6
			values["air_control"] = 2.0
		"room_breeze_test":
			values["wind_x_mps"] = 0.4
		_:
			return {}
	values["id"] = id
	values["revision"] = revision
	return values


static func validate(profile: Variant) -> bool:
	if typeof(profile) != TYPE_DICTIONARY:
		return false
	var rules: Dictionary = profile
	if rules.size() != DEFAULT.size():
		return false
	for key in DEFAULT:
		if not rules.has(key):
			return false
	if typeof(rules["id"]) != TYPE_STRING or String(rules["id"]).is_empty() or String(rules["id"]).length() > 48:
		return false
	if typeof(rules["revision"]) != TYPE_INT or int(rules["revision"]) < 0:
		return false
	for key in ["gravity_mps2", "terminal_fall_mps", "air_control", "wind_x_mps", "wind_z_mps"]:
		if not _finite_number(rules[key]):
			return false
	var gravity := float(rules["gravity_mps2"])
	var terminal := float(rules["terminal_fall_mps"])
	var air := float(rules["air_control"])
	var wind := Vector2(float(rules["wind_x_mps"]), float(rules["wind_z_mps"]))
	if gravity < MIN_GRAVITY_MPS2 or gravity > MAX_GRAVITY_MPS2:
		return false
	if terminal < MIN_TERMINAL_FALL_MPS or terminal > MAX_TERMINAL_FALL_MPS:
		return false
	return air >= MIN_AIR_CONTROL and air <= MAX_AIR_CONTROL and wind.length() <= MAX_WIND_MPS


static func _finite_number(value: Variant) -> bool:
	return (typeof(value) == TYPE_FLOAT or typeof(value) == TYPE_INT) and is_finite(float(value))
