#!/usr/bin/env python3
import argparse
import json
import mimetypes
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

def main():
    parser = argparse.ArgumentParser(description="GE360 read-only observability dashboard")
    parser.add_argument(
        "--state-dir",
        default=os.environ.get(
            "GE360_OBSERVABILITY_DIR",
            "ge360-state/observability"))
    parser.add_argument("--dashboard-dir", required=True)
    parser.add_argument(
        "--bind",
        default=os.environ.get(
            "GE360_OBSERVABILITY_BIND",
            "127.0.0.1"))
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.environ.get(
            "GE360_OBSERVABILITY_PORT",
            "9891")))
    args = parser.parse_args()

    state_dir = Path(args.state_dir).resolve()
    dashboard_dir = Path(args.dashboard_dir).resolve()

    class Handler(BaseHTTPRequestHandler):
        server_version = "GE360Observability/1.0"

        def do_GET(self):
            path = urlparse(self.path).path
            if path in ("/", "/index.html"):
                return self._send_file(dashboard_dir / "index.html", "text/html; charset=utf-8")
            if path == "/runtime-status.json":
                return self._send_json_file(state_dir / "runtime-status.json", {})
            if path == "/recent-events.json":
                return self._send_json_file(state_dir / "recent-events.json", [])
            self.send_error(404)

        def do_HEAD(self):
            path = urlparse(self.path).path
            if path in ("/", "/index.html", "/runtime-status.json", "/recent-events.json"):
                self.send_response(200)
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                return
            self.send_error(404)

        def do_POST(self): self.send_error(405)
        def do_PUT(self): self.send_error(405)
        def do_DELETE(self): self.send_error(405)
        def do_PATCH(self): self.send_error(405)

        def _send_json_file(self, path, fallback):
            if not path.exists():
                body = json.dumps(fallback).encode()
            else:
                try:
                    json.loads(path.read_text())
                    body = path.read_bytes()
                except Exception:
                    body = json.dumps(fallback).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def _send_file(self, path, content_type=None):
            if not path.is_file():
                self.send_error(404)
                return
            body = path.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", content_type or mimetypes.guess_type(path.name)[0] or "application/octet-stream")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, fmt, *args):
            print("GE360 dashboard:", fmt % args)

    httpd = ThreadingHTTPServer((args.bind, args.port), Handler)
    print(f"GE360 observability dashboard: http://{args.bind}:{args.port}")
    httpd.serve_forever()

if __name__ == "__main__":
    main()
