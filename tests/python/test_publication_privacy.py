"""Privacy regressions use constructed synthetic identities, never real device data."""

import contextlib
import io
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from devduo_tools import check_repository


def ipv4(*octets):
    return ".".join(map(str, octets))


class PublicationPrivacyTest(unittest.TestCase):
    def test_css_pseudo_elements_and_namespace_tokens_are_not_addresses(self):
        samples = [
            "getComputedStyle(modal, '::backdrop')",
            "dialog::backdrop { background: black; }",
            "::before { content: ''; }",
            "::after { content: ''; }",
            "namespace::backdrop",
            "::faceplate",
            "::beaded_widget",
            "::dead-beef-label",
            "::face.example",
            "std::filesystem::path",
        ]
        for sample in samples:
            with self.subTest(sample=sample):
                self.assertEqual(check_repository.privacy_findings(sample, "fixture.html"), [])

    def test_complete_ipv6_tokens_remain_rejected_with_zones_and_punctuation(self):
        addresses = [
            ":".join(["fd12", "3456", "", "2"]),
            ":".join(["fd12", "3456", "0", "0", "0", "0", "0", "2"]),
            ":".join(["fe80", "", "1234"]) + "%fixture-zone.2",
            ":".join(["", "", "ffff", ipv4(10, 2, 3, 4)]),
        ]
        for address in addresses:
            for source in (
                address,
                f"({address}),",
                f"'{address}';",
                f"<{address}>!",
                f"Endpoint {address}.",
                f"http://[{address}]:8765/",
            ):
                with self.subTest(form=source):
                    candidates = list(check_repository.IPV6_CANDIDATE.finditer(source))
                    self.assertEqual(len(candidates), 1)
                    self.assertEqual(candidates[0].group().rstrip("."), address)
                    findings = check_repository.privacy_findings(source, "guide.md")
                    self.assertTrue(findings)
                    self.assertNotIn(address, str(findings))

    def test_network_addresses_are_rejected_without_echoing_values(self):
        addresses = [
            ipv4(10, 2, 3, 4),
            ipv4(172, 20, 3, 4),
            ipv4(192, 168, 4, 7),
            ipv4(100, 80, 2, 3),
            ipv4(169, 254, 3, 4),
            ":".join(["fd12", "3456", "", "2"]),
            ":".join(["fe80", "", "1234"]) + "%fixture",
            ipv4(43, 12, 34, 56),
        ]
        for address in addresses:
            with self.subTest(address_type="ipv6" if ":" in address else "ipv4"):
                findings = check_repository.privacy_findings(
                    f"Endpoint: http://[{address}]/", "guide.md"
                )
                self.assertTrue(findings)
                self.assertNotIn(address, str(findings))

    def test_device_labels_domains_paths_mac_and_identifiers_are_rejected(self):
        samples = [
            "-".join(["LAB", "VM9NODE"]),
            "-".join(["DESKTOP", "A1B2C3D"]),
            "ssh " + "vm" + "9node",
            "VLAN " + str(37),
            ":".join(["aa", "12", "bc", "34", "de", "56"]),
            "https://" + "storage" + ".internal/",
            "hostname: " + "printer" + ".local",
            "/".join(["", "Users", "fixture-person", "project"]),
            "\\".join(["C:", "workspace", "fixture-project", "source"]),
            "\\" * 2 + "fileserver" + "\\" + "private",
            '"deviceUdid": "'
            + "-".join(["12345678", "abcd", "1234", "abcd", "123456789abc"])
            + '"',
        ]
        for sample in samples:
            with self.subTest(category="synthetic identity"):
                findings = check_repository.privacy_findings("heading\n" + sample, "guide.md")
                self.assertTrue(findings)
                self.assertEqual(findings[0][0], 2)
                self.assertNotIn(sample, str(findings))

    def test_loopback_reserved_examples_versions_and_project_fixtures_remain_valid(self):
        allowed = [
            "http://127.0.0.1:8765/",
            "http://localhost:3000",
            "http://[::1]/",
            "0.0.0.0",
            "http://203.0.113.20/",
            "http://[2001:db8::1]/",
            "http://devduo.local/",
            "host: example.test",
            '"resolved": "8.3.1.3"',
            "SIMULATOR_UDID",
            "mise.local.toml",
            "github-runner",
            "D3D11",
            "xcode-27",
            "desktop-validation",
            "System.Windows.Forms.Keys.Home",
        ]
        for sample in allowed:
            with self.subTest(sample=sample):
                self.assertEqual(check_repository.privacy_findings(sample, "example.lock.json"), [])

    def test_staged_snapshot_is_checked_without_refreshing_or_changing_index(self):
        with tempfile.TemporaryDirectory(prefix="devduo-privacy-") as directory:
            root = Path(directory)
            subprocess.run(["git", "init", str(root)], check=True, capture_output=True)
            path = root / "README.md"
            address = ipv4(192, 168, 9, 17)
            path.write_text("Private endpoint: " + address, encoding="utf-8")
            subprocess.run(["git", "-C", str(root), "add", "README.md"], check=True)
            path.write_text("Sanitized worktree\n", encoding="utf-8")
            before = (root / ".git/index").read_bytes()
            with mock.patch.object(check_repository, "ROOT", root):
                failures, count = check_repository.index_findings()
            self.assertEqual(count, 1)
            self.assertTrue(failures)
            self.assertNotIn(address, "\n".join(failures))
            self.assertEqual((root / ".git/index").read_bytes(), before)

    def test_cli_failure_reports_locations_without_sensitive_content(self):
        with tempfile.TemporaryDirectory(prefix="devduo-privacy-") as directory:
            root = Path(directory)
            subprocess.run(["git", "init", str(root)], check=True, capture_output=True)
            address = ipv4(10, 12, 34, 56)
            (root / "README.md").write_text("Endpoint " + address, encoding="utf-8")
            output = io.StringIO()
            with (
                mock.patch.object(check_repository, "ROOT", root),
                contextlib.redirect_stdout(output),
            ):
                self.assertEqual(check_repository.main([]), 1)
            self.assertIn("README.md:1: local network address", output.getvalue())
            self.assertNotIn(address, output.getvalue())


if __name__ == "__main__":
    unittest.main()
