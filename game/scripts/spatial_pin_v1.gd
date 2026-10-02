extends RefCounted
## Bounded spatial-pin v1 fixture. This does not replace WorldState's v2 save pin.
## All field values are normalized ASCII strings; no JSON numeric formatting is used.

const DOMAIN := "enfractal-spatial-pin-v1\n"
const MAX_BYTES := 8192
const FIELDS := [
	"axis_x", "axis_y", "axis_z", "center_lat_deg", "center_lon_deg",
	"center_projected_easting_m", "center_projected_northing_m",
	"features_sha256", "frame_id", "geographic_crs", "grid_side_samples",
	"height_decoder_id", "height_offset_m", "height_origin_m",
	"height_scale_m", "heights_sha256", "horizontal_crs", "map_format",
	"map_id", "sample_spacing_m", "side_m", "surface_algorithm_id",
	"surface_outside_id", "tile_side_m", "transform_pipeline_id",
	"vertical_datum_id", "vertical_scope", "vertical_transform_id",
]
const DECIMAL_FIELDS := [
	"center_lat_deg", "center_lon_deg", "center_projected_easting_m",
	"center_projected_northing_m", "height_offset_m", "height_origin_m",
	"height_scale_m", "sample_spacing_m", "side_m", "tile_side_m",
]


static func _matches(value: String, expression: String) -> bool:
	var regex := RegEx.new()
	if regex.compile(expression) != OK:
		return false
	var match_value := regex.search(value)
	return match_value != null and match_value.get_string() == value


static func _decimal_valid(value: String) -> bool:
	return value.length() <= 32 and _matches(value, "^-?(0|[1-9][0-9]*)(\\.[0-9]*[1-9])?$") and value != "-0"


static func _within_abs_integer_bound(value: String, limit: int) -> bool:
	var unsigned_value := value.trim_prefix("-")
	var whole := unsigned_value.get_slice(".", 0)
	if whole.length() > str(limit).length():
		return false
	var whole_number := whole.to_int()
	return whole_number < limit or (whole_number == limit and not unsigned_value.contains("."))


static func _millimetres(value: String) -> int:
	var parts := value.split(".")
	var fraction := ""
	if parts.size() == 2:
		fraction = parts[1]
	while fraction.length() < 3:
		fraction += "0"
	return parts[0].to_int() * 1000 + fraction.to_int()


static func validate(descriptor: Dictionary) -> bool:
	if descriptor.size() != FIELDS.size():
		return false
	for field in FIELDS:
		if not descriptor.has(field) or typeof(descriptor[field]) != TYPE_STRING:
			return false
		var value: String = descriptor[field]
		if DECIMAL_FIELDS.has(field):
			if not _decimal_valid(value) or not _within_abs_integer_bound(value, 1000000000):
				return false
		elif field == "grid_side_samples":
			if not _matches(value, "^[1-9][0-9]{0,4}$") or value.to_int() < 2 or value.to_int() > 65536:
				return false
		elif field == "heights_sha256" or field == "features_sha256":
			if not _matches(value, "^[0-9a-f]{64}$"):
				return false
		elif value.length() > 128 or not _matches(value, "^[A-Za-z][A-Za-z0-9_:.+/-]*$"):
			return false
	if not _within_abs_integer_bound(descriptor["center_lat_deg"], 90):
		return false
	if not _within_abs_integer_bound(descriptor["center_lon_deg"], 180):
		return false
	for field in ["height_scale_m", "sample_spacing_m", "side_m", "tile_side_m"]:
		if descriptor[field].begins_with("-") or descriptor[field] == "0":
			return false
	for field in ["sample_spacing_m", "side_m", "tile_side_m"]:
		var value: String = descriptor[field]
		if not _within_abs_integer_bound(value, 1000000) or (value.contains(".") and value.get_slice(".", 1).length() > 3):
			return false
	var spacing := _millimetres(descriptor["sample_spacing_m"])
	var side := _millimetres(descriptor["side_m"])
	var tile := _millimetres(descriptor["tile_side_m"])
	if spacing <= 0 or tile <= 0 or side <= 0:
		return false
	if (descriptor["grid_side_samples"].to_int() - 1) * spacing != side:
		return false
	if tile > side or side % tile != 0 or tile % spacing != 0:
		return false
	if descriptor["vertical_datum_id"] == "unknown":
		if descriptor["vertical_scope"] != "local_only" or descriptor["vertical_transform_id"] != "none":
			return false
	elif descriptor["vertical_scope"] != "source_declared":
		return false
	if descriptor["surface_algorithm_id"] not in ["heightfield_acb_bcd_diagonal_bc_v1", "heightfield_abc_bdc_diagonal_ad_v1"]:
		return false
	if descriptor["surface_outside_id"] != "closed_square_error_outside_v1":
		return false
	return descriptor["height_decoder_id"] == "uint16le_row_north_offset_scale_v1"


static func encode(descriptor: Dictionary) -> PackedByteArray:
	if not validate(descriptor):
		return PackedByteArray()
	var serialized := DOMAIN
	for field in FIELDS:
		serialized += field + "=" + descriptor[field] + "\n"
	var raw := serialized.to_utf8_buffer()
	return raw if raw.size() <= MAX_BYTES else PackedByteArray()


static func decode(raw: PackedByteArray) -> Dictionary:
	if raw.size() > MAX_BYTES:
		return {}
	for byte_value in raw:
		if byte_value > 127:
			return {}
	var lines := raw.get_string_from_ascii().split("\n", true)
	if lines.size() != FIELDS.size() + 2 or lines[0] != DOMAIN.trim_suffix("\n") or lines[-1] != "":
		return {}
	var descriptor := {}
	for index in FIELDS.size():
		var line: String = lines[index + 1]
		var equal_at := line.find("=")
		if equal_at < 0 or line.substr(0, equal_at) != FIELDS[index]:
			return {}
		descriptor[FIELDS[index]] = line.substr(equal_at + 1)
	if encode(descriptor) != raw:
		return {}
	return descriptor


static func digest(descriptor: Dictionary) -> String:
	var raw := encode(descriptor)
	return "" if raw.is_empty() else raw.get_string_from_ascii().sha256_text()
