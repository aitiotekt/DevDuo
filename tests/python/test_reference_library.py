"""Public reference integrity and native gesture capability boundaries."""

import contextlib
import io
import json
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from unittest.mock import Mock, patch

from devduo_tools.reference_library import MATRIX, export_library, sha256, validate_library
from devduo_tools.reference_session import SwipeBroker
from devduo_tools.reference_session import main as reference_session_main
from PIL import Image


class ReferenceLibraryTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="devduo-reference-test-")
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        image = self.folder / "synthetic.webp"
        Image.new("RGB", (4, 3), "teal").save(image, "WEBP")
        self.manifest = {
            "schemaVersion": 1,
            "reviewedForPublication": True,
            "assets": [{"file": image.name, "sha256": sha256(image), "sizePixels": [4, 3]}],
            "cases": [
                {
                    "fold": fold,
                    "bodyAngleClockwise": angle,
                    "appearance": theme,
                    "tabCount": tabs,
                    "chrome": chrome,
                    "asset": image.name,
                    "sourceCropPixels": [0, 0, 4, 3],
                    "sourceSizePixels": [4, 3],
                    "sourceSha256": "a" * 64,
                    "pageColors": {
                        "colorScheme": theme,
                        "pageBackground": "teal",
                        "themeColor": None,
                    },
                }
                for fold, angle, theme, tabs, chrome in sorted(MATRIX)
            ],
        }

    def write_manifest(self):
        (self.folder / "manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def test_public_derivatives_validate_without_private_sources(self):
        self.write_manifest()
        self.assertEqual(len(validate_library(self.folder)["cases"]), 48)

    def test_missing_duplicate_and_unreviewed_combinations_fail(self):
        original = self.manifest["cases"].copy()
        for cases in [original[:-1], [*original[:-1], original[0]]]:
            self.manifest["cases"] = cases
            self.write_manifest()
            with self.assertRaisesRegex(ValueError, "48 distinct"):
                validate_library(self.folder)
        self.manifest["cases"] = original
        self.manifest["reviewedForPublication"] = False
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "publication review"):
            validate_library(self.folder)

    def test_dark_page_cannot_be_mislabeled_as_system_dark(self):
        dark = next(case for case in self.manifest["cases"] if case["appearance"] == "dark")
        dark["pageColors"]["colorScheme"] = "light"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "separate axis"):
            validate_library(self.folder)

    def test_unsafe_paths_and_extra_images_fail(self):
        target = self.folder / "synthetic.webp"
        self.manifest["assets"][0]["file"] = "../synthetic.webp"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            validate_library(self.folder)
        self.manifest["assets"][0]["file"] = target.name
        self.write_manifest()
        (self.folder / "unreviewed.png").write_bytes(b"unreviewed source")
        with self.assertRaisesRegex(ValueError, "reviewed manifest"):
            validate_library(self.folder)

    def test_metadata_and_out_of_bounds_crop_fail(self):
        target = self.folder / "synthetic.webp"
        exif = Image.Exif()
        exif[0x010E] = "synthetic test metadata"
        Image.new("RGB", (4, 3), "teal").save(target, "WEBP", exif=exif)
        self.manifest["assets"][0]["sha256"] = sha256(target)
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "metadata"):
            validate_library(self.folder)
        Image.new("RGB", (4, 3), "teal").save(target, "WEBP")
        self.manifest["assets"][0]["sha256"] = sha256(target)
        self.manifest["cases"][0]["sourceCropPixels"] = [0, 0, 5, 3]
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "source crop"):
            validate_library(self.folder)

    def export_session(self):
        source = self.folder / "source.png"
        exif = Image.Exif()
        exif[0x010E] = "synthetic metadata"
        Image.new("RGB", (4, 3), "teal").save(source, "PNG", exif=exif)
        native = {
            "id": "capture",
            "action": "observe",
            "components": [
                {"identifier": "WebView", "frame": {"x": 0, "y": 0, "width": 4, "height": 3}},
                {
                    "identifier": "TabBarTab?isActive=true&UUID=synthetic",
                    "label": "private tab title",
                    "frame": {"x": 0, "y": 0, "width": 2, "height": 1},
                },
            ],
        }
        (self.folder / "native").mkdir()
        (self.folder / "native/capture.json").write_text(json.dumps(native))
        (self.folder / "orientation.json").write_text(
            json.dumps({"result": {"deviceOrientation": "portrait"}})
        )
        cases = []
        layouts, crops = {}, {}
        for index, combination in enumerate(sorted(MATRIX)):
            fold, angle, theme, tabs, chrome = combination
            name = f"synthetic-{index:03}"
            cases.append(
                {
                    "id": name,
                    "fold": fold,
                    "bodyAngleClockwise": angle,
                    "appearance": theme,
                    "tabCount": tabs,
                    "chrome": chrome,
                    "sourceImage": source.name,
                    "sourceSha256": sha256(source),
                    "nativeSession": "native",
                    "nativeId": "capture",
                    "native": native,
                    "orientationSource": "orientation.json",
                    "capturedAt": "synthetic",
                    "fixtureSourceSha256": "b" * 64,
                    "browser": {
                        "viewport": {"innerWidth": 4, "innerHeight": 3, "scrollY": 0},
                        "environment": {"pageBackground": "teal", "colorScheme": theme},
                    },
                }
            )
            crops[name] = [0, 0, 4, 3]
            layouts[name] = {"contentLayout": "synthetic", "nativeSceneToCanvasClockwise": 0}
        (self.folder / "cases.json").write_text(json.dumps(cases))
        (self.folder / "publication-review.json").write_text(
            json.dumps(
                {
                    "reviewedForPublication": True,
                    "cohort": "Synthetic format test",
                    "themeAxis": "Synthetic test only",
                    "crops": crops,
                    "layouts": layouts,
                    "limits": ["Synthetic test only"],
                    "triggers": {},
                    "fixtureSources": {"synthetic": "b" * 64},
                }
            )
        )
        return cases

    def test_export_keeps_original_bytes_strips_metadata_and_private_native_labels(self):
        self.export_session()
        source = self.folder / "source.png"
        original = source.read_bytes()
        output = self.folder / "export"
        export_library(self.folder, output)
        self.assertEqual(source.read_bytes(), original)
        manifest = validate_library(output)
        self.assertEqual(len(manifest["assets"]), 52)
        self.assertNotIn("UUID", json.dumps(manifest))
        self.assertNotIn("private tab title", json.dumps(manifest))
        self.assertTrue(manifest["cases"][0]["nativeComponents"][0]["selected"])

    def test_export_rejects_path_traversal_before_creating_output(self):
        cases = self.export_session()
        cases[0]["sourceImage"] = "../outside.png"
        (self.folder / "cases.json").write_text(json.dumps(cases))
        output = self.folder / "export"
        with self.assertRaisesRegex(ValueError, "local filenames"):
            export_library(self.folder, output)
        self.assertFalse(output.exists())


class ReferenceSessionArgumentsTest(unittest.TestCase):
    def test_invalid_gesture_delays_fail_before_output_or_native_launch(self):
        with tempfile.TemporaryDirectory(prefix="devduo-reference-arguments-") as folder:
            output = Path(folder) / "native"
            for delay in ("-1", "11", "0.5"):
                with (
                    self.subTest(delay=delay),
                    patch.object(
                        sys,
                        "argv",
                        [
                            "reference-session",
                            "--udid",
                            "synthetic",
                            "--output",
                            str(output),
                            "--gesture-delay",
                            delay,
                        ],
                    ),
                    patch("devduo_tools.reference_session.subprocess.Popen") as launch,
                    contextlib.redirect_stderr(io.StringIO()),
                    self.assertRaises(SystemExit) as failure,
                ):
                    reference_session_main()
                self.assertEqual(failure.exception.code, 2)
                self.assertFalse(output.exists())
                launch.assert_not_called()


class SwipeBrokerTest(unittest.TestCase):
    def test_capability_rejects_stale_duplicate_and_arbitrary_commands(self):
        with tempfile.TemporaryDirectory(prefix="devduo-swipe-test-") as folder:
            broker = SwipeBroker(Path(folder))
            endpoint = broker.start()
            self.addCleanup(broker.close)
            process = Mock()
            process.poll.return_value = None
            with self.assertRaises(ValueError):
                broker.request("shell-command", "safe-id", process)
            with self.assertRaises(ValueError):
                broker.request("observe", "../unsafe", process)
            with self.assertRaises(urllib.error.HTTPError) as unknown:
                urllib.request.urlopen(endpoint + "-wrong")
            self.assertEqual(unknown.exception.code, 404)

            def post(value):
                return urllib.request.urlopen(
                    urllib.request.Request(
                        endpoint,
                        data=json.dumps(value).encode(),
                        headers={"Content-Type": "application/json"},
                        method="POST",
                    )
                )

            result = []
            worker = threading.Thread(
                target=lambda: result.append(broker.request("down", "case-one", process))
            )
            worker.start()
            deadline = time.monotonic() + 3
            while json.load(urllib.request.urlopen(endpoint))["id"] != "case-one":
                self.assertLess(time.monotonic(), deadline)
                time.sleep(0.01)
            with self.assertRaises(urllib.error.HTTPError) as stale:
                post({"id": "old", "action": "down"})
            self.assertEqual(stale.exception.code, 409)
            receipt = {"id": "case-one", "action": "down", "components": []}
            self.assertTrue(json.load(post(receipt))["ok"])
            worker.join(timeout=3)
            self.assertEqual(result, [receipt])
            self.assertEqual(json.loads((Path(folder) / "case-one.json").read_text()), receipt)
            with self.assertRaises(urllib.error.HTTPError) as duplicate:
                post(receipt)
            self.assertEqual(duplicate.exception.code, 409)
            with self.assertRaises(ValueError):
                broker.request("down", "case-one", process)


if __name__ == "__main__":
    unittest.main()
