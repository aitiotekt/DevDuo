"""Presentation-only explicit image pairs; originals are never cropped or rewritten."""

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--pair",
        type=Path,
        nargs=2,
        action="append",
        required=True,
        metavar=("REFERENCE", "CANDIDATE"),
    )
    parser.add_argument("--label", action="append", help="One label per pair, in the same order")
    parser.add_argument("--font", type=Path, help="Optional TrueType/OpenType font")
    parser.add_argument(
        "--output", type=Path, required=True, help="New PNG path; existing files are refused"
    )
    args = parser.parse_args()
    if args.label and len(args.label) != len(args.pair):
        parser.error("Supply exactly one --label per --pair, or omit all labels")
    report_path = args.output.with_suffix(".json")
    if args.output.suffix.lower() != ".png":
        parser.error("--output must have the .png extension")
    if args.output.exists() or report_path.exists():
        parser.error(
            "Output PNG/JSON already exists; choose a new output to preserve prior evidence"
        )
    font = ImageFont.truetype(str(args.font), 20) if args.font else ImageFont.load_default(size=20)
    width, row_height = 1280, 500
    sheet = Image.new("RGB", (width, 50 + row_height * len(args.pair)), "#e8edf4")
    draw = ImageDraw.Draw(sheet)
    draw.text(
        (20, 12),
        "Reference | Candidate — resized for presentation; no fidelity score",
        fill="#182332",
        font=font,
    )
    entries = []
    for index, pair in enumerate(args.pair):
        y = 50 + row_height * index
        label = args.label[index] if args.label else f"Pair {index + 1}"
        draw.text((20, y), label, fill="#182332", font=font)
        sources = []
        for column, path in enumerate(pair):
            with Image.open(path) as original:
                original.load()
                image = original.convert("RGBA")
                size = list(image.size)
                image.thumbnail((600, 430), Image.Resampling.LANCZOS)
                x = 20 + column * 640 + (600 - image.width) // 2
                sheet.paste(image, (x, y + 40 + (430 - image.height) // 2), image)
            sources.append(
                {
                    "path": str(path.resolve()),
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "originalSizePixels": size,
                }
            )
        entries.append({"label": label, "sources": sources})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.output)
    report = {
        "schemaVersion": 1,
        "meaning": "Explicit image pairs resized for presentation. No automatic crop, alignment, photometric or geometry validation.",
        "pairs": entries,
    }
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"image": str(args.output), "pairs": len(entries)}))
