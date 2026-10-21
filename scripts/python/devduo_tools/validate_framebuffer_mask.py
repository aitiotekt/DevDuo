#!/usr/bin/env python3
"""Compare simulator screenshot alpha with a locally installed black-interior PDF mask."""

import argparse
import hashlib
import json
import pathlib
import subprocess

from .png_compare import read_png, write_png


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("framebuffer", type=pathlib.Path)
    p.add_argument("--mask-pdf", type=pathlib.Path, required=True)
    p.add_argument("--scale", type=float, required=True)
    p.add_argument("--output", type=pathlib.Path, required=True)
    a = p.parse_args()
    if a.scale <= 0:
        p.error("scale must be positive; use the independently verified display scale")
    for target in (
        a.output / "reference-mask.png",
        a.output / "difference.png",
        a.output / "report.json",
    ):
        if any(source.resolve() == target.resolve() for source in (a.framebuffer, a.mask_pdf)):
            p.error("Output would overwrite source evidence; choose another directory")
    a.output.mkdir(parents=True, exist_ok=True)
    prefix = a.output / "reference-mask"
    subprocess.run(
        ["pdftoppm", "-png", "-r", str(72 * a.scale), "-singlefile", str(a.mask_pdf), str(prefix)],
        check=True,
        timeout=120,
    )
    w, h, pixels = read_png(a.framebuffer)
    mw, mh, mask = read_png(prefix.with_suffix(".png"))
    if (w, h) != (mw, mh):
        p.error(
            f"Canvas mismatch {w}x{h} vs {mw}x{mh}; this tool never rotates or resizes. Capture the matching portrait display."
        )
    intersection = union = different = 0
    difference = bytearray()
    for i in range(0, len(pixels), 4):
        actual = pixels[i + 3] >= 128
        expected = (mask[i] + mask[i + 1] + mask[i + 2]) < 384
        intersection += actual and expected
        union += actual or expected
        different += actual != expected
        difference.extend((255, 0, 255, 255) if actual != expected else (0, 0, 0, 255))
    report = {
        "schemaVersion": 1,
        "widthPx": w,
        "heightPx": h,
        "sourceMaskPdf": str(a.mask_pdf.resolve()),
        "framebuffer": str(a.framebuffer.resolve()),
        "framebufferSha256": hashlib.sha256(a.framebuffer.read_bytes()).hexdigest(),
        "maskPdfSha256": hashlib.sha256(a.mask_pdf.read_bytes()).hexdigest(),
        "scale": a.scale,
        "binaryThreshold": 128,
        "binaryMaskDifferentPixels": different,
        "maskIntersectionOverUnion": intersection / union if union else None,
        "method": "Compare framebuffer alpha to black PDF interior at explicit 72*scale DPI. No registration, rotation, rescaling, or physical conversion.",
        "meaning": "Tests installed simulator mask against its framebuffer only. Does not certify hardware radii or desktop-host fidelity.",
    }
    (a.output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    write_png(a.output / "difference.png", w, h, difference)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
