# Device Hub Safari body / scene correspondence

The current app uses `config/safari-scenes.json`, extracted from original local Device Hub evidence. `Body +90°` chooses the measured body pose; it does not blindly rotate the last screen. The host separately records body rotation, scene rotation, actual DOM viewport, rendered DIP frame, and unscaled scene-coordinate component rectangles. Desktop UA, DPR, orientation APIs and CSS safe-area values remain native.

| Body angle | Closed scene | Sidebar | Open scene |
| --- | --- | --- | --- |
| 0° | 466 × 678 | right | 951 × 669 |
| 90° | 678 × 466 | right; camera below tabs | 669 × 951 |
| 180° | 678 × 466, retained preceding landscape | right | 951 × 669 |
| 270° | 678 × 466 | left; camera above navigation | 669 × 951 |

The closed 180° reference is the actual outcome of the recorded clockwise sequence. Safari retained the preceding landscape scene, so this is not evidence that upside-down portrait is supported or that all rotation histories produce the same scene. Open mask rotation signs are representations of a symmetric outline, not additional hardware measurements.

## Tab strip switch

The localized `Multiple tabs` checkbox is enabled for Open + Safari. Checked represents multiple tabs and includes the extra tab row. Unchecked represents one tab, omits that row and expands the real viewport. App mode has no browser chrome; Closed has its independent side widgets.

| Open scene | Multiple tabs initial | One tab initial | Scrolled, either mode |
| --- | --- | --- | --- |
| 951 × 669 | 951 × 543 | 951 × 589 | 951 × 618 |
| 669 × 951 | 669 × 825 | 669 × 871 | 669 × 900 |

The extra row consumes 46 screen-coordinate points in the measured scenes: expanded top reservation126 with multiple tabs,80 with one; compact51. Original single-tab Safari also reports safe-area top2 at the top and31 while compact, bottom34; those observations remain source data and are not injected into desktop page CSS.

All tab titles use one14-point medium font in scene coordinates. The macOS renderer centers a measured text line inside each tab slot and truncates long titles. Windows draws all tab titles with one pixel-unit font and centered alignment. Placeholder labels keep private browsing titles out of the demo. Slot count/width comes from the corresponding reference capture and its tab count; it is not a universal rule that a given device angle has that many tabs. This is a shell illustration, not a complete tab-management implementation.

## Reproduce and verify

1. Run the local receiver and load the calibration page in Safari. For one-tab captures, navigate an existing tab rather than opening another. Preserve its original URL and restore it afterward.
2. Select the fold mode and rotate in Device Hub. Record body orientation before/after; the runner rejects a mismatched actual pose.
3. Use the normal runner for multiple tabs. For a single existing calibration tab, use `--tab-mode single --reuse-current-url URL`, where URL is the already loaded loopback page with `pose=open-single-ORIENTATION` and a unique captureSession. The flag skips simctl openurl; the capture broker still saves original bytes from explicit display3. Do not use the invalid default XCTest image on that display.
4. With the original private evidence, use the archived cohort extractor to regenerate references: `mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_safari_scenes.py`. Every frame and image carries source paths and hashes. Verified one-tab sources live under each `open-single-*/verified/`; the first failed idle-wait attempt is retained separately and never accepted as a valid image.
5. In DevDuo, choose Fit preview, run each body angle and fold, capture initial/scroll, and use Save state. On macOS Fit now scales the native view coordinate space and keeps the CSS viewport unchanged. Record original CUA window JPEGs; WK snapshots alone miss under-inset continuation.
6. For this original cohort, compare its established state/screenshot layout with the archived verifier: `mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_desktop_scenes.py DIRECTORY --include-single --output DIRECTORY/verification.json`. The verifier independently checks original native component JSON, source hashes, all24 expected state records, actual DOM dimensions and widget collisions.
7. `uv run --locked devduo-tools contact-sheet --pair REFERENCE CANDIDATE --output NEW.png` creates presentation-only explicit image pairs using Pillow. The original PNG/JPEG evidence remains unchanged. These crops/resizes are not a pixel-accuracy test; scroll offsets, live clock and desktop text rasterization can differ.

## Limits

macOS Closed uses public WKWebView obscured insets so document flow actually paints below the address bar; the horizontal widget band uses document-root background color as an explicitly identified approximation. Open reserves a smaller native frame. macOS uses NSGlassEffectView, with adjustable material contribution; it does not recreate iOS Safari's private optical kernel. Wi-Fi/status illustrations and tab content remain approximations. Windows retains WebView2, shared frame references, vector icons and a GDI material fallback; cross compilation is not Windows runtime validation. Its Fit route uses WebView2 zoom and remains unverified on Windows.

All screen/mask/radius data comes from simulator artwork and measurements. Physical millimeters and real hardware radii remain unknown.

## Selected-tab bounds correction

The original TabBarTab accessibility rectangle of a selected tab excludes its32-point CloseTabBarItemButton. Using that title hit rectangle as the visual background made the selected tab narrower than adjacent tabs. The extractor now retains the original hit frame and separately reads the whole32-point-high TabBarItemTitleContainer as visualFrame, validating the union with the close button. At body0° the hit width194.67 becomes whole-tab226.67; the other three selected widths are124.33,150 and100.

The strip now draws one continuous gray track with separators, a full-size selected surface, a close mark and generic favicon placeholders. Overlapping accessibility hit rectangles remain in source data; exposed paint/text rectangles are clipped at the next whole-tab origin. All titles keep the same14-point scene font and vertically centered line. Colors, stroke softness and platform glass rendering are still approximations.

The earlier24-state verifier covers viewports and main navigation/address controls. The additional `scripts/archive/2026-10-21/verify_tab_strip.py` checks actual runtime tab geometry for all four Open angles against original whole-tab and Close frames. Dedicated evidence and source snapshots live in `captures/desktop-validation/selected-tab/`; single-tab off and compact scroll transitions are also checked. Windows shares the corrected reference geometry and track rendering, with runtime validation pending.

[English](005-SAFARI-SCENES.md) | [中文](../zh/005-SAFARI-SCENES.md)
