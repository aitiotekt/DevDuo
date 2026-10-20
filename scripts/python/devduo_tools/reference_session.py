"""Run an operator-controlled native Safari swipe session; poses stay in Device Hub."""

import argparse
import hashlib
import hmac
import json
import os
import secrets
import shutil
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from .paths import repository_root


class SwipeBroker:
    """A loopback capability permits only the current fixed native gesture command."""

    def __init__(self, output):
        self.output = output
        self.path = "/reference/" + secrets.token_urlsafe(32)
        self.command = {"id": "idle", "action": "idle"}
        self.receipt = None
        self.lock = threading.Condition()
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_args):
                pass

            def reply(self, status, value):
                data = json.dumps(value).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def do_GET(self):
                if not hmac.compare_digest(self.path, owner.path):
                    return self.reply(404, {"error": "not found"})
                with owner.lock:
                    self.reply(200, owner.command)

            def do_POST(self):
                if not hmac.compare_digest(self.path, owner.path):
                    return self.reply(404, {"error": "not found"})
                try:
                    length = int(self.headers.get("Content-Length", "0"))
                    if not 0 < length <= 1048576:
                        return self.reply(413, {"error": "invalid body size"})
                    receipt = json.loads(self.rfile.read(length))
                    if not isinstance(receipt, dict):
                        raise ValueError("expected object")
                except (ValueError, UnicodeDecodeError):
                    return self.reply(400, {"error": "invalid JSON"})
                with owner.lock:
                    if any(receipt.get(key) != owner.command[key] for key in ("id", "action")):
                        return self.reply(409, {"error": "stale command"})
                    if owner.receipt is not None:
                        return self.reply(409, {"error": "receipt already saved"})
                    target = owner.output / (receipt["id"] + ".json")
                    try:
                        with target.open("x", encoding="utf-8") as stream:
                            stream.write(json.dumps(receipt, indent=2) + "\n")
                    except OSError:
                        return self.reply(500, {"error": "could not save receipt"})
                    owner.receipt = receipt
                    owner.lock.notify_all()
                    self.reply(200, {"ok": True})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.daemon_threads = True
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def start(self):
        self.thread.start()
        return f"http://127.0.0.1:{self.server.server_port}" + self.path

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)

    def request(self, action, identifier, process, timeout=180):
        if action not in {"observe", "down", "up", "stop"}:
            raise ValueError("Expected observe, down, up or stop")
        if not identifier or any(
            c not in "abcdefghijklmnopqrstuvwxyz0123456789-" for c in identifier
        ):
            raise ValueError("Command IDs require lowercase ASCII letters, digits and hyphens")
        with self.lock:
            if (self.output / (identifier + ".json")).exists():
                raise ValueError("Command ID already used")
            self.command = {"id": identifier, "action": action}
            self.receipt = None
            deadline = time.monotonic() + timeout
            while self.receipt is None:
                if process.poll() is not None:
                    raise RuntimeError("XCTest exited; inspect the session logs")
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError("Native gesture timed out; inspect XCTest logs")
                self.lock.wait(min(remaining, 1))
            return self.receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--udid", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    root = repository_root()
    sources = [Path(__file__).resolve(), root / "scripts/DeviceHubUITests/SafariProbe.swift"]
    hashes = {}
    for source in sources:
        shutil.copyfile(source, output / source.name)
        hashes[source.relative_to(root).as_posix()] = hashlib.sha256(
            source.read_bytes()
        ).hexdigest()
    (output / "source-sha256.json").write_text(json.dumps(hashes, indent=2) + "\n")
    broker = SwipeBroker(output)
    endpoint = broker.start()
    command = [
        "xcodebuild",
        "-project",
        str(root / "scripts/DeviceHubUITests/SafariProbe.xcodeproj"),
        "-scheme",
        "SafariProbe",
        "-destination",
        "platform=iOS Simulator,id=" + args.udid,
        "-parallel-testing-enabled",
        "NO",
        "-collect-test-diagnostics",
        "never",
        "-test-timeouts-enabled",
        "YES",
        "-default-test-execution-time-allowance",
        "3600",
        "-maximum-test-execution-time-allowance",
        "3600",
        "-derivedDataPath",
        str(root / "temp/build/SafariReference"),
        "-resultBundlePath",
        str(output / "probe.xcresult"),
        "-only-testing:SafariProbe/SafariProbe/testReferenceSwipes",
        "test",
    ]
    environment = dict(os.environ, TEST_RUNNER_DEVDUO_REFERENCE_URL=endpoint)
    process = None
    try:
        with (
            (output / "xctest.stdout.log").open("wb") as stdout,
            (output / "xctest.stderr.log").open("wb") as stderr,
        ):
            process = subprocess.Popen(command, env=environment, stdout=stdout, stderr=stderr)
            print(
                "Session starting. Enter ACTION ID (observe/down/up/stop); native receipts are saved unchanged.",
                flush=True,
            )
            for line in sys.stdin:
                parts = line.strip().split()
                if not parts:
                    continue
                if len(parts) != 2:
                    print("Expected ACTION ID", flush=True)
                    continue
                action, identifier = parts
                receipt = broker.request(action, identifier, process)
                print(
                    json.dumps(
                        {
                            "id": receipt["id"],
                            "action": receipt["action"],
                            "componentCount": len(receipt.get("components", [])),
                        }
                    ),
                    flush=True,
                )
                if action == "stop":
                    break
            try:
                code = process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                process.terminate()
                code = process.wait(timeout=15)
            if code:
                raise RuntimeError("XCTest failed; inspect the session logs")
    finally:
        if process and process.poll() is None:
            process.terminate()
            process.wait(timeout=15)
        broker.close()


if __name__ == "__main__":
    main()
