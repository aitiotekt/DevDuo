# Calibration and validation

The [measurement contract](002-MEASUREMENT-CONTRACT.md) owns coordinate/provenance rules; the [scene matrix](005-SAFARI-SCENES.md) owns accepted viewport/tab geometry. This guide describes collecting and comparing evidence, not platform pass histories.

## Collect a new Safari session

1. Run `mise exec -- just serve` and open calibration in the intended local Device Hub runtime. Physical-device networking is outside this workflow.
2. Select fold/body angle and verify the displayed scene before each capture. Use `xcrun simctl list devices booted` for the local identifier; public examples use placeholders.

```sh
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

Modes are `closed`, `partial`, `open`; orientations are `portrait`, `portraitUpsideDown`, `landscapeLeft`, `landscapeRight`. Partial-fold collection is not a calibrated partial-fold model. Command success alone does not prove observed orientation.

3. Wait for load/material motion to settle. Record actual scroll and viewport for initial/compact/up-scroll or keyboard cases; a swipe is not proof of a state transition.
4. Collect Open single/multiple tabs separately. `--tab-mode single --reuse-current-url URL` can reuse a uniquely labeled page; preserve/restore prior pages deliberately.
5. Use a fresh ignored `captures/` / `measurements/` session. Retain original component JSON, explicit-display PNGs, browser records, versions/timestamps, source hashes and optional video. Check nonempty image dimensions and selected display; default XCTest images can target the wrong display. Preserve failed attempts separately.

## Update references and compare

Archived extractors assume their original cohort layout; they are not general importers. Follow the [archive reproduction notes](../../scripts/archive/README.md), generate into a new temporary path and compare against native/browser records before changing accepted config. Missing original evidence must remain explicit.

Choose only affected desktop checks. Geometry changes must preserve both containers, body mappings, initial/compact states, Open tab variants, masks and real CSS viewport. Compare actual DOM viewport, drawing frame/insets, complete tab containers and collisions independently of requested settings. Language and presentation changes preserve the document/scroll; immersive mode clears Safari reservations.

`devduo-tools png-compare` compares equal-scale lossless PNGs with explicit crops/exclusions. `contact-sheet` resizes for presentation and cannot certify optics. Command details belong in the [Python guide](../../scripts/python/README.md); video/timing utilities belong in the [Swift guide](../../scripts/swift/README.md).

Use multiple frames/video for dynamic material, recording page/background/meta inputs and scroll direction. WK snapshots can omit visible under-inset flow, so inspect the actual window too. JPEG observations or converted images are not original lossless framebuffers; pixel differences do not establish hardware dimensions or Apple's glass kernel.

## Document-end and native placement regression

Serve `/document-end.html?fixed=full` and compare it with `fixed=narrow` and `fixed=none`. Use the authored scroll/height controls and save actual DOM/state/window evidence. The fixture API supports repeatable programmatic scrolling, which does not establish native-wheel acceptance.

The target is real ordinary flow within document bounds, then canvas background outside them. An eligible full-width opaque fixed footer can extend its color separately. Narrow/removed footers must not fabricate ordinary flow; Safari may retain earlier footer colors, so use a clean page when comparing variants. Inset changes must preserve actual fixed frames, document size and scroll range. The implementation rule is in [WebKit drawing](014-WINDOWS-WEBKIT.md#drawing-and-document-boundaries).

Compare the native screen with the exterior at cold start, resize/restore, sidebar changes and relevant Fit settings. Record native placement/clip geometry separately from DOM. macOS AppKit checks supplement actual-window observations; they do not accept Windows behavior or an unavailable system extension.

## Translucent modal backgrounds

Use the fixture's fixed scrim and native dialog backdrop separately, clearing each before the next case. Include renderer `opacity`, an underlying full-width footer, compact state and document end. These are authored test inputs, not production viewport enlargement or DOM padding.

Safari's relevant distinction is **color extension**, not a larger fixed DOM box: viewport pixels receive authored alpha, while obscured strips may use a flat blended canvas color instead of continued texture. [WebKit's backdrop implementation](https://github.com/WebKit/WebKit/commit/2ae949b78743) and [filter/gradient qualification](https://bugs.webkit.org/show_bug.cgi?id=300965) explain that boundary. Footer color-history behavior is a qualification, not a universal target rule. Do not generalize to arbitrary filters, gradients or ancestor group opacity.

`fixed-overlay.html` and `just test-windows-fixed-overlay` compare viewport alpha and inset colors independently. Record document identity, extent, scroll, fixed rectangles and viewport units before/during/after. Exclude chrome-covered pixels from direct-page sampling and retain final-window evidence there. Current Windows and macOS system/fallback acceptance belongs in [Windows handoff](009-WINDOWS-HANDOFF.md) and [Platform capabilities](015-PLATFORM-PARITY.md).

## Safari scroll indicators

Use `/document-end.html?fixed=none&trace=1` or the dedicated `scroll-indicators.html` fixture. Its short/overflow/CSS controls are test conditions, not renderer workarounds.

The observed target starts hidden, appears with native scrolling/deceleration, tracks position and fades after motion. Idle scroll position or script-only instant/smooth motion does not require a thumb. Short content, `scrollbar-width:none` and hidden/zero-width authored bars are negative controls. Record native keyboard, canceled-wheel/script interaction and endpoint behavior separately; these observations do not specify exact thickness or timing for every scene/runtime.

The Windows implementation uses native eligibility plus a passive scene-edge visual; macOS retains its documented host-policy gap. Neither may change document viewport, fixed positions or scroll range to manufacture a pass. Current implementation belongs in the [backend](014-WINDOWS-WEBKIT.md#safari-scroll-indicators), not a dated result table here.

## Validation ownership and lessons

[Windows handoff](009-WINDOWS-HANDOFF.md) and [Platform capabilities](015-PLATFORM-PARITY.md) own current acceptance; [Contributing](../../CONTRIBUTING.md) selects checks. The [reviewed gallery](005-SAFARI-SCENES.md#checked-in-device-hub-reference-gallery) and private originals provide reference provenance, not a fresh native pass.

Preserve the reusable lessons: asymmetric masks reveal flips hidden by symmetry; selected backgrounds use whole tab containers, not hit frames; Closed 180° depends on capture history. Cross-fold trait/inset remnants or a restart into another body angle invalidate comparisons until a clean observed scene is restored.

Original evidence stays private and unchanged. Detailed timings, hashes and pass counts belong in receipts; only the current acceptance baseline carries summary metrics. Physical dimensions, real hardware radii and iOS optical parameters remain unknown.

[English](003-CALIBRATION-WORKFLOW.md) | [中文](../zh/003-CALIBRATION-WORKFLOW.md)
