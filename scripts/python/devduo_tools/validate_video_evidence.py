#!/usr/bin/env python3
"""Validate original video hash and decoded frames from devduo-inspect-video."""

import argparse
import hashlib
import json
import pathlib

from .png_compare import read_png


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("frames", type=pathlib.Path)
    p.add_argument(
        "--source-sha256", help="Expected original hash for legacy metadata without sourceSha256"
    )
    a = p.parse_args()
    meta = json.loads((a.frames / "video-metadata.json").read_text())
    source = pathlib.Path(meta["source"])
    if not source.is_file():
        p.error("Original video is unavailable: " + str(source))
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    expected_hash = a.source_sha256 or meta.get("sourceSha256")
    if not expected_hash:
        p.error("Expected original video hash missing; supply --source-sha256 for legacy metadata")
    if source_hash != expected_hash:
        p.error("Original video hash differs from the expected source hash")
    if len(meta["frames"]) < 2:
        p.error("At least two decoded frames are required")
    if meta["durationSeconds"] <= 0:
        p.error("Video duration must be positive")
    rows = []
    images = []
    for f in meta["frames"]:
        path = a.frames / f["image"]
        if path.resolve().parent != a.frames.resolve():
            p.error("Frame images must be direct children of the metadata directory")
        if f.get("sha256") and hashlib.sha256(path.read_bytes()).hexdigest() != f["sha256"]:
            p.error("Decoded frame source hash differs: " + str(path))
        w, h, pixels = read_png(path)
        if images and (w, h) != images[0][:2]:
            p.error("Decoded frame sizes differ; comparison requires a consistent canvas")
        if (w, h) != (f["widthPx"], f["heightPx"]):
            p.error("Decoded frame dimensions disagree: " + str(path))
        nonblack = sum(
            1 for i in range(0, len(pixels), 4) if pixels[i + 3] > 0 and max(pixels[i : i + 3]) > 8
        )
        if nonblack < w * h * 0.01:
            p.error("Black or transparent decoded video frame: " + str(path))
        rows.append(
            {
                **f,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "rgbaSha256": hashlib.sha256(pixels).hexdigest(),
                "nonblackPixelCount": nonblack,
            }
        )
        images.append((w, h, pixels))
    changed = None
    if len(images) >= 2 and images[0][:2] == images[-1][:2]:
        first, last = images[0][2], images[-1][2]
        changed = sum(
            1
            for i in range(0, len(first), 4)
            if any(abs(first[i + c] - last[i + c]) > 8 for c in range(4))
        )
    if not changed:
        p.error("First and last decoded frames do not demonstrate a changing image")
    report = {
        "schemaVersion": 1,
        "source": str(source),
        "videoSha256": source_hash,
        "durationSeconds": meta["durationSeconds"],
        "encodedNominalFramesPerSecond": meta["nominalFramesPerSecond"],
        "frames": rows,
        "firstLastChangedPixelsAtThreshold8": changed,
        "valid": True,
        "limits": [
            "Requested and actual decoded frame times may differ; repeated actual frames are retained.",
            "Demonstrates decodable, nonblack, changing images only; does not identify Liquid Glass constants or compositor refresh rate.",
            "Video encoding may use even pixel dimensions; use lossless PNG for strict geometry.",
        ],
    }
    (a.frames / "video-validation.json").write_text(json.dumps(report, indent=2) + "\n")
    print(
        json.dumps(
            {
                "source": str(source),
                "valid": True,
                "decodedFrames": len(rows),
                "firstLastChangedPixels": changed,
            }
        )
    )


if __name__ == "__main__":
    main()
