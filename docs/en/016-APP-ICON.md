# DevDuo application icon

The mark combines the supplied iPhone Duo open ring and five dots with developer code brackets and a cyan slash on navy. The original references are design inputs, not simulator measurement evidence. Artwork was generated with the built-in image_gen tool; platform exports only resize and mask it.

`assets/icons/AppIcon.icon/Assets/artwork.png` is the 1024 × 1024 opaque master. `AppIcon.icon/icon.json` is the editable Apple Icon Composer document; system masking and rendering stay separate from the flat artwork. `Assets.car` is compiled with Xcode on macOS. `manifest.json` records exact source/output hashes, compiler and deployment target.

- `app.ico`: Windows executable icon at 16, 24, 32, 48, 64, 128 and 256 pixels.
- `app-icon.png`: 256-pixel rounded tile for the Avalonia window.
- `AppIcon.icns`: legacy resource with an 824-pixel tile centered in a 1024-pixel transparent canvas.
- `Assets.car`: modern macOS catalog, bundled in `Contents/Resources` with `CFBundleIconName=AppIcon`; ICNS is the compatibility resource.

After changing the master, run `mise exec -- just sync-icons` on macOS with full Xcode and the locked Python environment. `just export-icons` only exports raster/ICO/ICNS and leaves the catalog manifest stale until native compilation. Set `DEVELOPER_DIR` for a nonstandard Xcode installation. `just check-icons` validates checked-in bytes on every platform; the public audit also checks them. Hosted packaging consumes these assets without recompiling the catalog. See [Apple's Icon Composer guide](https://developer.apple.com/documentation/xcode/creating-your-app-icon-using-icon-composer).

Current verification: all ICO sizes decode, the compiled catalog contains `AppIcon`, packaged macOS icon bytes and plist references match the source, and the Avalonia URI loads the PNG on both platforms. Finder thumbnails and Get Info on macOS 27.0.1 and the Windows executable icon were checked natively. Dock appearance and other macOS appearance variants are unverified. Private receipts are under `captures/desktop-validation/app-icons/`.

## Generation prompt

Create one finished DevDuo app icon as immaculate flat vector-style raster artwork. Square canvas, full-bleed deep navy background reaching all four edges, no rounded tile mask. Fuse the Duo symbol (thick white open circular arc and five separated white circular dots in a shallow curve across the bottom) with the developer code symbol `</>` centered inside the arc instead of Wi-Fi. White brackets and bright cyan slash; clean gaps, smooth curves, substantial strokes, no words. The system supplies the platform mask and effects.

The generated square was resized to the 1024-pixel master.

[English](016-APP-ICON.md) | [中文](../zh/016-APP-ICON.md)
