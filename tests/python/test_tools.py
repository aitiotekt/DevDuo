"""Portable regressions for the maintained command boundary and evidence checks."""

import concurrent.futures
import contextlib
import hashlib
import io
import json
import subprocess
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from unittest import mock

from devduo_tools import calibration_server, check_repository, cli, png_compare
from devduo_tools.paths import repository_root
from PIL import Image

ROOT = repository_root()


class ToolsTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="devduo tests ")
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)

    def command(self, *args):
        return subprocess.run(
            [sys.executable, "-m", "devduo_tools", *map(str, args)],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=30,
        )

    def image(self, name="input.png", color=(30, 60, 90, 255)):
        path = self.folder / name
        Image.new("RGBA", (4, 3), color).save(path)
        return path

    def test_every_command_has_help(self):
        for command in cli.COMMANDS:
            with self.subTest(command=command):
                result = self.command(command, "--help")
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("usage:", result.stdout)
        self.assertNotEqual(self.command("does-not-exist").returncode, 0)

    def test_png_roundtrip_palette_transparency_and_integrity(self):
        path = self.image()
        width, height, rgba = png_compare.read_png(path)
        self.assertEqual((width, height, len(rgba)), (4, 3, 48))
        png_compare.write_png(self.folder / "roundtrip.png", width, height, rgba)
        self.assertEqual(png_compare.read_png(self.folder / "roundtrip.png"), (width, height, rgba))
        palette = Image.new("P", (1, 1), 0)
        palette.putpalette([10, 20, 30] + [0] * 765)
        palette.save(self.folder / "palette.png", transparency=0)
        self.assertEqual(
            png_compare.read_png(self.folder / "palette.png")[2], bytearray([10, 20, 30, 0])
        )
        broken = bytearray(path.read_bytes())
        broken[29] ^= 1  # Corrupt the IHDR CRC, while leaving dimensions intact.
        (self.folder / "broken.png").write_bytes(broken)
        with self.assertRaises((OSError, ValueError, SyntaxError)):
            png_compare.read_png(self.folder / "broken.png")

    def test_diff_threshold_exclusion_dimensions_and_crop(self):
        image = png_compare.read_png(self.image())
        changed = bytearray(image[2])
        changed[0] += 10
        candidate = (4, 3, changed)
        report = png_compare.compare(image, candidate, self.folder / "diff")
        self.assertEqual(report["changedPixels"], 1)
        self.assertEqual(report["changedBoundingBoxInclusive"], [0, 0, 0, 0])
        self.assertEqual(
            png_compare.compare(image, candidate, self.folder / "excluded", exclude=[(0, 0, 1, 1)])[
                "changedPixels"
            ],
            0,
        )
        self.assertEqual(
            png_compare.compare(image, candidate, self.folder / "threshold", threshold=10)[
                "changedPixels"
            ],
            0,
        )
        with self.assertRaises(ValueError):
            png_compare.compare(image, (1, 1, changed[:4]), self.folder / "mismatch")
        with self.assertRaises(ValueError):
            png_compare.crop(image, (3, 2, 2, 2))
        with self.assertRaises(ValueError):
            png_compare.write_png(self.folder / "invalid.png", 4, 3, changed[:4])

    def test_cli_unicode_spaces_hashes_and_source_preservation(self):
        reference = self.image("reference space 中文.png")
        candidate = self.image("candidate space 中文.png", (250, 60, 90, 255))
        original = reference.read_bytes()
        output = self.folder / "comparison space 中文"
        result = self.command("png-compare", reference, candidate, "--output", output)
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads((output / "report.json").read_text())
        self.assertEqual(report["referenceSha256"], hashlib.sha256(original).hexdigest())
        self.assertEqual(report["changedPixels"], 12)
        result = self.command(
            "sample-appearance",
            reference,
            "--region",
            "patch:1,1,2,2",
            "--output",
            self.folder / "samples",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        sample = json.loads((self.folder / "samples/report.json").read_text())["samples"][0]
        self.assertEqual(sample["meanEncodedRgba"], [30, 60, 90, 255])
        self.assertEqual(reference.read_bytes(), original)

    def test_contact_sheet_explicit_pairs_and_no_overwrite(self):
        reference = self.image("reference.png")
        candidate = self.image("candidate.png")
        output = self.folder / "paired 中文.png"
        args = (
            "contact-sheet",
            "--pair",
            reference,
            candidate,
            "--label",
            "Measured pair",
            "--output",
            output,
        )
        result = self.command(*args)
        self.assertEqual(result.returncode, 0, result.stderr)
        with Image.open(output) as sheet:
            self.assertEqual(sheet.size, (1280, 550))
        self.assertEqual(len(json.loads(output.with_suffix(".json").read_text())["pairs"]), 1)
        self.assertNotEqual(self.command(*args).returncode, 0)
        with Image.open(reference) as image:
            self.assertEqual(image.size, (4, 3))

    def test_invalid_cli_inputs_fail_without_traceback(self):
        path = self.image("difference.png")
        for args in (
            ("png-compare", path, path, "--output", self.folder),
            ("png-compare", path, path, "--output", self.folder / "diff", "--threshold", 256),
            (
                "sample-appearance",
                path,
                "--region",
                "patch:4,0,1,1",
                "--output",
                self.folder / "samples",
            ),
            ("png-compare", self.folder / "missing.png", path, "--output", self.folder / "diff"),
        ):
            with self.subTest(args=args):
                result = self.command(*args)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn("Traceback", result.stderr)

    def test_open_safari_preserves_query_fragment_and_argument_boundaries(self):
        from devduo_tools.capture_macos import open_safari

        args = [
            "open-safari",
            "--device",
            "explicit simulator",
            "--pose",
            "open",
            "--state",
            "text 中文",
            "--url",
            "http://127.0.0.1:8765/?pose=old&keep=value#fragment",
        ]
        with (
            mock.patch.object(sys, "argv", args),
            mock.patch("subprocess.run") as run,
            contextlib.redirect_stdout(io.StringIO()),
        ):
            open_safari()
        call = run.call_args.args[0]
        self.assertIn("explicit simulator", call)
        self.assertIn("pose=open", call[-1])
        self.assertNotIn("pose=old", call[-1])
        self.assertIn("keep=value", call[-1])
        self.assertTrue(call[-1].endswith("#fragment"))

    def test_capture_broker_keeps_original_png_and_rejects_black_display(self):
        from devduo_tools.run_safari_probe import CaptureBroker

        original = self.image("broker-input.png").read_bytes()
        broker = CaptureBroker("explicit simulator", 3, "open-portrait", self.folder, {}, "session")
        self.addCleanup(broker.server.server_close)
        completed = subprocess.CompletedProcess([], 0, stdout=original)
        with mock.patch("devduo_tools.run_safari_probe.subprocess.run", return_value=completed):
            receipt = broker.capture("initial")
        self.assertEqual(receipt["outcome"], "valid")
        self.assertEqual((self.folder / receipt["image"]).read_bytes(), original)
        self.assertEqual(receipt["sha256"], hashlib.sha256(original).hexdigest())
        black = self.image("black.png", (0, 0, 0, 255)).read_bytes()
        with mock.patch(
            "devduo_tools.run_safari_probe.subprocess.run",
            return_value=subprocess.CompletedProcess([], 0, stdout=black),
        ):
            with self.assertRaisesRegex(RuntimeError, "black or transparent"):
                broker.capture("scrolled")
        receipts = [
            json.loads(line)
            for line in (self.folder / "capture-milestones.jsonl").read_text().splitlines()
        ]
        self.assertEqual([entry["outcome"] for entry in receipts], ["valid", "rejected"])
        self.assertFalse((self.folder / "open-portrait-scrolled.png").exists())

    def test_capture_broker_rejects_wrong_session_endpoint_and_stage_order(self):
        from devduo_tools.run_safari_probe import CaptureBroker

        broker = CaptureBroker("explicit simulator", 3, "open-portrait", self.folder, {}, "session")
        url = broker.start()
        self.addCleanup(broker.close)

        def request(endpoint, state):
            return urllib.request.urlopen(
                urllib.request.Request(
                    endpoint, data=json.dumps({"pose": "open-portrait", "state": state}).encode()
                )
            )

        with self.assertRaises(urllib.error.HTTPError) as error:
            request(url + "-invalid", "initial")
        self.assertEqual(error.exception.code, 404)
        with self.assertRaises(urllib.error.HTTPError) as error:
            request(url, "scrolled")
        self.assertEqual(error.exception.code, 409)
        with mock.patch.object(broker, "capture", return_value={"image": "test.png"}):
            self.assertTrue(json.load(request(url, "initial"))["ok"])
            with self.assertRaises(urllib.error.HTTPError) as error:
                request(url, "initial")
            self.assertEqual(error.exception.code, 409)
            self.assertTrue(json.load(request(url, "scrolled"))["ok"])

    def test_geometry_configuration_contains_screen_and_unknown_physical_units(self):
        profiles = json.loads((ROOT / "config/device-profiles.json").read_text())
        self.assertGreaterEqual(len(profiles), 2)
        for profile in profiles:
            self.assertLessEqual(
                profile["screenX"] + profile["screenCanvasWidth"], profile["shellCanvasWidth"]
            )
            self.assertLessEqual(
                profile["screenY"] + profile["screenCanvasHeight"], profile["shellCanvasHeight"]
            )
            self.assertTrue(profile["screenMaskPath"].startswith("M"))
            self.assertIsNone(profile["outerCornerRadiusDip"])
        scenes = json.loads((ROOT / "config/safari-scenes.json").read_text())
        self.assertEqual(len(scenes), 8)
        self.assertEqual(
            sum(len(scene["states"]) + len(scene.get("singleStates", {})) for scene in scenes), 24
        )

    def test_video_validates_frame_changes_without_a_source_hash_gate(self):
        source = self.folder / "encoded.mov"
        source.write_bytes(b"local encoded source test fixture")
        frames = []
        for index, color in enumerate(((10, 30, 50, 255), (240, 30, 50, 255))):
            image = self.image(f"frame-{index}.png", color)
            frames.append(
                {"image": image.name, "widthPx": 4, "heightPx": 3, "actualSeconds": index}
            )
        metadata = {
            "source": str(source),
            "durationSeconds": 2,
            "nominalFramesPerSecond": 30,
            "frames": frames,
        }
        (self.folder / "video-metadata.json").write_text(json.dumps(metadata))
        result = self.command("validate-video", self.folder)
        self.assertEqual(result.returncode, 0, result.stderr)
        source.write_bytes(b"changed source")
        result = self.command("validate-video", self.folder)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_gitignore_excludes_generated_files_but_preserves_sources_and_locks(self):
        subprocess.run(["git", "init", str(self.folder)], capture_output=True, check=True)
        (self.folder / ".gitignore").write_bytes((ROOT / ".gitignore").read_bytes())
        generated = [
            "temp/public-checkout/README.md",
            "captures/reference.png",
            "measurements/records.jsonl",
            "nested/temp/edit.py",
            "scripts/swift/.build/debug/description.json",
            "scripts/swift/.swiftpm/xcode/workspace.xcworkspace/contents.xcworkspacedata",
            ".ruff_cache/cache",
            "tests/.pytest_cache/cache",
            "src/DevDuo/obj/project.assets.json",
            "tests/DevDuo.Localization.Tests/bin/test.dll",
            "docsite/.vitepress/dist/index.html",
            "debug.log",
            "edit.py.tmp",
            "patch.rej",
            ".env.local",
        ]
        sources = [
            "src/DevDuo/App.cs",
            "scripts/python/devduo_tools/cli.py",
            "scripts/swift/Package.swift",
            "scripts/swift/Package.resolved",
            "scripts/archive/2026-10-21/extract_geometry.py",
            "tests/python/test_tools.py",
            "docs/images/example.png",
            "pnpm-lock.yaml",
            "uv.lock",
            "tests/DevDuo.Localization.Tests/packages.lock.json",
            ".env.example",
        ]
        for name in generated + sources:
            path = self.folder / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("Git ignore fixture\n")
        subprocess.run(["git", "-C", str(self.folder), "add", "."], check=True)
        staged = set(
            subprocess.check_output(["git", "-C", str(self.folder), "ls-files", "-z"])
            .decode()
            .split("\0")
        )
        self.assertFalse(staged.intersection(generated), "Generated files entered the index")
        self.assertFalse(set(sources).difference(staged), "Source or lockfiles were ignored")

    def test_public_audit_handles_worktree_removals_without_touching_index(self):
        subprocess.run(["git", "init", str(self.folder)], capture_output=True, check=True)
        required = [
            "README.md",
            "LICENSE",
            "THIRD_PARTY_NOTICES.md",
            "CONTRIBUTING.md",
            "AGENTS.md",
            "DevDuo.sln",
            "docs/en/009-WINDOWS-HANDOFF.md",
            "docs/en/100-ROADMAP.md",
            "removed.txt",
        ]
        for name in required:
            path = self.folder / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("review fixture\n")
        subprocess.run(["git", "-C", str(self.folder), "add", "."], check=True)
        (self.folder / "removed.txt").unlink()
        before = (self.folder / ".git/index").read_bytes()
        with (
            mock.patch.object(check_repository, "ROOT", self.folder),
            contextlib.redirect_stdout(io.StringIO()),
        ):
            self.assertEqual(check_repository.main([]), 0)
        self.assertEqual((self.folder / ".git/index").read_bytes(), before)


class ReceiverTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.server = calibration_server.Server(("127.0.0.1", 0), self.folder)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"
        self.addCleanup(self.stop)

    def stop(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)

    def test_concurrent_unicode_persistence_and_invalid_records(self):
        self.assertEqual(json.load(urllib.request.urlopen(self.url + "/health"))["status"], "ok")
        self.assertIn(b"viewport-fit=cover", urllib.request.urlopen(self.url + "/").read())
        with urllib.request.urlopen(self.url + "/document-end.html?fixed=narrow") as response:
            self.assertEqual(response.headers["Cache-Control"], "no-store")
            self.assertEqual(response.read(), (ROOT / "web/document-end.html").read_bytes())
        with urllib.request.urlopen(self.url + "/safari-reference.html?tab=A") as response:
            self.assertEqual(response.headers["Cache-Control"], "no-store")
            self.assertEqual(response.read(), (ROOT / "web/safari-reference.html").read_bytes())

        def post(index):
            body = json.dumps(
                {
                    "schemaVersion": 1,
                    "labels": {"pose": "中文", "index": index},
                    "unknown": {"physicalWidthMm": None},
                },
                ensure_ascii=False,
            ).encode()
            request = urllib.request.Request(self.url + "/api/measurements", data=body)
            return json.load(urllib.request.urlopen(request))["id"]

        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            ids = list(pool.map(post, range(8)))
        self.assertEqual(len(set(ids)), 8)
        records = [
            json.loads(line)
            for line in (self.folder / "records.jsonl").read_text(encoding="utf-8").splitlines()
        ]
        self.assertEqual(len(records), 8)
        self.assertTrue(all(record["labels"]["pose"] == "中文" for record in records))
        for record in records:
            self.assertEqual(
                json.loads((self.folder / (record["id"] + ".json")).read_text(encoding="utf-8")),
                record,
            )
        for body in (b"{}", b"{", b'{"schemaVersion":1,"labels":[]}', b"[]"):
            with self.assertRaises(urllib.error.HTTPError) as error:
                urllib.request.urlopen(
                    urllib.request.Request(self.url + "/api/measurements", data=body)
                )
            self.assertEqual(error.exception.code, 400)
        with self.assertRaises(urllib.error.HTTPError) as error:
            urllib.request.urlopen(self.url + "/../pyproject.toml")
        self.assertEqual(error.exception.code, 404)

    def test_receiver_rejects_non_loopback_bind(self):
        with self.assertRaises(ValueError):
            calibration_server.Server(("0.0.0.0", 0), self.folder)
