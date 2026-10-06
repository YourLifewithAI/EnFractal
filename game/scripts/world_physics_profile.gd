class_name WorldPhysicsProfile
extends RefCounted
## Bounded world physics for the room. Gravity and wind are world properties a trusted host
## supplies with a revision; clients cannot grant one. Body properties (size, step, jump height)
## live in the C# controller, so changing gravity changes how long a jump lasts, not how high it is.
## These presets are game feel, not physical accuracy; see docs/engine/phase3/body-and-physics.md.

const DEFAULT := {
	"id": "room_tuned",
	"revision": 0,
	"gravity_mps2": 3.5,
	"wind_x_mps": 0.0,
	"wind_z_mps": 0.0,
}
## Presets the playtest cycles through, in order.
const PRESET_IDS := ["room_tuned", "room_real", "room_floaty"]
const MIN_GRAVITY_MPS2 := 1.0
const MAX_GRAVITY_MPS2 := 30.0
const MAX_WIND_MPS := 1.0


static func preset(id: String, revision: int) -> Dictionary:
	var values: Dictionary = DEFAULT.duplicate(true)
	match id:
		"room_tuned":
			pass
		"room_real":
			values["gravity_mps2"] = 9.8
		"room_floaty":
			values["gravity_mps2"] = 1.6
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
	for key in ["gravity_mps2", "wind_x_mps", "wind_z_mps"]:
		if not _finite_number(rules[key]):
			return false
	var gravity := float(rules["gravity_mps2"])
	var wind := Vector2(float(rules["wind_x_mps"]), float(rules["wind_z_mps"]))
	return gravity >= MIN_GRAVITY_MPS2 and gravity <= MAX_GRAVITY_MPS2 and wind.length() <= MAX_WIND_MPS


static func _finite_number(value: Variant) -> bool:
	return (typeof(value) == TYPE_FLOAT or typeof(value) == TYPE_INT) and is_finite(float(value))
