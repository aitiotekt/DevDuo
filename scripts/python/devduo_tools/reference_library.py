"""Export and validate explicitly reviewed Device Hub canvas reference derivatives."""

import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import parse_qs

from PIL import Image, ImageDraw, ImageFont

MATRIX = {
    (fold, angle, theme, tabs, chrome)
    for fold in ("closed", "open")
    for angle in (0, 90, 180, 270)
    for theme in ("light", "dark")
    for tabs in ((2,) if fold == "closed" else (1, 2))
    for chrome in ("expanded", "compact")
}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def case_key(case):
    return (
        case["fold"],
        case["bodyAngleClockwise"],
        case["appearance"],
        case["tabCount"],
        case["chrome"],
    )


def validate_library(folder):
    """A public checkout validates derivatives, without claiming absent raw evidence passed."""
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1 or manifest.get("reviewedForPublication") is not True:
        raise ValueError("Reference library requires explicit publication review")
    cases = manifest.get("cases", [])
    if len(cases) != len(MATRIX) or {case_key(case) for case in cases} != MATRIX:
        raise ValueError("Expected all 48 distinct requested reference combinations")
    for case in cases:
        if type(case["bodyAngleClockwise"]) is not int or type(case["tabCount"]) is not int:
            raise ValueError("Body angles and tab counts must be integers")
        if case["pageColors"]["colorScheme"] != case["appearance"]:
            raise ValueError(
                "System appearance must match the observed color scheme; page color is a separate axis"
            )
    assets = manifest.get("assets", [])
    names = set()
    for asset in assets:
        name = asset["file"]
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]*\.(webp|jpg)", name) or name in names:
            raise ValueError("Unsafe or duplicate reference asset name")
        names.add(name)
        path = folder / name
        if sha256(path) != asset["sha256"]:
            raise ValueError("Reference derivative hash mismatch: " + name)
        with Image.open(path) as image:
            image.load()
            if image.format not in {"WEBP", "JPEG"} or list(image.size) != asset["sizePixels"]:
                raise ValueError("Reference derivative format/dimensions mismatch: " + name)
            if image.getexif() or image.info.get("xmp") or image.info.get("icc_profile"):
                raise ValueError("Reference derivative contains unreviewed metadata: " + name)
    actual = {
        p.name for p in folder.iterdir() if p.suffix.lower() in {".webp", ".jpg", ".png", ".jpeg"}
    }
    if actual != names:
        raise ValueError("Reference images must all have reviewed manifest entries")
    for case in cases:
        if case["asset"] not in names:
            raise ValueError("Case references an unknown asset")
        crop = case["sourceCropPixels"]
        dimensions = case["sourceSizePixels"]
        if (
            len(crop) != 4
            or any(type(v) is not int for v in crop)
            or not 0 <= crop[0] < crop[2] <= dimensions[0]
            or not 0 <= crop[1] < crop[3] <= dimensions[1]
        ):
            raise ValueError("Invalid source crop")
        if not re.fullmatch(r"[0-9a-f]{64}", case["sourceSha256"]):
            raise ValueError("Missing original source hash")
    conditions = manifest.get("colorConditions", [])
    if conditions:
        if "overview-color-triggers.webp" not in names or {
            c["sheetColumn"] for c in conditions
        } != set(range(len(conditions))):
            raise ValueError("Color conditions require the complete comparison sheet")
        for condition in conditions:
            if (
                condition["systemAppearance"] not in {"light", "dark"}
                or condition["palette"] not in {"light", "dark", "auto"}
                or condition["pageColors"]["colorScheme"] != condition["systemAppearance"]
                or not re.fullmatch(r"[0-9a-f]{64}", condition["sourceSha256"])
            ):
                raise ValueError("Invalid observed color comparison condition")
    return manifest


def export_library(session, output):
    session, output = session.resolve(), output.resolve()
    source = json.loads((session / "publication-review.json").read_text(encoding="utf-8"))
    if source.get("reviewedForPublication") is not True:
        raise ValueError("Review original canvases/crops before exporting public images")
    cases = json.loads((session / "cases.json").read_text(encoding="utf-8"))
    if len(cases) != len(MATRIX) or {case_key(case) for case in cases} != MATRIX:
        raise ValueError("The capture session does not cover the complete requested matrix")
    names = {case["id"] for case in cases}
    if len(names) != len(cases) or set(source["crops"]) != names or set(source["layouts"]) != names:
        raise ValueError(
            "Every unique capture requires an explicit crop and observed layout review"
        )
    for case in cases:
        for value in (case["id"], case["nativeId"], case["nativeSession"]):
            if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", value):
                raise ValueError("Capture/native identifiers must be portable local names")
        for value, extensions in (
            (case["sourceImage"], "png|jpg|jpeg|webp"),
            (case["orientationSource"], "json"),
        ):
            if not re.fullmatch(r"[a-z0-9][a-z0-9-]*\.(" + extensions + r")", value):
                raise ValueError("Capture sources must be local filenames")
            if (session / value).resolve().parent != session:
                raise ValueError("Capture source resolves outside its session")
        native_folder = session / case["nativeSession"]
        if native_folder.resolve().parent != session:
            raise ValueError("Native source resolves outside its session")
        receipt = json.loads((native_folder / (case["nativeId"] + ".json")).read_text())
        if receipt != case["native"] or receipt.get("id") != case["nativeId"]:
            raise ValueError("Indexed native receipt differs from its preserved original")
        layout = source["layouts"][case["id"]]
        if (
            not re.fullmatch(r"[a-z][a-z0-9-]*", layout["contentLayout"])
            or type(layout["nativeSceneToCanvasClockwise"]) is not int
            or layout["nativeSceneToCanvasClockwise"] not in {0, 90, 180, 270}
        ):
            raise ValueError("Record the observed content layout and canvas transform explicitly")
    output.mkdir(parents=True, exist_ok=False)
    assets, public_cases = [], []
    for case in cases:
        name = case["id"]
        raw = session / case["sourceImage"]
        if raw.parent != session or sha256(raw) != case["sourceSha256"]:
            raise ValueError("Original capture hash/path mismatch: " + name)
        crop = source["crops"][name]
        with Image.open(raw) as image:
            dimensions = list(image.size)
            if (
                len(crop) != 4
                or not 0 <= crop[0] < crop[2] <= image.width
                or not 0 <= crop[1] < crop[3] <= image.height
            ):
                raise ValueError("Crop is outside the original image")
            derivative = image.crop(tuple(crop)).convert("RGB")
            target = output / (name + ".webp")
            derivative.save(target, "WEBP", quality=88, method=6, exact=True)
            assets.append(
                {
                    "file": target.name,
                    "sha256": sha256(target),
                    "sizePixels": list(derivative.size),
                    "kind": "canvas-crop",
                }
            )
        view = case["browser"]["viewport"]
        native = case["native"]
        raw_frame = next(c["frame"] for c in native["components"] if c["identifier"] == "WebView")
        frame = {key: raw_frame[key] for key in ("x", "y", "width", "height")}
        layout = source["layouts"][name]
        # Keep only reviewed numeric geometry and controlled fixture labels. Raw native
        # identifiers include tab UUIDs; raw records remain in the ignored session.
        components = []
        for component in native["components"]:
            identifier = component["identifier"].split("?", 1)[0]
            if identifier in {
                "BackButton",
                "SidebarButton",
                "NewTabButton",
                "TabOverviewButton",
                "MoreMenuButton",
                "ReloadButton",
                "TabBarTab",
                "CloseTabBarItemButton",
                "TabBarItemTitle",
                "TabBarItemTitleContainer",
            }:
                item = {
                    "identifier": identifier,
                    "framePoints": {
                        key: component["frame"][key] for key in ("x", "y", "width", "height")
                    },
                }
                if identifier == "TabBarTab":
                    active = parse_qs(component["identifier"].partition("?")[2]).get(
                        "isActive", [None]
                    )[0]
                    item["selected"] = None if active is None else active == "true"
                components.append(item)
        public_cases.append(
            {
                **{
                    key: case[key]
                    for key in ("fold", "bodyAngleClockwise", "appearance", "tabCount", "chrome")
                },
                "id": name,
                "asset": target.name,
                "contentLayout": layout["contentLayout"],
                "layoutGroup": f"{case['fold']}-{layout['contentLayout']}",
                "nativeSceneToCanvasClockwise": layout["nativeSceneToCanvasClockwise"],
                "sourceSha256": case["sourceSha256"],
                "sourceSizePixels": dimensions,
                "sourceCropPixels": crop,
                "capturedAt": case["capturedAt"],
                "viewportCssPx": {
                    key: value
                    for key, value in view.items()
                    if key
                    in {
                        "innerWidth",
                        "innerHeight",
                        "scrollY",
                        "documentHeight",
                        "screenWidth",
                        "screenHeight",
                        "devicePixelRatio",
                        "orientationType",
                        "orientationAngle",
                        "visualWidth",
                        "visualHeight",
                        "visualOffsetTop",
                        "visualScale",
                    }
                },
                "pageColors": {
                    key: case["browser"]["environment"].get(key)
                    for key in ("colorScheme", "pageBackground", "themeColor")
                },
                "fixtureSourceSha256": case["fixtureSourceSha256"],
                "nativeWebViewFramePoints": frame,
                "nativeComponents": components,
                "nativeGesture": native["action"],
                "nativeReceiptSha256": sha256(
                    session / case["nativeSession"] / (case["nativeId"] + ".json")
                ),
                "bodyOrientation": json.loads((session / case["orientationSource"]).read_text())[
                    "result"
                ]["deviceOrientation"],
            }
        )
    for fold in ("closed", "open"):
        for theme in ("light", "dark"):
            selected = sorted(
                (
                    case
                    for case in public_cases
                    if case["fold"] == fold and case["appearance"] == theme
                ),
                key=lambda case: (
                    case["bodyAngleClockwise"],
                    -case["tabCount"],
                    case["chrome"] == "compact",
                ),
            )
            columns = 2 if fold == "closed" else 4
            tile_width, tile_height = 340, 420
            sheet = Image.new("RGB", (tile_width * columns, tile_height * 4), "#e6eaf0")
            draw = ImageDraw.Draw(sheet)
            font = ImageFont.load_default(size=14)
            for index, case in enumerate(selected):
                x, y = (index % columns) * tile_width, (index // columns) * tile_height
                with Image.open(output / case["asset"]) as image:
                    image.thumbnail((tile_width - 16, tile_height - 54))
                    sheet.paste(image, (x + (tile_width - image.width) // 2, y + 48))
                label = f"Body {case['bodyAngleClockwise']} / {case['tabCount']} tab(s) / {case['chrome']}"
                draw.text((x + 8, y + 6), label, fill="#17283b", font=font)
                viewport = case["viewportCssPx"]
                draw.text(
                    (x + 8, y + 26),
                    f"{viewport['innerWidth']} x {viewport['innerHeight']} CSS / Y={viewport['scrollY']}",
                    fill="#46566b",
                    font=font,
                )
            target = output / f"overview-{fold}-{theme}.webp"
            sheet.save(target, "WEBP", quality=88, method=6)
            assets.append(
                {
                    "file": target.name,
                    "sha256": sha256(target),
                    "sizePixels": list(sheet.size),
                    "kind": "resized-contact-sheet",
                }
            )
    public_conditions = []
    conditions = source.get("conditions", [])
    if conditions:
        if len(conditions) > 8:
            raise ValueError("Use at most eight explicit color comparison conditions")
        sheet = Image.new("RGB", (340 * len(conditions), 540), "#e6eaf0")
        draw = ImageDraw.Draw(sheet)
        font = ImageFont.load_default(size=14)
        for index, condition in enumerate(conditions):
            name = condition["sourceImage"]
            if not re.fullmatch(r"[a-z0-9][a-z0-9-]*\.(png|jpg|jpeg|webp)", name):
                raise ValueError("Color comparison sources must be local filenames")
            raw = session / name
            if raw.resolve().parent != session or sha256(raw) != condition["sourceSha256"]:
                raise ValueError("Color comparison source hash/path mismatch")
            crop = condition["sourceCropPixels"]
            with Image.open(raw) as image:
                dimensions = list(image.size)
                if (
                    len(crop) != 4
                    or any(type(v) is not int for v in crop)
                    or not 0 <= crop[0] < crop[2] <= image.width
                    or not 0 <= crop[1] < crop[3] <= image.height
                ):
                    raise ValueError("Color comparison crop is outside the source")
                tile = image.crop(tuple(crop)).convert("RGB")
                tile.thumbnail((320, 460))
                sheet.paste(tile, (index * 340 + (340 - tile.width) // 2, 70))
            colors = {
                key: condition["pageColors"].get(key)
                for key in ("colorScheme", "pageBackground", "themeColor")
            }
            draw.text(
                (index * 340 + 8, 8),
                f"System {condition['systemAppearance']} / page {condition['palette']}",
                fill="#17283b",
                font=font,
            )
            draw.text(
                (index * 340 + 8, 32),
                f"theme-color: {colors['themeColor'] or 'none'}",
                fill="#46566b",
                font=font,
            )
            public_conditions.append(
                {
                    "id": condition["id"],
                    "systemAppearance": condition["systemAppearance"],
                    "palette": condition["palette"],
                    "pageColors": colors,
                    "sourceSha256": condition["sourceSha256"],
                    "sourceCropPixels": crop,
                    "sourceSizePixels": dimensions,
                    "sheetColumn": index,
                }
            )
        target = output / "overview-color-triggers.webp"
        sheet.save(target, "WEBP", quality=88, method=6)
        assets.append(
            {
                "file": target.name,
                "sha256": sha256(target),
                "sizePixels": list(sheet.size),
                "kind": "resized-color-comparison",
            }
        )
    manifest = {
        "schemaVersion": 1,
        "reviewedForPublication": True,
        "cohort": source["cohort"],
        "source": "Original Device Hub window observations; cropped canvas derivatives, not direct simulator framebuffers.",
        "encoding": {
            "format": "WebP",
            "quality": 88,
            "resizedCanvasCrops": False,
            "metadata": "stripped",
        },
        "fixtureSources": source["fixtureSources"],
        "themeAxis": source["themeAxis"],
        "toolVersions": source.get("toolVersions", {}),
        "simulatorControls": source.get("simulatorControls", {}),
        "units": "Body angles: clockwise in canvas; viewport: actual CSS px; native frames: XCTest points; crops: original window pixels. No physical mm.",
        "limits": source["limits"],
        "triggers": source["triggers"],
        "assets": assets,
        "cases": public_cases,
        "colorConditions": public_conditions,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    validate_library(output)
    print(f"Exported {len(public_cases)} reference cases and {len(assets)} reviewed WebP assets")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    export = actions.add_parser("export", help="Export a reviewed complete local capture session")
    export.add_argument("--session", type=Path, required=True)
    export.add_argument("--output", type=Path, required=True)
    verify = actions.add_parser("verify", help="Verify public derivative bytes and matrix coverage")
    verify.add_argument("library", type=Path)
    args = parser.parse_args()
    if args.action == "export":
        export_library(args.session.resolve(), args.output.resolve())
    else:
        manifest = validate_library(args.library.resolve())
        print(
            f"PASS: {len(manifest['cases'])} reference cases; public derivative hashes/dimensions/metadata verified"
        )


if __name__ == "__main__":
    main()
