# DevDuo application icon

The mark combines the supplied iPhone Duo open ring and five dots with developer code brackets and a cyan slash on navy. The original references are design inputs, not simulator measurement evidence. Artwork was generated with the built-in image_gen tool; platform exports are mechanical resizing/masking, not device geometry.

`AppIcon.icon/Assets/artwork.png` is the 1024 × 1024 opaque master. `AppIcon.icon/icon.json` is the editable Apple Icon Composer document: system masking and rendering remain separate from the flat artwork. `Assets.car` was compiled with Xcode on macOS, not renamed from an image. `manifest.json` records exact source/output hashes, compiler and deployment target.

- `app.ico`: Windows executable icon at 16, 24, 32, 48, 64, 128 and 256 pixels.
- `app-icon.png`: 256-pixel rounded tile for the Avalonia window.
- `AppIcon.icns`: legacy resource with an 824-pixel tile centered in a 1024-pixel transparent canvas.
- `Assets.car`: modern macOS catalog, bundled in `Contents/Resources` with `CFBundleIconName=AppIcon`; ICNS is the compatibility resource.

After changing the master, run `mise exec -- just sync-icons` on macOS with full Xcode and the locked Python environment. `just export-icons` only exports raster/ICO/ICNS; it leaves the old catalog manifest stale until native compilation. Set `DEVELOPER_DIR` for a nonstandard Xcode installation. `just check-icons` validates checked-in bytes on every platform; public audit also checks them. Hosted packaging consumes these assets without recompiling the catalog or installing Python in native jobs. Compilation and resource validation do not establish actual Finder/Dock appearance on every macOS configuration.

See [Apple's Icon Composer guide](https://developer.apple.com/documentation/xcode/creating-your-app-icon-using-icon-composer). Format workflow follows the reference project's separate modern catalog and safe-zone legacy resource; its artwork is not reused.

Verification on 2026-10-24 against icon commit `338c76c`: all seven ICO sizes decoded, the compiled catalog contained `AppIcon`, and the packaged macOS icon bytes and plist references matched the source. The Avalonia URI loaded the exact PNG from both macOS and Windows assemblies. Finder's thumbnail and Get Info preview displayed the new mark on macOS 27.0.1. Windows cross-compilation passed. The maintainer subsequently confirmed that the Windows implementation agent had already verified the Windows icon; that native validation was not repeated on this Mac. Other macOS appearance variants remain unverified. Private receipts are retained under `captures/desktop-validation/app-icons/`.

## Final generation prompt

Create one finished DevDuo app icon as immaculate flat vector-style raster artwork. Square canvas, full-bleed deep navy background reaching all four edges, no rounded tile mask. Fuse the Duo symbol (thick white open circular arc and five separated white circular dots in a shallow curve across the bottom) with the developer code symbol `</>` centered inside the arc instead of Wi-Fi. White brackets and bright cyan slash; clean gaps, smooth curves, substantial strokes, no words. The system supplies the platform mask and effects. The generated square was resized to the 1024-pixel master; no original measurement captures were modified.

[English](016-APP-ICON.md) | [中文](../zh/016-APP-ICON.md)
