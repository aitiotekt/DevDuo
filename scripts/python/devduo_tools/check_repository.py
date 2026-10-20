#!/usr/bin/env python3
"""Audit tracked and addable public files without staging or changing Git state."""

import argparse
import ast
import json
import re
import subprocess
from pathlib import Path

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
        r"/(?:Users|Volumes|home)/[^\s\"'<>]+|[A-Za-z]:[\\/]+Users[\\/]+[^\s\"'<>]+"
    ),
    "private workstation name": re.compile(r"\bENFW-[A-Z0-9-]+\b"),
    "literal simulator identifier": re.compile(
        r"(?:--(?:udid|device)\s+|\"(?:udid|deviceUdid|simulatorUDID)\"\s*:\s*\")[0-9A-Fa-f]{8}-[0-9A-Fa-f-]{27,}"
    ),
    "private key": re.compile(r"-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----"),
    "legacy project identity": re.compile("Dev" + "Phone|DEV" + "PHONE|dev" + "phone"),
}


def main(argv=None):
    argparse.ArgumentParser(description=__doc__).parse_args(argv)
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
    library = ROOT / "docs/shared/safari-reference"
    if (library / "manifest.json").exists():
        try:
            references = validate_library(library)
            reviewed_images = {
                (library / asset["file"]).relative_to(ROOT).as_posix()
                for asset in references["assets"]
            }
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
        for label, pattern in PATTERNS.items():
            match = pattern.search(source)
            if match:
                line = source.count("\n", 0, match.start()) + 1
                failures.append(f"{name}:{line}: {label}")
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
    if failures:
        print("FAIL: repository publication audit")
        print("\n".join(failures))
        return 1
    print(
        f"PASS: {len(names)} public files; no targeted private-data patterns, legacy identity or broken documentation links"
    )
    if removed:
        print(f"Worktree audit excluded {len(removed)} removed paths; Git index was not changed.")
    print(
        "Raw captures and generated files remain local. Review the diff for other sensitive data before publishing."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
