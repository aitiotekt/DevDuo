#!/usr/bin/env python3
"""Audit tracked and addable public files without staging or changing Git state."""

import argparse
import ast
import ipaddress
import json
import re
import subprocess
from pathlib import Path

from .icons import check_icons
from .paths import repository_root
from .reference_library import validate_library

ROOT = repository_root()
PRIVATE_ROOTS = {"captures", "measurements", "work", "temp", "tmp", ".local"}
MANAGED_LINKS = {
    entry["link"]: entry["target"]
    for entry in json.loads((ROOT / "scripts/docs-manifest.json").read_text(encoding="utf-8"))[
        "links"
    ]
}
PATTERNS = {
    "personal absolute path": re.compile(
        r"/(?:Users|Volumes|home)/[^\s\"'<>]+|[A-Za-z]:[\\/]+(?:Users|workspace|workspaces|projects|repos)[\\/]+[^\s\"'<>]+",
        re.IGNORECASE,
    ),
    "private workstation name": re.compile(
        r"\b(?:[A-Z]{2,12}-(?:[LDRS]\d+[A-Z]{2,6}|VM\d+[A-Z]{2,6})|(?-i:(?:DESKTOP|LAPTOP)-[A-Z0-9]{4,}))\b",
        re.IGNORECASE,
    ),
    "local network device label": re.compile(
        r"\b(?:ssh|scp|hostname|host_name|server_name|runner_name|ansible_host)\b[\s\"':=]+(?:[a-z]{1,3}\d+[a-z]{2,6})\b",
        re.IGNORECASE,
    ),
    "local network number": re.compile(r"\bVLAN[\s_:=#-]*(?:ID[\s_:=#-]*)?\d+\b", re.IGNORECASE),
    "device MAC address": re.compile(r"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b"),
    "private network share": re.compile(
        r"(?<!\\)\\{2}(?![?.]\\)[A-Za-z0-9][A-Za-z0-9_.-]*\\[A-Za-z0-9]"
    ),
    "internal network domain": re.compile(
        r"\b(?:[a-z0-9-]+\.)+(?:lan|home|internal|corp|intranet)\b"
    ),
    "private multicast-DNS host": re.compile(
        r"(?:https?://|\b(?:ssh|scp)\s+|\bhostname[\s\"':=]+)(?!devduo\.local(?=[:/\s\"']|$))(?:[a-z0-9-]+\.)+local\b",
        re.IGNORECASE,
    ),
    "literal simulator identifier": re.compile(
        r"(?:--(?:udid|device)\s+|\"(?:udid|deviceUdid|simulatorUDID|machineGuid|serialNumber)\"\s*:\s*\")[0-9A-Fa-f]{8}-[0-9A-Fa-f-]{27,}",
        re.IGNORECASE,
    ),
    "private key": re.compile(r"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----"),
    "legacy project identity": re.compile("Dev" + "Phone|DEV" + "PHONE|dev" + "phone"),
}
IPV4_CANDIDATE = re.compile(r"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])")
IPV6_CANDIDATE = re.compile(
    r"(?<![\w:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f:.]*(?:%[a-z0-9_.-]+)?(?![\w:.%\-])",
    re.IGNORECASE,
)
# Standard policy ranges are encoded as network integers, never as example machine addresses.
LOCAL_NETWORKS = tuple(
    ipaddress.ip_network(value)
    for value in (
        (0x0A000000, 8),
        (0xAC100000, 12),
        (0xC0A80000, 16),
        (0x64400000, 10),
        (0xA9FE0000, 16),
        (0xFC << 120, 7),
        (0xFE80 << 112, 10),
    )
)
EXAMPLE_NETWORKS = tuple(
    ipaddress.ip_network(value)
    for value in (
        "192.0.2.0/24",
        "198.51.100.0/24",
        "203.0.113.0/24",
        "2001:db8::/32",
    )
)


def privacy_findings(source, name):
    """Return locations/categories, never echo the sensitive matched values."""
    findings = []
    for label, pattern in PATTERNS.items():
        findings.extend(
            (source.count("\n", 0, match.start()) + 1, label) for match in pattern.finditer(source)
        )
    for pattern in (IPV4_CANDIDATE, IPV6_CANDIDATE):
        for match in pattern.finditer(source):
            try:
                # Match the complete token, not a hexadecimal prefix of CSS or
                # namespace identifiers. A sentence-ending period is punctuation.
                candidate = (
                    match.group().rstrip(".") if pattern is IPV6_CANDIDATE else match.group()
                )
                address = ipaddress.ip_address(candidate.split("%", 1)[0])
            except ValueError:
                continue
            if (
                address.is_loopback
                or address.is_unspecified
                or any(
                    address.version == network.version and address in network
                    for network in EXAMPLE_NETWORKS
                )
            ):
                continue
            local = any(
                address.version == network.version and address in network
                for network in LOCAL_NETWORKS
            )
            prefix = source[max(0, source.rfind("\n", 0, match.start())) : match.start()]
            # Numeric dependency versions are not host addresses. Private ranges remain forbidden everywhere.
            host_context = re.search(
                r"://[^\s/]*$|\b(?:host|hostname|ip|address)[\s\"':=\[]+$", prefix, re.IGNORECASE
            )
            if local or Path(name).suffix.lower() in {".md", ".html"} or host_context:
                findings.append(
                    (
                        source.count("\n", 0, match.start()) + 1,
                        "local network address" if local else "literal network host address",
                    )
                )
    return sorted(set(findings))


def index_findings():
    """Inspect staged text separately without refreshing or modifying the index."""
    staged = subprocess.check_output(["git", "-C", str(ROOT), "ls-files", "--stage", "-z"])
    entries = []
    failures = []
    for record in staged.decode("utf-8").split("\0"):
        if not record:
            continue
        details, name = record.split("\t", 1)
        mode, blob, stage = details.split()
        if stage != "0":
            failures.append(f"index:{name}: unresolved merge entry")
        else:
            entries.append((name, blob, mode))
    data = subprocess.check_output(
        ["git", "-C", str(ROOT), "cat-file", "--batch"],
        input="".join(f"{blob}\n" for _, blob, _ in entries).encode("ascii"),
    )
    position = 0
    for name, _, mode in entries:
        end = data.index(b"\n", position)
        _, kind, size = data[position:end].split()
        position = end + 1
        content = data[position : position + int(size)]
        position += int(size) + 1
        if kind != b"blob" or mode not in {"100644", "100755", "120000"}:
            failures.append(f"index:{name}: unsupported staged object")
            continue
        for _, label in privacy_findings(name, "path"):
            failures.append(f"index:{name}: sensitive filename ({label})")
        if Path(name).parts[0] in PRIVATE_ROOTS:
            failures.append(f"index:{name}: private evidence is staged for publication")
        try:
            source = content.decode("utf-8-sig")
        except UnicodeError:
            continue
        for line, label in privacy_findings(source, name):
            failures.append(f"index:{name}:{line}: {label}")
    return failures, len(entries)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--index",
        action="store_true",
        help="Also inspect the staged text snapshot without changing Git state",
    )
    args = parser.parse_args(argv)
    result = subprocess.run(
        ["git", "-C", str(ROOT), "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
        check=True,
        capture_output=True,
    )
    names = sorted(set(result.stdout.decode("utf-8").split("\0")) - {""})
    deleted = subprocess.run(
        ["git", "-C", str(ROOT), "ls-files", "--deleted", "-z"],
        check=True,
        capture_output=True,
    )
    removed = set(deleted.stdout.decode("utf-8").split("\0")) - {""}
    # A read-only worktree audit follows intended removals even in an initial staged tree.
    # The untouched index still requires review/staging by the repository owner.
    names = [name for name in names if name not in removed]
    failures = []
    reviewed_images = set()
    if (ROOT / "assets/icons").exists():
        try:
            reviewed_images.update(check_icons(ROOT))
        except (OSError, ValueError, KeyError) as error:
            failures.append(f"Application icon validation failed: {error}")
    library = ROOT / "docs/shared/safari-reference"
    if (library / "manifest.json").exists():
        try:
            references = validate_library(library)
            reviewed_images.update(
                {
                    (library / asset["file"]).relative_to(ROOT).as_posix()
                    for asset in references["assets"]
                }
            )
        except (OSError, ValueError, KeyError) as error:
            failures.append(f"Reference library validation failed: {error}")
    for name in names:
        path = ROOT / name
        if (
            path.parts[len(ROOT.parts)] in PRIVATE_ROOTS
            or any(
                part
                in {"bin", "obj", "__pycache__", "xcuserdata", "node_modules", ".venv", ".pnpm"}
                for part in Path(name).parts
            )
            or name.startswith(("docsite/.vitepress/cache/", "docsite/.vitepress/dist/"))
        ):
            failures.append(f"{name}: generated or private evidence is eligible for publication")
            continue
        if path.is_symlink():
            expected = MANAGED_LINKS.get(name)
            if (
                expected is None
                or not path.exists()
                or path.resolve() != (ROOT / expected).resolve()
                or Path(path.readlink()).is_absolute()
            ):
                failures.append(f"{name}: unapproved, absolute or broken symlink")
                continue
            if path.is_dir():
                continue
        if not path.is_file():
            failures.append(f"{name}: tracked file is missing")
            continue
        try:
            source = path.read_text(encoding="utf-8-sig")
        except UnicodeError:
            if name not in reviewed_images:
                failures.append(f"{name}: binary requires explicit publication review")
            continue
        for line, label in privacy_findings(source, name):
            failures.append(f"{name}:{line}: {label}")
        for _, label in privacy_findings(name, "path"):
            failures.append(f"{name}: sensitive filename ({label})")
        try:
            if path.suffix == ".py":
                ast.parse(source, filename=name)
            elif path.suffix == ".json":
                json.loads(source)
        except (SyntaxError, ValueError) as error:
            failures.append(f"{name}: invalid {path.suffix}: {error}")
        if path.suffix == ".md":
            for target in re.findall(r"\[[^\]]*\]\(([^)]+)\)", source):
                if "://" in target or target.startswith("#"):
                    continue
                target = target.split("#", 1)[0]
                if target and not (path.resolve().parent / target).exists():
                    failures.append(f"{name}: broken local link {target}")
    for required in (
        "README.md",
        "LICENSE",
        "THIRD_PARTY_NOTICES.md",
        "CONTRIBUTING.md",
        "AGENTS.md",
        "DevDuo.sln",
        "docs/en/009-WINDOWS-HANDOFF.md",
        "docs/en/100-ROADMAP.md",
    ):
        if required not in names:
            failures.append(f"{required}: required public file missing")
    if args.index:
        staged_failures, staged_count = index_findings()
        failures.extend(staged_failures)
    if failures:
        print("FAIL: repository publication audit")
        print("\n".join(failures))
        return 1
    print(
        f"PASS: {len(names)} public files; no targeted private-data patterns, legacy identity or broken documentation links"
    )
    if removed:
        print(f"Worktree audit excluded {len(removed)} removed paths; Git index was not changed.")
    if args.index:
        print(
            f"PASS: {staged_count} staged objects inspected for text privacy; Git index was not changed."
        )
    print(
        "Raw captures and generated files remain local. Review the diff for other sensitive data before publishing."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
