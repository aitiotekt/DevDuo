#!/usr/bin/env python3
"""Capture the actual current Device Hub pose via native Safari XCTest. macOS only."""

import argparse
import datetime
import hashlib
import hmac
import importlib.metadata
import json
import os
import pathlib
import platform
import secrets
import shutil
import signal
import subprocess
import tempfile
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from .paths import repository_root
from .png_compare import read_png

CAPTURE_METHOD = "simctl-explicit-display-at-xctest-milestone"


def utcnow():
    return datetime.datetime.now(datetime.UTC).isoformat()


class CaptureBroker:
    def __init__(self, udid, display, pose, output, source_hashes, capture_session_id):
        self.capture_session_id = capture_session_id
        self.udid = udid
        self.display = display
        self.pose = pose
        self.output = output
        self.source_hashes = source_hashes
        self.path = "/capture/" + secrets.token_urlsafe(32)
        self.completed = {}
        self.lock = threading.Lock()
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format, *args):
                with (owner.output / "capture-broker-http.log").open("a") as stream:
                    stream.write(utcnow() + " " + format % args + "\n")

            def reply(self, status, payload):
                data = json.dumps(payload).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                try:
                    self.wfile.write(data)
                except (BrokenPipeError, ConnectionResetError):
                    pass

            def do_POST(self):
                if not hmac.compare_digest(self.path, owner.path):
                    return self.reply(404, {"ok": False, "error": "not found"})
                try:
                    length = int(self.headers.get("Content-Length", "0"))
                    if not 0 < length <= 4096:
                        return self.reply(413, {"ok": False, "error": "invalid body size"})
                    body = json.loads(self.rfile.read(length))
                    if (
                        not isinstance(body, dict)
                        or set(body) != {"pose", "state"}
                        or body["pose"] != owner.pose
                        or body["state"] not in ("initial", "scrolled")
                    ):
                        return self.reply(
                            400,
                            {
                                "ok": False,
                                "error": "expected fixed pose and initial/scrolled state only",
                            },
                        )
                except (ValueError, UnicodeDecodeError):
                    return self.reply(400, {"ok": False, "error": "invalid request"})
                with owner.lock:
                    state = body["state"]
                    if state in owner.completed:
                        return self.reply(409, {"ok": False, "error": "stage already captured"})
                    if state == "scrolled" and "initial" not in owner.completed:
                        return self.reply(
                            409, {"ok": False, "error": "initial stage required first"}
                        )
                    try:
                        record = owner.capture(state)
                    except Exception as error:
                        return self.reply(500, {"ok": False, "error": str(error)})
                    owner.completed[state] = record
                    self.reply(200, {"ok": True, **record})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.daemon_threads = True
        self.thread = threading.Thread(
            target=self.server.serve_forever, name="devduo-capture-broker", daemon=True
        )

    def start(self):
        self.thread.start()
        return f"http://127.0.0.1:{self.server.server_port}" + self.path

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)

    def capture(self, state):
        name = self.pose + "-" + state
        target = self.output / (name + ".png")
        if target.exists():
            raise RuntimeError("Refusing to overwrite existing stage screenshot: " + target.name)
        temporary_directory = tempfile.TemporaryDirectory(prefix="devduo-capture-")
        temporary = pathlib.Path(temporary_directory.name) / (name + ".png")
        record = {
            "schemaVersion": 1,
            "pose": self.pose,
            "state": state,
            "displayId": self.display,
            "browserCaptureSessionId": self.capture_session_id,
            "requestedAt": utcnow(),
            "captureMethod": CAPTURE_METHOD,
            "snapshotSource": "CoreSimulator explicit display PNG with alpha mask",
            "image": target.name,
            "deviceUdid": self.udid,
            "sourceSha256": self.source_hashes,
            "outcome": "pending",
        }
        command = [
            "xcrun",
            "simctl",
            "io",
            self.udid,
            "screenshot",
            "--display=" + str(self.display),
            "--mask=alpha",
            "-",
        ]
        record["command"] = command
        record["captureStartedAt"] = utcnow()
        try:
            with (self.output / (name + "-simctl.stderr.log")).open("wb") as stderr:
                result = subprocess.run(
                    command,
                    cwd=temporary_directory.name,
                    stdout=subprocess.PIPE,
                    stderr=stderr,
                    timeout=55,
                )
            raw_png = result.stdout
            transport = "simctl raw PNG stdout; Python writes unchanged bytes"
            literal_dash = pathlib.Path(temporary_directory.name) / "-"
            if not raw_png and literal_dash.is_file():
                raw_png = literal_dash.read_bytes()
                transport = "simctl wrote literal dash file in this milestone isolated temp directory despite documented stdout syntax; original PNG bytes copied unchanged"
                record["documentedStdoutFallback"] = True
            temporary.write_bytes(raw_png)
            record["commandWorkingDirectory"] = temporary_directory.name
            (self.output / (name + "-simctl.stdout.log")).write_text(
                json.dumps(
                    {
                        "encoding": "raw PNG bytes from simctl stdout, no image conversion",
                        "stdoutByteLength": len(result.stdout),
                        "pngByteLength": len(raw_png),
                        "pngSha256": hashlib.sha256(raw_png).hexdigest(),
                        "transport": transport,
                        "canonicalImage": target.name,
                    }
                )
                + "\n"
            )
            record["simctlExitCode"] = result.returncode
            record["capturedAt"] = utcnow()
            record["stdoutByteLength"] = len(result.stdout)
            record["transport"] = transport
            if result.returncode:
                raise RuntimeError("simctl screenshot failed; see stage stderr log")
            if not temporary.is_file() or not temporary.stat().st_size:
                raise RuntimeError("simctl did not produce a nonempty screenshot")
            width, height, pixels = read_png(temporary)
            record["widthPx"] = width
            record["heightPx"] = height
            record["sha256"] = hashlib.sha256(temporary.read_bytes()).hexdigest()
            nonblack = sum(
                1
                for r, g, b, alpha in zip(pixels[0::4], pixels[1::4], pixels[2::4], pixels[3::4])
                if alpha > 0 and (r > 2 or g > 2 or b > 2)
            )
            record["nonblackPixelCount"] = nonblack
            record["nonblackThresholdPerRgbChannel"] = 2
            record["nonblackPixelRatio"] = nonblack / (width * height)
            if not nonblack:
                invalid = self.output / (name + "-rejected-black.png")
                shutil.copyfile(temporary, invalid)
                record["image"] = invalid.name
                raise RuntimeError(
                    "Selected display screenshot is entirely black or transparent; rejected"
                )
            shutil.copyfile(temporary, target)
            record["outcome"] = "valid"
            record["validatedAt"] = utcnow()
            return record
        except Exception as error:
            record["outcome"] = "rejected"
            record["error"] = str(error)
            record.setdefault("capturedAt", utcnow())
            raise
        finally:
            with (self.output / "capture-milestones.jsonl").open("a") as stream:
                stream.write(json.dumps(record, ensure_ascii=False) + "\n")
            temporary_directory.cleanup()


def main():
    import urllib.parse

    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--udid", required=True)
    p.add_argument("--mode", required=True, choices=["closed", "partial", "open"])
    p.add_argument(
        "--orientation",
        required=True,
        choices=["portrait", "portraitUpsideDown", "landscapeLeft", "landscapeRight"],
    )
    p.add_argument("--output", type=pathlib.Path)
    p.add_argument("--video", action="store_true")
    p.add_argument("--tab-mode", choices=["multi", "single"], default="multi")
    p.add_argument(
        "--reuse-current-url",
        help="Do not open a new tab. Require an already loaded loopback calibration URL with matching pose and captureSession.",
    )
    a = p.parse_args()
    root = repository_root()
    project = root / "scripts/DeviceHubUITests/SafariProbe.xcodeproj"
    if not project.exists():
        raise SystemExit("SafariProbe.xcodeproj missing: " + str(project))
    pose = a.mode + ("-single" if a.tab_mode == "single" else "") + "-" + a.orientation
    display = 1 if a.mode == "closed" else 3
    capture_session_id = str(uuid.uuid4())
    url = (
        "http://127.0.0.1:8765/?device=iPhone%20Duo&pose="
        + pose
        + "&browser=Safari&state=initial&displayMode="
        + a.mode
        + "&run=stable&captureSession="
        + capture_session_id
    )
    if a.reuse_current_url:
        parsed = urllib.parse.urlparse(a.reuse_current_url)
        query = urllib.parse.parse_qs(parsed.query)
        if (
            parsed.scheme != "http"
            or parsed.hostname != "127.0.0.1"
            or parsed.port != 8765
            or query.get("pose") != [pose]
            or not query.get("captureSession")
        ):
            p.error(
                "Current URL must be loopback calibration with matching pose and captureSession"
            )
        url = a.reuse_current_url
        capture_session_id = query["captureSession"][0]
    stamp = datetime.datetime.now(datetime.UTC).strftime("%Y%m%dT%H%M%S.%fZ")
    out = (a.output or root / "captures/safari" / pose / stamp).resolve()
    if out.exists():
        p.error("Capture output already exists; choose a new directory to preserve source evidence")
    out.mkdir(parents=True, exist_ok=False)

    def orientation(name):
        f = out / name
        subprocess.run(
            [
                "xcrun",
                "devicectl",
                "device",
                "orientation",
                "get",
                "--device",
                a.udid,
                "--json-output",
                str(f),
            ],
            check=True,
            stdout=subprocess.DEVNULL,
        )
        return json.loads(f.read_text())["result"]["deviceOrientation"]

    before = orientation("orientation-before.json")
    if before != a.orientation:
        raise SystemExit(
            f"Actual orientation is {before}, expected {a.orientation}. Use Device Hub Rotate Right/Left and retry; command success alone is insufficient."
        )
    meta = {
        "schemaVersion": 1,
        "pose": pose,
        "browserCaptureSessionId": capture_session_id,
        "browserUrl": url,
        "foldMode": a.mode,
        "foldModeEvidence": "Operator selection; verify Hub screenshot. No supported CLI fold API identified.",
        "orientationBefore": before,
        "startedAt": utcnow(),
        "deviceUdid": a.udid,
        "displayId": display,
        "captureMethod": CAPTURE_METHOD,
        "snapshotSource": "Explicit selected-display simctl raw PNG stdout at each synchronous XCTest evidence milestone; Python writes original bytes, default XCUI screenshot is diagnostic only",
        "webAssetSha256": {
            x.name: hashlib.sha256(x.read_bytes()).hexdigest()
            for x in (root / "web").iterdir()
            if x.is_file()
        },
        "units": "Native UI frame: XCTest screen points. Browser telemetry: CSS px. Screenshot: image pixels. No physical mm inferred.",
        "sampling": "Native component JSON -> synchronous broker explicit-display PNG -> default XCTest diagnostic screenshot. Sequential, not atomic; browser POST is independent. Correlate timestamps.",
    }
    tooling_sources = [
        *sorted(pathlib.Path(__file__).parent.glob("*.py")),
        root / "pyproject.toml",
        root / "uv.lock",
    ]
    meta["toolingSourceSha256"] = {
        str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in tooling_sources
    }
    meta["pythonVersion"] = platform.python_version()
    meta["pillowVersion"] = importlib.metadata.version("Pillow")
    source_snapshot = out / "tooling-source"
    source_snapshot.mkdir(exist_ok=False)
    for path in tooling_sources:
        target = source_snapshot / path.relative_to(root)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, target)
    meta["tabMode"] = a.tab_mode
    meta["reuseCurrentTab"] = bool(a.reuse_current_url)
    meta["probeSourceSha256"] = hashlib.sha256(
        (project.parent / "SafariProbe.swift").read_bytes()
    ).hexdigest()
    meta["runnerSourceSha256"] = hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest()
    shutil.copyfile(project.parent / "SafariProbe.swift", out / "probe-source.swift")
    shutil.copyfile(pathlib.Path(__file__), out / "runner-source.py")

    def save_session():
        (out / "session.json").write_text(json.dumps(meta, indent=2) + "\n")

    save_session()
    broker = CaptureBroker(
        a.udid,
        display,
        pose,
        out,
        {
            "probe": meta["probeSourceSha256"],
            "runner": meta["runnerSourceSha256"],
            "web": meta["webAssetSha256"],
        },
        capture_session_id,
    )
    capture_url = broker.start()
    env = dict(
        os.environ,
        TEST_RUNNER_DEVDUO_POSE=pose,
        TEST_RUNNER_DEVDUO_CAPTURE_URL=capture_url,
        TEST_RUNNER_DEVDUO_SESSION_ID=capture_session_id,
    )
    video = None
    video_stdout = None
    video_stderr = None
    video_temporary_directory = None
    video_path = None
    failure = None
    try:
        if not a.reuse_current_url:
            subprocess.run(["xcrun", "simctl", "openurl", a.udid, url], check=True)
        if a.video:
            video_stdout = (out / "video.stdout.log").open("wb")
            video_stderr = (out / "video.stderr.log").open("wb")
            video_temporary_directory = tempfile.TemporaryDirectory(prefix="devduo-video-")
            video_path = pathlib.Path(video_temporary_directory.name) / "transition.mov"
            meta["videoTemporarySource"] = str(video_path)
            meta["videoCopyMethod"] = (
                "encoded original bytes copied after recording stops; no transcoding"
            )
            video = subprocess.Popen(
                [
                    "xcrun",
                    "simctl",
                    "io",
                    a.udid,
                    "recordVideo",
                    "--display=" + str(display),
                    "--codec=h264",
                    str(video_path),
                ],
                stdout=video_stdout,
                stderr=video_stderr,
            )
            deadline = time.monotonic() + 10
            recording_started = False
            while time.monotonic() < deadline:
                if (
                    b"Recording started"
                    in (out / "video.stderr.log").read_bytes()
                    + (out / "video.stdout.log").read_bytes()
                ):
                    recording_started = True
                    break
                if video.poll() is not None:
                    raise RuntimeError("recordVideo failed; inspect video stdout/stderr logs")
                time.sleep(0.1)
            if not recording_started:
                raise RuntimeError("recordVideo start was not confirmed before timeout")
        cmd = [
            "xcodebuild",
            "-project",
            str(project),
            "-scheme",
            "SafariProbe",
            "-destination",
            "platform=iOS Simulator,id=" + a.udid,
            "-parallel-testing-enabled",
            "NO",
            "-collect-test-diagnostics",
            "never",
            "-test-timeouts-enabled",
            "YES",
            "-default-test-execution-time-allowance",
            "180",
            "-maximum-test-execution-time-allowance",
            "180",
            "-derivedDataPath",
            str(root / "temp/build/SafariProbe"),
            "-resultBundlePath",
            str(out / "probe.xcresult"),
            "-only-testing:SafariProbe/SafariProbe/testCurrentPose",
            "test",
        ]
        with (
            (out / "xctest.stdout.log").open("wb") as stdout,
            (out / "xctest.stderr.log").open("wb") as stderr,
        ):
            result = subprocess.run(cmd, env=env, stdout=stdout, stderr=stderr)
        meta["testExitCode"] = result.returncode
    except Exception as error:
        failure = error
        meta["runError"] = str(error)
    finally:
        if video and video.poll() is None:
            video.send_signal(signal.SIGINT)
            try:
                video.wait(timeout=30)
            except subprocess.TimeoutExpired:
                video.terminate()
                video.wait(timeout=10)
        if video_path is not None:
            if video_path.is_file() and video_path.stat().st_size:
                shutil.copyfile(video_path, out / "transition.mov")
                meta["videoSha256"] = hashlib.sha256(
                    (out / "transition.mov").read_bytes()
                ).hexdigest()
            else:
                meta["videoError"] = "No nonempty encoded video produced"
                failure = failure or RuntimeError(meta["videoError"])
        if video_temporary_directory:
            video_temporary_directory.cleanup()
        for log in (video_stdout, video_stderr):
            if log:
                log.close()
        broker.close()
        meta["finishedAt"] = utcnow()
        meta["captureMilestones"] = list(broker.completed.values())
        meta["captureStagesValid"] = set(broker.completed) == {"initial", "scrolled"}
        try:
            meta["orientationAfter"] = orientation("orientation-after.json")
        except Exception as error:
            meta["orientationAfterError"] = str(error)
            failure = failure or error
        save_session()
    if (out / "probe.xcresult").exists():
        with tempfile.TemporaryDirectory(prefix="devduo-after-") as folder:
            after = pathlib.Path(folder) / "framebuffer-after.png"
            with (
                (out / "framebuffer-after.stdout.log").open("wb") as stdout,
                (out / "framebuffer-after.stderr.log").open("wb") as stderr,
            ):
                result = subprocess.run(
                    [
                        "xcrun",
                        "simctl",
                        "io",
                        a.udid,
                        "screenshot",
                        "--display=" + str(display),
                        "--mask=alpha",
                        "-",
                    ],
                    cwd=folder,
                    check=True,
                    stdout=subprocess.PIPE,
                    stderr=stderr,
                )
                stdout.write(
                    (
                        json.dumps(
                            {
                                "encoding": "raw PNG stdout saved unchanged",
                                "byteLength": len(result.stdout),
                                "sha256": hashlib.sha256(result.stdout).hexdigest(),
                            }
                        )
                        + "\n"
                    ).encode()
                )
            raw = result.stdout or (pathlib.Path(folder) / "-").read_bytes()
            after.write_bytes(raw)
            shutil.copyfile(after, out / "framebuffer-after.png")
        attachments = out / "attachments"
        subprocess.run(
            [
                "xcrun",
                "xcresulttool",
                "export",
                "attachments",
                "--path",
                str(out / "probe.xcresult"),
                "--output-path",
                str(attachments),
            ],
            check=True,
            stdout=subprocess.DEVNULL,
        )
        manifest = json.loads((attachments / "manifest.json").read_text())
        for test in manifest:
            for entry in test["attachments"]:
                src = attachments / entry["exportedFileName"]
                base = entry["suggestedHumanReadableName"].split("_0_")[0] + src.suffix
                if src.exists():
                    # Never allow an exported default-screen attachment to overwrite broker evidence.
                    target = out / base
                    if target.exists():
                        raise RuntimeError(
                            "Attachment would overwrite existing evidence: " + target.name
                        )
                    shutil.copyfile(src, target)
    print(
        json.dumps(
            {
                "output": str(out),
                "pose": pose,
                "testExitCode": meta.get("testExitCode"),
                "orientationAfter": meta.get("orientationAfter"),
                "captureMethod": CAPTURE_METHOD,
                "captureStagesValid": meta["captureStagesValid"],
            },
            ensure_ascii=False,
        ),
        flush=True,
    )
    if failure:
        raise SystemExit(str(failure))
    if (
        meta.get("testExitCode", 1)
        or meta.get("orientationAfter") != a.orientation
        or not meta["captureStagesValid"]
    ):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
