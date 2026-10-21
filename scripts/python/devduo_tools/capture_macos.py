#!/usr/bin/env python3
"""Capture an explicit simulator display; manual labels never assert measured state."""

import argparse
import datetime
import hashlib
import json
import pathlib
import shutil
import subprocess
import tempfile

from .paths import repository_root
from .png_compare import read_png

ROOT = repository_root()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--device", required=True, help="simulator UDID")
    p.add_argument("--display", type=int, choices=[1, 3], required=True)
    p.add_argument("--pose", choices=["closed", "partially-open", "open"], required=True)
    p.add_argument("--rotation", type=int, choices=[0, 90, 180, 270], required=True)
    p.add_argument("--state", required=True)
    p.add_argument("--record-id")
    p.add_argument(
        "--record-file",
        type=pathlib.Path,
        help="Optional actual browser JSON, verified against --record-id when both supplied",
    )
    p.add_argument("--notes", default="")
    p.add_argument("--output", type=pathlib.Path, default=ROOT / "captures")
    a = p.parse_args()
    if not a.state or any(character in a.state for character in "/\\") or a.state in {".", ".."}:
        p.error("--state must be a filename label without path separators")
    record = None
    if a.record_file:
        record = json.loads(a.record_file.read_text())
        if not isinstance(record, dict):
            p.error("--record-file must contain a saved browser measurement object")
        if a.record_id and record.get("id") != a.record_id:
            p.error("--record-file ID does not match --record-id")
        if not record.get("id") or not isinstance(record.get("viewport"), dict):
            p.error("--record-file must be an actual saved browser measurement")
        a.record_id = record["id"]
    a.output.mkdir(parents=True, exist_ok=True)
    now = datetime.datetime.now(datetime.UTC)
    stamp = now.strftime("%Y%m%dT%H%M%S.%fZ")
    name = f"{a.pose}-r{a.rotation}-d{a.display}-{a.state}-{stamp}"
    png = a.output / (name + ".png")
    # CoreSimulator may lack permission to write an external-volume repository.
    # Capture to an isolated simulator-accessible temp directory, then copy original bytes.
    with tempfile.TemporaryDirectory(prefix="devduo-manual-") as folder:
        temporary = pathlib.Path(folder) / "framebuffer.png"
        cmd = [
            "xcrun",
            "simctl",
            "io",
            a.device,
            "screenshot",
            f"--display={a.display}",
            "--mask=alpha",
            str(temporary),
        ]
        result = subprocess.run(cmd, check=True, capture_output=True, timeout=60)
        captured = datetime.datetime.now(datetime.UTC).isoformat()
        w, h, pixels = read_png(temporary)
        nonblack = sum(
            1 for i in range(0, len(pixels), 4) if pixels[i + 3] > 0 and max(pixels[i : i + 3]) > 2
        )
        if not nonblack:
            raise SystemExit("Rejected entirely black/transparent selected-display screenshot")
        shutil.copyfile(temporary, png)
    meta = {
        "schemaVersion": 1,
        "capturedAt": captured,
        "source": "CoreSimulator explicit display raw PNG; original bytes copied unchanged",
        "device": a.device,
        "display": a.display,
        "pose": a.pose,
        "rotation": a.rotation,
        "state": a.state,
        "labelsStatus": "Operator-supplied; set actual Hub fold/rotation separately and confirm image/telemetry. This tool does not change pose.",
        "measurementRecordId": a.record_id,
        "recordAssociationStatus": "record-file ID verified; sample and screenshot remain sequential"
        if record
        else "operator-supplied association, unverified"
        if a.record_id
        else "no browser record associated",
        "browserViewport": record.get("viewport") if record else None,
        "recordFileSha256": hashlib.sha256(a.record_file.read_bytes()).hexdigest()
        if record
        else None,
        "notes": a.notes,
        "command": cmd,
        "simctlStdout": result.stdout.decode(errors="replace"),
        "simctlStderr": result.stderr.decode(errors="replace"),
        "png": png.name,
        "sha256": hashlib.sha256(png.read_bytes()).hexdigest(),
        "widthPx": w,
        "heightPx": h,
        "nonblackPixelCount": nonblack,
        "physicalWidthMm": None,
        "physicalHeightMm": None,
        "hardwareCornerRadiusMm": None,
    }
    (a.output / (name + ".json")).write_text(json.dumps(meta, indent=2) + "\n")
    print(
        json.dumps({"image": str(png), "widthPx": w, "heightPx": h, "nonblackPixelCount": nonblack})
    )


if __name__ == "__main__":
    main()


def open_safari():
    """Open an operator-labeled calibration URL through simctl."""
    import urllib.parse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--device", required=True, help="Simulator UDID")
    parser.add_argument("--pose", choices=["closed", "partially-open", "open"], required=True)
    parser.add_argument("--state", default="initial")
    parser.add_argument("--url", default="http://127.0.0.1:8765/")
    args = parser.parse_args()
    parsed = urllib.parse.urlsplit(args.url)
    if parsed.scheme not in ("http", "https") or not parsed.hostname:
        parser.error("--url must be an absolute HTTP(S) URL")
    query = urllib.parse.parse_qsl(parsed.query, keep_blank_values=True)
    labels = {"device": "iPhone Duo", "pose": args.pose, "browser": "Safari", "state": args.state}
    query = [(key, value) for key, value in query if key not in labels] + list(labels.items())
    url = urllib.parse.urlunsplit(parsed._replace(query=urllib.parse.urlencode(query)))
    subprocess.run(["xcrun", "simctl", "openurl", args.device, url], check=True, timeout=30)
    print(url)
