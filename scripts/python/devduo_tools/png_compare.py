#!/usr/bin/env python3
"""Exact-size RGBA PNG comparison, without silent alignment or resizing."""

import argparse
import hashlib
import json
import pathlib
import zlib

from PIL import Image


def read_png(path):
    """Decode PNG to RGBA without color management or resizing."""
    with Image.open(path) as image:
        if image.format != "PNG":
            raise ValueError("Expected PNG")
        if image.mode.startswith("I") or image.mode == "F":
            raise ValueError(
                "Only 8-bit/color or palette PNG comparison is supported; convert higher-depth inputs explicitly"
            )
        image.verify()
    with Image.open(path) as image:
        rgba = image.convert("RGBA")
        return rgba.width, rgba.height, bytearray(rgba.tobytes())


def write_png(path, w, h, rgba):
    if w <= 0 or h <= 0 or len(rgba) != w * h * 4:
        raise ValueError("Positive dimensions and exactly width*height*4 RGBA bytes required")
    Image.frombytes("RGBA", (w, h), bytes(rgba)).save(path, format="PNG")


def rect(value):
    result = tuple(map(int, value.split(",")))
    if len(result) != 4 or min(result) < 0 or result[2] == 0 or result[3] == 0:
        raise argparse.ArgumentTypeError("Expected x,y,width,height with positive size")
    return result


def crop(image, region):
    if region is None:
        return image
    w, h, pixels = image
    x, y, cw, ch = region
    if min(x, y) < 0 or min(cw, ch) <= 0 or x + cw > w or y + ch > h:
        raise ValueError("Crop outside image")
    return (
        cw,
        ch,
        bytearray(
            b"".join(
                bytes(pixels[((y + dy) * w + x) * 4 : ((y + dy) * w + x + cw) * 4])
                for dy in range(ch)
            )
        ),
    )


def compare(reference, candidate, output, threshold=8, exclude=()):
    if not 0 <= threshold <= 255:
        raise ValueError("threshold must be 0..255")
    for width, height, pixels in (reference, candidate):
        if width <= 0 or height <= 0 or len(pixels) != width * height * 4:
            raise ValueError("Invalid RGBA image dimensions or buffer length")
    w, h, a = reference
    w2, h2, b = candidate
    if (w, h) != (w2, h2):
        raise ValueError(
            f"Size mismatch {w}x{h} vs {w2}x{h2}; crop corresponding regions explicitly; no implicit scale or registration"
        )
    output = pathlib.Path(output)
    output.mkdir(parents=True, exist_ok=True)
    diff = bytearray()
    overlay = bytearray()
    total = changed = absolute = 0
    bounds = [w, h, -1, -1]
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 4
            skip = any(rx <= x < rx + rw and ry <= y < ry + rh for rx, ry, rw, rh in exclude)
            d = [abs(a[i + c] - b[i + c]) for c in range(4)]
            if not skip:
                total += 1
                absolute += sum(d)
                if max(d) > threshold:
                    changed += 1
                    bounds = [
                        min(bounds[0], x),
                        min(bounds[1], y),
                        max(bounds[2], x),
                        max(bounds[3], y),
                    ]
            diff.extend(
                (0, 0, 0, 255)
                if skip
                else (min(255, d[0] * 4 + d[3]), min(255, d[1] * 4), min(255, d[2] * 4 + d[3]), 255)
            )
            overlay.extend((a[i + c] + b[i + c]) // 2 for c in range(4))
    report = {
        "schemaVersion": 1,
        "width": w,
        "height": h,
        "comparedPixels": total,
        "changedPixels": changed,
        "changedPixelRatio": changed / total if total else None,
        "meanAbsoluteRgbaDifference": absolute / (total * 4) if total else None,
        "thresholdPerChannel": threshold,
        "changedBoundingBoxInclusive": bounds if changed else None,
        "excludedRects": exclude,
        "method": "unaligned RGBA pixels; difference amplified 4x; embedded ICC profiles not color-managed; animation/text rasterization/chrome state require matched captures",
    }
    write_png(output / "difference.png", w, h, diff)
    write_png(output / "overlay.png", w, h, overlay)
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("reference", type=pathlib.Path)
    p.add_argument("candidate", type=pathlib.Path)
    p.add_argument("--output", type=pathlib.Path, required=True)
    p.add_argument("--reference-crop", type=rect)
    p.add_argument("--candidate-crop", type=rect)
    p.add_argument("--exclude", type=rect, action="append", default=[])
    p.add_argument("--threshold", type=int, default=8)
    args = p.parse_args()
    if not 0 <= args.threshold <= 255:
        p.error("threshold must be 0..255")
    outputs = {args.output / name for name in ("difference.png", "overlay.png", "report.json")}
    if any(
        source.resolve() == target.resolve()
        for source in (args.reference, args.candidate)
        for target in outputs
    ):
        p.error("Comparison output would overwrite an original input; choose another directory")
    try:
        report = compare(
            crop(read_png(args.reference), args.reference_crop),
            crop(read_png(args.candidate), args.candidate_crop),
            args.output,
            args.threshold,
            args.exclude,
        )
    except (ValueError, OSError, zlib.error) as e:
        p.error(str(e))
    report["reference"] = str(args.reference.resolve())
    report["candidate"] = str(args.candidate.resolve())
    report["referenceSha256"] = hashlib.sha256(args.reference.read_bytes()).hexdigest()
    report["candidateSha256"] = hashlib.sha256(args.candidate.read_bytes()).hexdigest()
    report["referenceCrop"] = args.reference_crop
    report["candidateCrop"] = args.candidate_crop
    (args.output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
