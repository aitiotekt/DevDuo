"""Application icon publication integrity boundaries."""

import json
import shutil
import tempfile
import unittest
from pathlib import Path

from devduo_tools.icons import check_icons
from devduo_tools.paths import repository_root


class IconIntegrityTest(unittest.TestCase):
    def test_checked_in_resources_and_stale_or_extra_entries(self):
        root = repository_root()
        self.assertEqual(len(check_icons(root)), 5)
        with tempfile.TemporaryDirectory(prefix="devduo-icons-") as temporary:
            candidate = Path(temporary)
            shutil.copytree(root / "assets/icons", candidate / "assets/icons")
            shutil.copyfile(root / "devduo-metadata.toml", candidate / "devduo-metadata.toml")
            artwork = candidate / "assets/icons/AppIcon.icon/Assets/artwork.png"
            original = artwork.read_bytes()
            artwork.write_bytes(original + b"changed")
            with self.assertRaisesRegex(ValueError, "Stale or changed"):
                check_icons(candidate)
            artwork.write_bytes(original)
            manifest_file = candidate / "assets/icons/manifest.json"
            manifest = json.loads(manifest_file.read_text())
            manifest["files"]["../../unexpected"] = "0" * 64
            manifest_file.write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError, "Invalid icon provenance"):
                check_icons(candidate)
