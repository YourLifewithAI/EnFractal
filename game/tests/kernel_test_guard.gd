extends Logger
## Makes a script or engine error fail a kernel suite by itself. A GDScript runtime error aborts only the
## function it happens in, so before this guard a suite whose test function died half way still printed
## "N checks, 0 failures" and exited 0, and only the runner's stderr check caught it (Lane P review).
##
## Use: `var guard = GUARD.new()` and `OS.add_logger(guard)` first thing, then end with
## `quit(guard.exit_code(failed))`. A suite's own failed checks (push_error) are its failures already and
## warnings are left to the runner's stderr check; every other logged error counts here.

var errors := 0
var first := ""
var _lock := Mutex.new()


func _log_error(function: String, file: String, line: int, code: String, rationale: String, _editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
	if error_type == ERROR_TYPE_WARNING or function == "push_error":
		return
	_lock.lock()
	errors += 1
	if first.is_empty():
		first = "%s:%d %s %s" % [file, line, code, rationale]
	_lock.unlock()


func _log_message(_message: String, _error: bool) -> void:
	pass


## The suite's exit code: 1 when a check failed or any script or engine error was logged.
func exit_code(suite_failed: bool) -> int:
	OS.remove_logger(self)
	if errors > 0:
		print("%d script or engine error(s) during the suite; first: %s" % [errors, first])
	return 1 if suite_failed or errors > 0 else 0
