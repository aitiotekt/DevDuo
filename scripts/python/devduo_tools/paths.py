"""Locate a source checkout without depending on the installed package location."""

from pathlib import Path


def repository_root(start: Path | None = None) -> Path:
    candidates = [start or Path.cwd(), Path(__file__).resolve().parent]
    for candidate in candidates:
        for path in (candidate, *candidate.parents):
            if (path / "pyproject.toml").is_file() and (path / "web/calibration.js").is_file():
                return path
    raise FileNotFoundError("Run this command in a DevDuo source checkout.")
