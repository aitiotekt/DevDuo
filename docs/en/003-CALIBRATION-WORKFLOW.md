# Calibration and validation

Use the committed baseline before changing references. The [measurement contract](002-MEASUREMENT-CONTRACT.md) defines coordinate/provenance rules; the [scene matrix](005-SAFARI-SCENES.md) is the single viewport and tab-geometry reference.

## Collect a new Safari session

1. Run `mise exec -- just serve` and open the calibration page in the local simulator. Physical-device networking is outside the verified workflow.
2. Boot the intended Xcode Device Hub runtime, choose Closed/Partially Open/Open and the body rotation, and verify the displayed pose before each capture. Obtain the local identifier with `xcrun simctl list devices booted`; keep public examples as placeholders.

```sh
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

Modes are `closed`, `partial`, `open`; orientations are `portrait`, `portraitUpsideDown`, `landscapeLeft`, `landscapeRight`. The runner verifies observed pose and retains original native component JSON, explicit-display PNGs, browser records, source hashes and optional video. A command succeeding does not prove the pose changed; partial-fold collection support is not a calibrated partial-fold model.

3. Capture initial/scrolled states after loading and material motion settle; inspect actual scrollY and viewport. Add up-scroll/keyboard cases as needed. A swipe alone is not a verified scroll state.
4. Collect Open single/multiple tabs separately. For an already loaded, uniquely labeled page, `--tab-mode single --reuse-current-url URL` avoids creating another tab. Preserve and restore any previous page deliberately.
5. Use a fresh session directory under ignored `captures/` / `measurements/`. Record runtime versions and times, keep failed attempts separate, and accept only nonempty images with verified dimensions, explicit display selection, timestamps and hashes. The default XCTest image can refer to the wrong display.

## Update references and compare

Historical extractors/validators assume the original cohort layout; they are not generic importers for arbitrary future sessions. The [archive guide and review index](../../scripts/archive/README.md) own their exact inputs/commands. Generate to a new `temp/` path, compare against native frames/browser records and preserve original source bytes before updating accepted config. Missing original evidence must fail explicitly.

For desktop acceptance, exercise both containers, Closed/Open and four body angles. Safari adds initial/scrolled states and both Open tab-strip settings (24 reference states). Save app state and original window screenshots; compare actual DOM viewport, native drawing frame, obscured insets, widget positions, whole-tab bounds and collisions independently of requested options. Switching to immersive must clear reservations. Fit must preserve the logical viewport, and language changes must preserve the loaded page, scroll and measured geometry.

Use `mise exec -- uv run --locked devduo-tools png-compare` on equal-scale lossless PNGs, with explicit crops/exclusions. `contact-sheet --pair REFERENCE CANDIDATE --output NEW.png` is for presentation; it resizes and cannot certify optical fidelity. Argument details live in the [Python guide](../../scripts/python/README.md); video timing/hash checks are in the [Swift guide](../../scripts/swift/README.md).

Preserve multiple frames/video, page/theme colors, scroll direction and capture timing when comparing material behavior. Inspect the actual app window: WK snapshots can omit visible under-inset continuation. Window JPEG observations and explicitly labeled PNG conversions are not original lossless simulator framebuffers. Pixel-diff statistics describe a matched pair, not hardware fidelity or the iOS glass kernel.

## Document-end and native placement regression

Run `just serve`, then load `/document-end.html?fixed=full` in DevDuo and Device Hub Safari. The maintained 2400-CSS-pixel fixture provides full-width, narrow and absent fixed-footer variants, scroll controls and **Save observation**. Clear the footer before testing the narrow variant: Safari can retain an earlier extension color. Save actual DOM metrics and original window images; the page's `devduoDocumentEndProbe` API also supports reproducible programmatic scrolling. Programmatic results do not establish wheel-input acceptance.

The 2026-10-22 macOS session under `captures/desktop-validation/document-end/20261022T071643Z-fix/` compared all three variants against iOS 27.1 Safari. At the document end, the full-width opaque footer extends purple; narrow/absent footers leave the teal canvas background, with no repeated numbered rows. DevDuo retained width 382 and document height 2400: expanded bottom inset 98 gives viewport height 580 / maximum scrollY 1820; compact inset 80 gives 598 / 1802. The fixed footer remains 28 pixels high with its bottom equal to viewport height. Safari stayed at its expanded inset during this programmatic comparison; compact DevDuo was checked separately. These are CSS/native scene units, not physical dimensions.

WKWebView now owns canvas painting. The additional passive footer layer approximates only hit-testable, solid opaque full-width fixed footers; it is cleared when eligibility ends and does not alter DOM, input or scroll range. Gradients, translucent surfaces, sticky elements and arbitrary Safari color-history heuristics remain outside this approximation. Inspect the actual presented window, since WK snapshots can omit obscured-inset pixels.

For placement regressions, compare the native screen with the exterior after cold start, maximize/restore, sidebar toggles and Fit/1:1 changes; record `nativeClipGeometry` separately from DOM metrics. Native attachment placement and explicit clip configuration must complete the same layout pass. Run `just test-macos` for real AppKit clip/resize/Fit bounds, inset and passive-layer checks; this supplements presented-window acceptance and does not certify Windows.

## Recorded baseline and remaining acceptance

The local 2026-10-21 baseline was established using actual macOS windows, accessibility, saved state and separate Device Hub references. These are recorded observations, not a claim that tests were rerun whenever this document changes.

| Area | Established observation | Remaining limit |
| --- | --- | --- |
| Reference geometry | Original-source regeneration matches public config; eight body mappings, 24 Safari states and four whole selected-tab frames were checked | Runtime/tab-count/rotation-history changes need new evidence; partial-fold geometry is unavailable |
| macOS WKWebView | Safari/immersive, Fit, independent masks, centered tab titles and Closed under-chrome flow were inspected | Open top continuation is approximate; older-system material fallback is untested |
| Language switching | English/Chinese switch preserved URL, scrollY 580, 382 × 598 viewport, native frames/insets; fixture theme and persisted preferences were checked | Windows native language/accessibility/storage acceptance is pending |
| Host UI (SukiUI/Avalonia 12) | Scripted controls against actual WKWebView: Closed/Open multiple/single/App and four Closed angles matched the scene matrix; outline and language changes kept scrollY 400; Fit 0.746 kept 382 × 580 CSS px; a scrolled 1:1 Open preview kept the native view inside the preview viewport with an unchanged 951 × 543 CSS viewport; calibration labels, query/fragment, history, refused-port error and fixture rebuild were exercised; light/dark host renders were inspected | Shortcuts with WKWebView focus, VoiceOver, popups over the native child and the live title bar were not observed interactively; Windows is pending |
| Portable tooling | Public-only setup, source/link checks, image/broker tests and strict bilingual site builds were exercised | Private-evidence verification needs separately supplied originals; no remote CI run is implied |
| Windows | Shared WebView2 route cross compiled | Initialization, input, DPI, monitor changes, zoom Fit, clipping and appearance need [native acceptance](009-WINDOWS-HANDOFF.md) |

Original local evidence includes `captures/desktop-validation/container-modes/`, `under-chrome/`, `selected-tab/` and `host-ui/`. The asymmetric 90° mask exposed an extra Y flip that was corrected; symmetric portrait alone was insufficient. The selected-tab correction uses the whole title container, not its narrower title hit frame. Closed 180° remains qualified by the recorded clockwise sequence. Keep these regression cases when altering rendering.

Use [Contributing](../../CONTRIBUTING.md) for current automated checks and the [Windows handoff](009-WINDOWS-HANDOFF.md) for the platform checklist. Physical dimensions, real hardware radii and iOS optical parameters remain unknown regardless of build success.

[English](003-CALIBRATION-WORKFLOW.md) | [中文](../zh/003-CALIBRATION-WORKFLOW.md)
