#!/usr/bin/env python3
"""Record rendered RGBA statistics of explicitly chosen PNG pixel regions (locked Pillow decoder)."""

import argparse
import hashlib
import json
import pathlib
import re
import statistics

from .png_compare import crop, read_png, write_png


def region(value):
    try:
        name, raw = value.split(":", 1)
        values = tuple(int(v) for v in raw.split(","))
        if (
            not re.fullmatch(r"[A-Za-z0-9_-]+", name)
            or len(values) != 4
            or min(values) < 0
            or min(values[2:]) == 0
        ):
            raise ValueError()
        return name, values
    except ValueError:
        raise argparse.ArgumentTypeError("Expected name:x,y,width,height in actual image pixels")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("image", type=pathlib.Path)
    p.add_argument("--region", action="append", type=region, required=True)
    p.add_argument("--output", type=pathlib.Path, required=True)
    a = p.parse_args()
    if len({name for name, _ in a.region}) != len(a.region):
        p.error("Region names must be unique")
    outputs = [a.output / (name + ".png") for name, _ in a.region] + [a.output / "report.json"]
    if any(a.image.resolve() == path.resolve() for path in outputs):
        p.error("Sample output would overwrite the source image; choose another directory")
    image = read_png(a.image)
    a.output.mkdir(parents=True, exist_ok=True)
    samples = []
    for name, rect in a.region:
        try:
            w, h, pixels = crop(image, rect)
        except ValueError as e:
            p.error(str(e))
        file = a.output / (name + ".png")
        write_png(file, w, h, pixels)
        channels = [list(pixels[c::4]) for c in range(4)]
        samples.append(
            {
                "name": name,
                "rectangleImagePixels": list(rect),
                "pixelCount": w * h,
                "meanEncodedRgba": [statistics.mean(c) for c in channels],
                "populationStdDevEncodedRgba": [statistics.pstdev(c) for c in channels],
                "minEncodedRgba": [min(c) for c in channels],
                "maxEncodedRgba": [max(c) for c in channels],
                "crop": file.name,
            }
        )
    report = {
        "schemaVersion": 1,
        "source": str(a.image.resolve()),
        "sourceSha256": hashlib.sha256(a.image.read_bytes()).hexdigest(),
        "imagePixels": {"width": image[0], "height": image[1]},
        "samples": samples,
        "meaning": "Rendered composite pixels only; explicit operator-selected rectangles, not material identification.",
        "limits": [
            "PNG encoded RGBA values, no ICC color management or linear-light conversion.",
            "Does not distinguish page tint, backdrop, text, reflection, optical boundary, blur, refraction, opacity or source light.",
            "Same region coordinates across states do not automatically refer to the same moving component.",
            "No pixel-to-mm or hardware radius inference.",
        ],
    }
    (a.output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({"source": str(a.image), "samples": len(samples), "output": str(a.output)}))


if __name__ == "__main__":
    main()
