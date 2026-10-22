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

WKWebView owns canvas painting. The original passive footer layer approximated only hit-testable, solid opaque full-width fixed footers and cleared when eligibility ended, without altering DOM, input or scroll range. That fallback remains limited where native fixed-container extension is unavailable; it must not be used to certify translucent masks. The current native path is described in [Architecture](001-ARCHITECTURE.md#drawing-below-floating-chrome). Inspect the actual presented window, since WK snapshots can omit obscured-inset pixels.

For placement regressions, compare the native screen with the exterior after cold start, maximize/restore, sidebar toggles and Fit/1:1 changes; record `nativeClipGeometry` separately from DOM metrics. Native attachment placement and explicit clip configuration must complete the same layout pass. Run `just test-macos` for real AppKit clip/resize/Fit bounds, inset and passive-layer checks; this supplements presented-window acceptance and does not certify Windows.

## Translucent modal backgrounds

The 2026-10-24 iOS 27.1 Device Hub session used Closed portrait and `/document-end.html?fixed=none`. **Fixed scrim** creates `position: fixed; inset: 0; background: rgb(0 0 0 / 55%)`; **Dialog backdrop** uses `showModal()` and the same `::backdrop` color. Close each overlay before switching. `overlay=fixed|dialog` selects an initial condition; changes automatically save a receiver record. The fixture does not enlarge the overlay, pad the document or add scroll locking.

| Condition | Observed extension |
| --- | --- |
| No overlay | Numbered document flow continues behind the address bar |
| Fixed scrim or native dialog, no fixed footer | Bottom and side inset areas become a flat darkened canvas color; rows do not continue through that fill |
| Native-scroll compact / document end | Same dark fill; showing/dismissing preserves the 382 × 598 viewport, Y=505 or maximum Y=1802, and document height 2400 |
| Opaque full-width footer plus overlay | Fresh load gives dark canvas fill beneath the dimmed footer. Dismiss/reopen retained the earlier undimmed purple bottom extension for fixed and dialog overlays, including a later capture; side fill stayed dark |

Expanded navigation retained 382 × 580 and maximum Y=1820. The fixed scrim's DOM bottom stayed at the viewport edge: 580, or compact fractional height 597.65625 rounded to `innerHeight=598`. The browser extends a composited **color**, rather than enlarging the fixed DOM box or dimming every off-viewport flow pixel. This matches [WebKit's backdrop fix](https://github.com/WebKit/WebKit/commit/2ae949b78743): blend backdrop color against page background and extend into obscured insets. [Bug 300965](https://bugs.webkit.org/show_bug.cgi?id=300965) distinguishes this from filter/gradient variants. Blur, gradients, other body directions and other runtimes were not accepted here.

Original window JPEGs, explicit browser-record associations, hashes, native swipe receipts and a resized comparison remain local in `captures/safari/overlay/20261023T194024Z/`, separate from the public 48-case gallery. The footer color-history case is an observed Safari boundary, not the desired general rule. Windows now uses native fixed-container colors and has separately passed overlay acceptance; see [the Windows handoff](009-WINDOWS-HANDOFF.md#default-pages-document-titles-and-modal-insets-2026-10-24). The changed macOS bridge still needs native acceptance. This Windows host has not inspected the original macOS capture session. In a later clarification the user also confirmed the black-background plus `opacity: .5` variant.

Use `tests/DevDuo.WebKitProbe/pages/fixed-overlay.html` with `just test-windows-fixed-overlay` for desktop regression. Capture the actual viewport and native source/scene separately: viewport pixels test alpha composition, while inset pixels test the solid extension. Record document identity, scroll, document size, fixed rectangles and viewport units before opening, while visible and after closing. Cover ordinary fixed masks, dialog backdrops, underlying opaque footers, expanded/compact bottom reservations and side insets. A test fixture or passing compilation alone does not establish Safari visual parity or macOS native acceptance.

## Safari scroll indicators

The 2026-10-24 Closed portrait iOS 27.1 session used `/document-end.html?fixed=none&trace=1`. It records document/client/visual widths, CSS scrollbar settings and bounded scroll/resize samples. **Middle** uses instant `scrollTo`; **Smooth to 1000** uses smooth scrolling. `content=short` removes document overflow, `overflow=scroll` forces that CSS setting, and `scrollbars=none|auto` selects root `scrollbar-width`. These are fixture controls, not changes to DevDuo's renderer or to arbitrary developer pages.

| Condition | Observed indicator behavior |
| --- | --- |
| Fresh long page at rest | Hidden in the recorded initial-presentation sequences; scrollable content alone does not require a visible initial indicator |
| Native swipe and deceleration | A thin overlay appears and tracks position, remains briefly after motion, then fades; idle at nonzero scrollY does not keep it visible |
| Reaching document end in a clean tab | Still visible at the endpoint before fading; visibility must not be a `scrollY < maxScrollY` rule |
| Instant/smooth programmatic scrolling | Actual scrollY changed, without showing the indicator in these samples; DOM scroll events alone do not identify user interaction |
| Short page, including `overflow-y: scroll` | No indicator in the sampled idle/gesture sequences; maximum scrollY remained zero |
| `scrollbar-width: none` on a long page | Native scrolling reached Y=516 without a visible indicator |

Every recorded width remained `innerWidth = documentElement.clientWidth = body.clientWidth = visualViewport.width = 382`, with zero gutter while shown or hidden. The native thumb occupied the **whole screen's outer right edge**, over the side reserved for Safari widgets, rather than the 382-pixel page viewport edge. Its painted thickness is not a layout reservation. [WebKit's scrollbar guidance](https://webkit.org/blog/16301/webkit-features-in-safari-18-2/#scrollbars) explains overlay scrollbars, iOS's thin style, `none`, and why `scrollbar-gutter` does not reserve space with overlays. macOS's **Always** system preference can instead produce consuming scrollbars; that desktop setting is not the iOS target.

Use separate fresh tabs for positive controls: after the `none` sample, the reused tab's subsequent `auto` documents remained visually hidden even during real scrolling, while a new `auto` tab restored the indicator. The cause remains unlocated; those contaminated endpoint attempts do not establish normal endpoint behavior. Original JPEG sequences, timestamped native receipts, browser-record associations and reviewed comparison crops remain in `captures/safari/scrollbars/20261023T195821Z/`. The native runner's `--gesture-delay 4` starts window capture before the swipe; it defaults to zero. Sampling intervals were roughly 0.2–0.5 seconds, so neither a flash between frames nor an exact universal fade delay is certified. Other body directions, nested/horizontal scrollers, restored navigation, accessibility and host-desktop acceptance remain unverified.

The [Windows Safari indicator implementation](014-WINDOWS-WEBKIT.md#safari-scroll-indicators) starts hidden, preserves overlay geometry and page CSS suppression, and distinguishes native intent/motion from document position. Do not force persistent `overflow:scroll`, subtract desktop scrollbar width from the measured viewport, or derive visibility solely from DOM scroll deltas. Leave page content, fixed positions and scroll ranges intact; desktop acceptance remains separate from this Device Hub session.

These Device Hub results were retrieved from dev commit `55e1c353ff5ee1b8dc6e8ea796f228b00964dfb4`. Their original macOS captures are not present on this Windows host; desktop implementation and acceptance are recorded separately.

## Recorded baseline and remaining acceptance

The local 2026-10-21 baseline was established using actual macOS windows, accessibility, saved state and separate Device Hub references. These are recorded observations, not a claim that tests were rerun whenever this document changes.

| Area | Established observation | Remaining limit |
| --- | --- | --- |
| Reference geometry | Original-source regeneration matches public config; eight body mappings, 24 Safari states and four whole selected-tab frames were checked | Runtime/tab-count/rotation-history changes need new evidence; partial-fold geometry is unavailable |
| macOS WKWebView | Safari/immersive, Fit, independent masks, centered tab titles and Closed under-chrome flow were inspected | Open top continuation is approximate; older-system material fallback is untested |
| Language switching | English/Chinese switch preserved URL, scrollY 580, 382 × 598 viewport, native frames/insets; fixture theme and persisted preferences were checked | Windows native language/accessibility/storage acceptance is pending |
| Host UI (SukiUI/Avalonia 12) | Scripted controls against actual WKWebView: Closed/Open multiple/single/App and four Closed angles matched the scene matrix; outline and language changes kept scrollY 400; Fit 0.746 kept 382 × 580 CSS px; a scrolled 1:1 Open preview kept the native view inside the preview viewport with an unchanged 951 × 543 CSS viewport; calibration labels, query/fragment, history, refused-port error and fixture rebuild were exercised; light/dark host renders were inspected | Shortcuts with WKWebView focus, VoiceOver, popups over the native child and the live title bar were not observed interactively; Windows is pending |
| Portable tooling | Public-only setup, source/link checks, image/broker tests and strict bilingual site builds were exercised | Private-evidence verification needs separately supplied originals; no remote CI run is implied |
| Windows | Native Avalonia WebKit 24-state viewport/fixed/units/scroll checks and 39 standalone ordinary-page comparisons | Broader input, DPI/monitor and optical coverage needs [native acceptance](009-WINDOWS-HANDOFF.md); WebKit scope and reproduction are in the [backend guide](014-WINDOWS-WEBKIT.md) |

Original local evidence includes `captures/desktop-validation/container-modes/`, `under-chrome/`, `selected-tab/` and `host-ui/`. The asymmetric 90° mask exposed an extra Y flip that was corrected; symmetric portrait alone was insufficient. The selected-tab correction uses the whole title container, not its narrower title hit frame. Closed 180° remains qualified by the recorded clockwise sequence. Keep these regression cases when altering rendering.

Use [Contributing](../../CONTRIBUTING.md) for current automated checks and the [Windows handoff](009-WINDOWS-HANDOFF.md) for the platform checklist. Physical dimensions, real hardware radii and iOS optical parameters remain unknown regardless of build success.

The [checked-in scene gallery](005-SAFARI-SCENES.md#checked-in-device-hub-reference-gallery) supplies reviewed WebP canvas crops and a complete 48-case manifest. Native swipe sessions, local indexing/review fields and reproducible export commands are documented in the [Python guide](../../scripts/python/README.md#safari-canvas-reference-library). Keep failed animation waits and cross-fold trait/inset remnants separate. If visible chrome and actual viewport disagree, restore a clean scene before accepting it; restarting can reset the commanded body orientation, so verify it again.

[English](003-CALIBRATION-WORKFLOW.md) | [中文](../zh/003-CALIBRATION-WORKFLOW.md)
