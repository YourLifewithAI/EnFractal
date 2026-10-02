"""Loopback-only HTTP adapter. Credentials come from an ignored config file."""
from __future__ import annotations
import argparse
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import re

from service import MAX_REQUEST_BYTES, Service


def make_handler(service, token):
    class Handler(BaseHTTPRequestHandler):
        server_version = "EnfractalLocalSave/1"

        def log_message(self, *_):
            pass  # Never log tokens, envelopes, URLs, bodies or HTTP headers.

        def send_json(self, status, result):
            body = json.dumps(result, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(body)

        def do_POST(self):
            self.connection.settimeout(8)
            if self.path != "/v1/action":
                return self.send_json(404, {"ok": False, "code": "not_found", "message": "Unknown local endpoint."})
            if not hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + token):
                return self.send_json(401, {"ok": False, "code": "unauthorized", "message": "A local session credential is required."})
            # A browser origin is never a trusted local-host adapter, even with a
            # guessed URL. No CORS support, cookies, query tokens or GET actions.
            if self.headers.get("Origin") or self.headers.get("Transfer-Encoding"):
                return self.send_json(400, {"ok": False, "code": "adapter_required", "message": "Use the local game adapter."})
            try:
                length = int(self.headers.get("Content-Length", "-1"))
                if not 0 < length <= MAX_REQUEST_BYTES:
                    raise ValueError()
                body = self.rfile.read(length)
                if len(body) != length:
                    raise ValueError()
                request = json.loads(body, parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
            except (ValueError, UnicodeError, TimeoutError, RecursionError):
                return self.send_json(400, {"ok": False, "code": "request_invalid", "message": "Use bounded, finite JSON."})
            self.send_json(200, service.dispatch("local_player", request))

        def do_GET(self):
            self.send_json(405, {"ok": False, "code": "method_required", "message": "Use authenticated POST."})

    return Handler


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8-sig"))
    token = config["token"]
    if not isinstance(token, str) or not re.fullmatch(r"[0-9a-f]{64}", token):
        raise SystemExit("Configuration requires a random 256-bit hexadecimal token.")
    service = Service(config["database_url"])
    server = ThreadingHTTPServer(("127.0.0.1", args.port), make_handler(service, token))
    server.daemon_threads = True
    print(f"Enfractal local save service listening on 127.0.0.1:{args.port}", flush=True)
    try:
        server.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
