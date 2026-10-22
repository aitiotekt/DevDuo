"""Export desktop icon formats and compile the Apple Icon Composer catalog."""

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tomllib
from pathlib import Path

from .paths import repository_root

SOURCE = "AppIcon.icon/Assets/artwork.png"
INPUTS = (SOURCE, "AppIcon.icon/icon.json")
OUTPUTS = ("app-icon.png", "app.ico", "AppIcon.icns", "Assets.car")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def export_icons(folder: Path) -> None:
    from PIL import Image, ImageDraw

    with Image.open(folder / SOURCE) as source:
        if source.size != (1024, 1024):
            raise ValueError("Icon artwork must be 1024 x 1024.")
        artwork = source.convert("RGBA")
    # Apple applies the modern mask itself. Legacy exports need their own safe zone.
    mask = Image.new("L", artwork.size)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, 1023, 1023), radius=220, fill=255)
    tile = artwork.copy()
    tile.putalpha(mask)
    tile.resize((256, 256), Image.Resampling.LANCZOS).save(folder / "app-icon.png")
    tile.save(folder / "app.ico", sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
    legacy = Image.new("RGBA", (1024, 1024))
    legacy.alpha_composite(tile.resize((824, 824), Image.Resampling.LANCZOS), (100, 100))
    legacy.save(folder / "AppIcon.icns", format="ICNS")


def compile_catalog(root: Path, folder: Path) -> None:
    if sys.platform != "darwin":
        raise ValueError("Icon Composer compilation requires macOS and full Xcode.")
    metadata = tomllib.loads((root / "devduo-metadata.toml").read_text(encoding="utf-8"))
    output = root / "temp/build/icons"
    output.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        [
            "xcrun",
            "actool",
            str(folder / "AppIcon.icon"),
            "--compile",
            str(output),
            "--platform",
            "macosx",
            "--minimum-deployment-target",
            metadata["macos"]["minimum_system_version"],
            "--target-device",
            "mac",
            "--app-icon",
            "AppIcon",
            "--output-partial-info-plist",
            str(output / "icon-info.plist"),
            "--output-format",
            "human-readable-text",
        ],
        check=True,
    )
    shutil.copyfile(output / "Assets.car", folder / "Assets.car")
    compiler = subprocess.check_output(["xcodebuild", "-version"], text=True).strip()
    manifest = {
        "schemaVersion": 1,
        "compiler": compiler,
        "minimumSystemVersion": metadata["macos"]["minimum_system_version"],
        "files": {name: digest(folder / name) for name in (*INPUTS, *OUTPUTS)},
    }
    (folder / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def check_icons(root: Path) -> set[str]:
    folder = root / "assets/icons"
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    metadata = tomllib.loads((root / "devduo-metadata.toml").read_text(encoding="utf-8"))
    if (
        manifest.get("schemaVersion") != 1
        or not manifest.get("compiler", "").startswith("Xcode ")
        or manifest.get("minimumSystemVersion") != metadata["macos"]["minimum_system_version"]
        or set(manifest["files"]) != set((*INPUTS, *OUTPUTS))
    ):
        raise ValueError("Invalid icon provenance; run sync-icons on macOS.")
    for name in manifest["files"]:
        if (folder / name).is_symlink():
            raise ValueError(f"Icon resource cannot be a symbolic link: {name}")
    if (folder / "AppIcon.icns").read_bytes()[:4] != b"icns":
        raise ValueError("Invalid legacy ICNS.")
    if (folder / "app.ico").read_bytes()[:4] != b"\0\0\1\0":
        raise ValueError("Invalid Windows ICO.")
    if (folder / "Assets.car").read_bytes()[:8] != b"BOMStore":
        raise ValueError("Invalid compiled Apple asset catalog.")
    return {f"assets/icons/{name}" for name in (SOURCE, *OUTPUTS)}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("export", "compile", "check"))
    args = parser.parse_args(argv)
    root = repository_root()
    folder = root / "assets/icons"
    try:
        if args.operation == "export":
            export_icons(folder)
        elif args.operation == "compile":
            compile_catalog(root, folder)
        else:
            check_icons(root)
        print(f"PASS: icons {args.operation}")
        return 0
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1
