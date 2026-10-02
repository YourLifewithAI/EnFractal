extends SceneTree
## Low-level ENet+DTLS path proof only, not account authentication or E09.
## Both hosts explicitly bind IPv4 loopback. Keys exist only in memory.

const LOOPBACK := "127.0.0.1"
const SERVER_NAME := "enfractal-test.invalid"
const PAYLOAD := "ENFRACTAL_LOOPBACK_TRANSPORT_PROBE"
const ACK := "ENFRACTAL_LOOPBACK_ACK"
const CASE_TIMEOUT_MS := 2000


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var crypto := Crypto.new()
	var key := crypto.generate_rsa(2048)
	var other_key := crypto.generate_rsa(2048)
	var issuer := "CN=%s,O=Enfractal Local Test,C=US" % SERVER_NAME
	var now_s := int(Time.get_unix_time_from_system())
	var cert := crypto.generate_self_signed_certificate(key, issuer, _x509_time(now_s - 86400), _x509_time(now_s + 86400))
	var other_cert := crypto.generate_self_signed_certificate(other_key, issuer, _x509_time(now_s - 86400), _x509_time(now_s + 86400))
	var expired_cert := crypto.generate_self_signed_certificate(key, issuer, _x509_time(now_s - 172800), _x509_time(now_s - 86400))
	if key == null or cert == null or other_cert == null or expired_cert == null:
		push_error("Could not construct ephemeral test certificate fixtures.")
		quit(1)
		return
	var cases: Array[Dictionary] = []
	cases.append(_probe("trusted_matching_name", key, cert, cert, SERVER_NAME, true, true))
	cases.append(_probe("wrong_hostname", key, cert, cert, "wrong-test.invalid", true, false))
	cases.append(_probe("untrusted_certificate", key, cert, other_cert, SERVER_NAME, true, false))
	cases.append(_probe("expired_certificate", key, expired_cert, expired_cert, SERVER_NAME, true, false))
	cases.append(_probe("plaintext_client_to_dtls_server", key, cert, cert, SERVER_NAME, false, false))
	var passed := true
	for case_result in cases:
		passed = passed and bool(case_result["passed"])
	var report := {
		"schema": "enfractal-transport-smoke-v1",
		"run_id": OS.get_environment("ENFRACTAL_TRANSPORT_RUN_ID"),
		"engine": Engine.get_version_info()["string"],
		"os": OS.get_name(),
		"utc": Time.get_datetime_string_from_system(true),
		"bind_address": LOOPBACK,
		"public_listener": false,
		"private_keys_saved": false,
		"scope": "ENetConnection DTLS loopback path; no account auth, replay ledger, packet capture, remote host or high-level RPC proof",
		"passed": passed,
		"cases": cases,
	}
	var report_text := JSON.stringify(report, "\t")
	var output_path := OS.get_environment("ENFRACTAL_TRANSPORT_REPORT")
	if not output_path.is_empty():
		var output := FileAccess.open(output_path, FileAccess.WRITE)
		if output == null:
			push_error("Cannot write transport report.")
			quit(1)
			return
		output.store_string(report_text + "\n")
		output.close()
	print(report_text)
	quit(0 if passed else 1)


func _x509_time(unix_seconds: int) -> String:
	return Time.get_datetime_string_from_unix_time(unix_seconds).replace("-", "").replace(":", "").replace("T", "")


func _probe(case_name: String, key: CryptoKey, cert: X509Certificate, trusted: X509Certificate,
		hostname: String, use_dtls: bool, expect_round_trip: bool) -> Dictionary:
	var server := ENetConnection.new()
	var client := ENetConnection.new()
	var result := {
		"name": case_name, "passed": false, "setup_ok": false,
		"server_connected": false, "client_connected": false,
		"server_received": false, "client_received": false,
		"service_error_observed": false, "elapsed_ms": 0,
	}
	# create_host() binds an unspecified address; deliberately never use it here.
	var server_error := server.create_host_bound(LOOPBACK, 0, 1, 2)
	var client_error := client.create_host_bound(LOOPBACK, 0, 1, 2)
	if server_error != OK or client_error != OK:
		result["setup_error"] = "loopback_bind_failed"
		server.destroy()
		client.destroy()
		return result
	if server.dtls_server_setup(TLSOptions.server(key, cert)) != OK:
		result["setup_error"] = "dtls_server_setup_failed"
		server.destroy()
		client.destroy()
		return result
	if use_dtls and client.dtls_client_setup(hostname, TLSOptions.client(trusted)) != OK:
		result["setup_error"] = "dtls_client_setup_failed"
		server.destroy()
		client.destroy()
		return result
	var outgoing := client.connect_to_host(LOOPBACK, server.get_local_port(), 2)
	if outgoing == null:
		result["setup_error"] = "connect_allocation_failed"
		server.destroy()
		client.destroy()
		return result
	result["setup_ok"] = true
	var started := Time.get_ticks_msec()
	while Time.get_ticks_msec() - started < CASE_TIMEOUT_MS:
		var client_event := client.service(0)
		var server_event := server.service(0)
		if client_event[0] == ENetConnection.EVENT_CONNECT:
			result["client_connected"] = true
			if outgoing.send(0, PAYLOAD.to_utf8_buffer(), ENetPacketPeer.FLAG_RELIABLE) != OK:
				result["setup_error"] = "client_send_failed"
				break
		if server_event[0] == ENetConnection.EVENT_CONNECT:
			result["server_connected"] = true
		if server_event[0] == ENetConnection.EVENT_RECEIVE:
			var server_peer: ENetPacketPeer = server_event[1]
			result["server_received"] = server_peer.get_packet().get_string_from_utf8() == PAYLOAD
			if server_peer.send(0, ACK.to_utf8_buffer(), ENetPacketPeer.FLAG_RELIABLE) != OK:
				result["setup_error"] = "server_send_failed"
				break
		if client_event[0] == ENetConnection.EVENT_RECEIVE:
			var client_peer: ENetPacketPeer = client_event[1]
			result["client_received"] = client_peer.get_packet().get_string_from_utf8() == ACK
			break
		if client_event[0] == ENetConnection.EVENT_ERROR or server_event[0] == ENetConnection.EVENT_ERROR:
			result["service_error_observed"] = true
			break
		OS.delay_msec(1)
	result["elapsed_ms"] = Time.get_ticks_msec() - started
	var round_trip := bool(result["server_received"]) and bool(result["client_received"])
	if expect_round_trip:
		result["passed"] = round_trip and not result.has("setup_error")
	else:
		result["passed"] = not result["server_connected"] and not result["client_connected"] and not result["server_received"] and not result["client_received"] and not result.has("setup_error")
		# Certificate cases must observe a rejection, not merely a quiet timeout.
		# The plaintext case intentionally proves bounded non-delivery only.
		if use_dtls:
			result["passed"] = result["passed"] and result["service_error_observed"]
	client.destroy()
	server.destroy()
	return result
