class_name WorldPhysicsProfile
extends RefCounted
## Bounded local physics rules for the walking fixture. A future world authority
## must own the revision and supply this profile; clients cannot grant one.

const DEFAULT := {
	"id": "earth_test",
	"revision": 0,
	"gravity_mps2": 22.0,
	"glide_gravity_mps2": 4.0,
	"wind_x_mps": 0.0,
	"wind_z_mps": 0.0,
}
const MAX_WIND_MPS := 8.0


static func preset(id: String, revision: int) -> Dictionary:
	var values: Dictionary
	match id:
		"earth_test":
			values = DEFAULT.duplicate(true)
		"light_gravity_test":
			values = DEFAULT.duplicate(true)
			values["id"] = id
			values["gravity_mps2"] = 9.0
			values["glide_gravity_mps2"] = 2.0
		"ridge_breeze_test":
			values = DEFAULT.duplicate(true)
			values["id"] = id
			values["wind_x_mps"] = 4.0
		_:
			return {}
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
	for key in ["gravity_mps2", "glide_gravity_mps2", "wind_x_mps", "wind_z_mps"]:
		if not _finite_number(rules[key]):
			return false
	var gravity := float(rules["gravity_mps2"])
	var glide_gravity := float(rules["glide_gravity_mps2"])
	var wind := Vector2(float(rules["wind_x_mps"]), float(rules["wind_z_mps"]))
	return gravity >= 4.0 and gravity <= 30.0 \
		and glide_gravity >= 0.5 and glide_gravity <= gravity \
		and wind.length() <= MAX_WIND_MPS


static func _finite_number(value: Variant) -> bool:
	return (typeof(value) == TYPE_FLOAT or typeof(value) == TYPE_INT) and is_finite(float(value))
