extends RefCounted
## Private loopback control connection. Never accepts a user-supplied remote URL.
const MAX_RESPONSE := 8 * 1024 * 1024
var port := 47631
var token := ""
var timeout_ms := 2500

func call_service(request: Dictionary) -> Dictionary:
	if token.length() < 32 or port < 1024 or port > 65535:
		return _failed("service_configuration", "Start the Save and Travel launcher to connect this workshop.")
	var body := JSON.stringify(request)
	if body.to_utf8_buffer().size() > 5 * 1024 * 1024:
		return _failed("request_limit", "This save exceeds the travel service limit.")
	var client := HTTPClient.new()
	var deadline := Time.get_ticks_msec() + timeout_ms
	if client.connect_to_host("127.0.0.1", port) != OK:
		return _failed("service_unavailable", "The save service is unavailable. Reconnect before editing.")
	while client.get_status() in [HTTPClient.STATUS_RESOLVING, HTTPClient.STATUS_CONNECTING]:
		client.poll()
		if Time.get_ticks_msec() >= deadline:
			return _timeout(client)
		OS.delay_msec(1)
	if client.get_status() != HTTPClient.STATUS_CONNECTED:
		return _failed("service_unavailable", "The save service is unavailable. Reconnect before editing.")
	var headers := PackedStringArray(["Content-Type: application/json", "Authorization: Bearer " + token])
	if client.request(HTTPClient.METHOD_POST, "/v1/action", headers, body) != OK:
		return _failed("service_unavailable", "The request could not be sent. Reconnect before editing.")
	while client.get_status() == HTTPClient.STATUS_REQUESTING:
		client.poll()
		if Time.get_ticks_msec() >= deadline:
			return _timeout(client)
		OS.delay_msec(1)
	if not client.has_response():
		return _failed("commit_unknown", "The service did not confirm the result. Reconnect to recover the saved outcome.")
	var received := PackedByteArray()
	while client.get_status() == HTTPClient.STATUS_BODY:
		client.poll()
		received.append_array(client.read_response_body_chunk())
		if received.size() > MAX_RESPONSE:
			client.close()
			return _failed("response_limit", "The service response exceeded the bounded save size.")
		if Time.get_ticks_msec() >= deadline:
			return _timeout(client)
		OS.delay_msec(1)
	var parsed = JSON.parse_string(received.get_string_from_utf8())
	client.close()
	if not parsed is Dictionary or not parsed.get("ok") is bool:
		return _failed("response_invalid", "The service returned an unreadable result. Reconnect before editing.")
	return parsed

func _timeout(client: HTTPClient) -> Dictionary:
	client.close()
	return _failed("commit_unknown", "The save or journey may have committed. Reconnect to recover its durable result.")

func _failed(code: String, message: String) -> Dictionary:
	return {"ok": false, "code": code, "message": message}
