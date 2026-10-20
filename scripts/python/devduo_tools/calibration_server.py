#!/usr/bin/env python3
"""Loopback calibration receiver; Python standard library only."""

import argparse
import ipaddress
import json
import mimetypes
import pathlib
import threading
import uuid
from datetime import UTC, datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from .paths import repository_root

ROOT = repository_root()


class Server(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, address, output):
        if address[0] != "localhost" and not ipaddress.ip_address(address[0]).is_loopback:
            raise ValueError("The calibration receiver must bind to a loopback address")
        super().__init__(address, Handler)
        self.output = output.resolve()
        self.output.mkdir(parents=True, exist_ok=True)
        self.lock = threading.Lock()


class Handler(BaseHTTPRequestHandler):
    def setup(self):
        super().setup()
        self.connection.settimeout(10)

    def reply(self, status, value):
        data = json.dumps(value, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/health":
            return self.reply(200, {"status": "ok", "schemaVersion": 1})
        name = {
            "/": "index.html",
            "/calibration.js": "calibration.js",
            "/style.css": "style.css",
            "/document-end.html": "document-end.html",
        }.get(path)
        if not name:
            return self.reply(404, {"error": "not found"})
        data = (ROOT / "web" / name).read_bytes()
        self.send_response(200)
        self.send_header(
            "Content-Type", mimetypes.guess_type(name)[0] or "application/octet-stream"
        )
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        if self.path != "/api/measurements":
            return self.reply(404, {"error": "not found"})
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 1048576:
                return self.reply(413, {"error": "expected body between 1 byte and 1 MiB"})
            record = json.loads(self.rfile.read(length))
            if not isinstance(record, dict) or record.get("schemaVersion") != 1:
                return self.reply(400, {"error": "schemaVersion must be 1"})
            for key in (
                "labels",
                "screen",
                "viewport",
                "safeAreaCssPx",
                "components",
                "environment",
            ):
                if key in record and not isinstance(record[key], dict):
                    return self.reply(400, {"error": key + " must be an object"})
            record["id"] = str(uuid.uuid4())
            record["receivedAt"] = datetime.now(UTC).isoformat()
            record["sourceAddress"] = self.client_address[0]
            with self.server.lock:
                (self.server.output / (record["id"] + ".json")).write_text(
                    json.dumps(record, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
                )
                with (self.server.output / "records.jsonl").open("a", encoding="utf-8") as stream:
                    stream.write(json.dumps(record, ensure_ascii=False) + "\n")
            self.reply(201, {"id": record["id"], "receivedAt": record["receivedAt"]})
        except (ValueError, UnicodeDecodeError):
            self.reply(400, {"error": "invalid JSON or Content-Length"})
        except OSError:
            self.reply(
                500, {"error": "Could not persist measurement; inspect the output directory"}
            )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "measurements")
    args = parser.parse_args()
    with Server((args.host, args.port), args.output) as server:
        print(
            f"Calibration: http://{args.host}:{args.port}/  records: {args.output.resolve()}",
            flush=True,
        )
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
