extends SceneTree
const GUARD = preload("res://tests/kernel_test_guard.gd")
## Fails the suite on any script or engine error (kernel_test_guard.gd).
var guard = GUARD.new()
const AUTHORITY = preload("res://scripts/creation_authority.gd")
const COMPILER = preload("res://scripts/creation_compiler.gd")
const PLAYER := "player:local"
const ROOM := {"room_id": "durable_fixture", "manifest_sha256": "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
	"bounds": {"min_m": [-60, -1, 290], "max_m": [60, 30, 410]}}
const OTHER_ROOM := {"room_id": "durable_fixture", "manifest_sha256": "fedcba0987654321fedcba0987654321fedcba0987654321fedcba0987654321",
	"bounds": {"min_m": [-60, -1, 290], "max_m": [60, 30, 410]}}
var checks := 0
var failures := 0
var disk: Dictionary = {}
var acknowledge := true
var permit := true
var calls := 0

func _initialize() -> void:
	OS.add_logger(guard)
	call_deferred("_run")

func check(value: bool, label: String) -> void:
	checks += 1
	if not value:
		failures += 1
		push_error(label)

func _save(envelope: Dictionary) -> Dictionary:
	calls += 1
	disk = envelope.duplicate(true)
	return {"ok":acknowledge,"message":"Commit result unknown; recover before editing."}

func _height(_x: float, _z: float, _from_y: float) -> Dictionary:
	return {"ok": true, "height_m": 0.0, "entity_id": "shell:floor"}

func _guard() -> bool:
	return permit

func _request(authority, action: String) -> Dictionary:
	var source: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://tests/fixtures/creations/fixture_use_rotor_wind.json"))
	return {"op":"place","action_id":action,"source":source,"x_m":-20.0,"z_m":340.0,"yaw_deg":0.0,"expected_revision":authority.revision,"expected_permission_revision":authority.permission_revision}

func _run() -> void:
	var authority = AUTHORITY.new()
	var path := "user://tests/durable_sink_%d.json" % OS.get_process_id()
	authority.configure(ROOM,Callable(self,"_height"),path)
	authority.persistence_sink = Callable(self,"_save")
	authority.access_guard = Callable(self,"_guard")
	var request := _request(authority,"first")
	check(authority.submit(PLAYER,request).ok,"durable acknowledged placement")
	check(calls == 1 and disk.instances.size() == 1,"one sink commit with editable source")
	check(not FileAccess.file_exists(path),"external sink never writes a competing local world file")
	check(not disk.instances[0].has("artifact"),"derivatives not durable source")
	var exported: Dictionary = authority.export_envelope()
	exported.instances.clear()
	check(authority.snapshot(PLAYER).instances.size() == 1,"export has no mutable reference to host state")
	check(authority.submit(PLAYER,request).replayed and calls == 1,"retry uses durable receipt without duplicate commit")
	acknowledge = false
	var uncertain := _request(authority,"uncertain")
	var failed: Dictionary = authority.submit(PLAYER,uncertain)
	check(not failed.ok and authority.revision == 1,"unknown commit never acknowledges or changes visible revision")
	check(authority.snapshot(PLAYER).instances.size() == 1 and disk.instances.size() == 2,"uncertainty holds old local state while database may have committed")
	permit = false
	check(not authority.is_ready(),"host fence disables readiness")
	check(not authority.submit(PLAYER,_request(authority,"blocked")).ok and calls == 2,"fenced writer cannot call persistence")
	permit = true
	check(authority.load_envelope(disk).ok,"reload reconciles committed result")
	check(authority.snapshot(PLAYER).instances.size() == 2,"committed creation survives lost response")
	check(authority.submit(PLAYER,uncertain).replayed and calls == 2,"original action receipt survives recovery")
	check(not authority.snapshot(PLAYER).consent[PLAYER],"recover resets consent")
	var wrong: Dictionary = disk.duplicate(true)
	wrong.room_pin.manifest_sha256 = OTHER_ROOM.manifest_sha256
	check(not authority.load_envelope(wrong).ok and not authority.is_ready(),"foreign room pin fails closed")
	check(authority.load_envelope(disk).ok,"valid pinned envelope can recover")
	wrong = disk.duplicate(true)
	wrong.instances[0].source.parts[0].material = "arbitrary_shader"
	check(not authority.load_envelope(wrong).ok,"restored source goes through same compiler")
	var blank = AUTHORITY.new()
	blank.configure(ROOM,Callable(self,"_height"),"user://tests/blank_unused.json")
	check(authority.load_envelope(blank.export_envelope()).ok,"empty sandbox source accepted")
	check(authority.snapshot(PLAYER).instances.is_empty(),"switching world drops old creations and receipts")
	print("Durable creation adapter: %d checks, %d failures" % [checks,failures])
	quit(guard.exit_code(failures != 0))
