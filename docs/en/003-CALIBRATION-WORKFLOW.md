# Reproducible calibration workflow

## Start with the portable baseline

Run the checks and application in the README before changing measured configuration. Keep each new session in a distinct ignored directory under `captures/` and `measurements/`. Record toolchain/runtime versions and capture times without adding personal host names or absolute user-directory paths to public documentation.

## Collect actual Safari observations on macOS

1. Start `mise exec -- just serve` on loopback. Use the local simulator to access the measurement page; a physical device requires an explicitly reachable address and is outside the current verified workflow.
2. Open Xcode's Device Hub and boot the intended simulated device/runtime. Select Closed, Partially Open or Open and the body rotation through Device Hub. Verify the actual displayed state before every capture.
3. Obtain the simulator identifier using `xcrun simctl list devices booted`, then pass it locally as `SIMULATOR_UDID` in the following example. Do not replace that placeholder in public docs.

```sh
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

Modes are `closed`, `partial`, `open`; orientations are `portrait`, `portraitUpsideDown`, `landscapeLeft`, `landscapeRight`. The runner verifies observed pose, captures original native component JSON, explicit-display PNGs, browser measurements, source hashes and optionally transition video. A commanded orientation is not proof of actual rotation. Screen and body coordinates can differ.

4. Wait for initial loading and material motion to settle. Capture initial and scrolled states separately; confirm observed scroll offset and actual viewport in browser records. Record up-scroll and keyboard changes as additional states where required by the matrix. A swipe command alone does not establish a scroll state.
5. In Open Safari, collect one-tab and multiple-tab scenes separately. Opening a new URL can create a tab and change the shell geometry. The existing runner supports `--tab-mode single --reuse-current-url URL` for an already loaded, uniquely labeled calibration page; preserve and restore any previous page deliberately.
6. Retain original evidence without conversion. Explicit display selection matters: the default XCTest image can refer to the wrong display. Check capture dimensions, nonempty content, source timestamps and hashes before accepting a frame. Keep unsuccessful attempts separately labeled.

## Update references deliberately

The current cohort extractor expects the original local evidence layout documented in [reference-data.md](007-REFERENCE-DATA.md). It is not a generic future-session importer. Generate into a scratch file, inspect the changed values against native frames/browser telemetry, then update configuration only for accepted observations. Keep screen-mask geometry, exterior proportions, optical surfaces and accessibility hit frames distinct.

An active tab's `TabBarTab` hit rectangle excludes its close button. Use the complete `TabBarItemTitleContainer` for the visible tab background and reconcile the close-button union. This avoids the selected tab appearing smaller than its neighbors.

## Validate desktop reproduction

Run both Safari and immersive containers, Closed/Open and each of the four body rotations. In Open Safari, repeat with the tab strip enabled and disabled, at the top and after scrolling. Save desktop state and original app screenshots. Compare actual CSS viewport, native frame, obscured insets, widget positions, tab rectangles and collisions independently of requested settings.

The following **archived, cohort-specific** verifiers are for reproducing the established baseline with its original private evidence, not accepting arbitrary new sessions:

```sh
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_desktop_scenes.py captures/desktop-session --include-single --output captures/desktop-session/verification.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_tab_strip.py captures/tab-session --output captures/tab-session/verification.json
```

These verifiers expect their established state-file naming/layout; inspect each tool before preparing a new session. They do not infer missing originals. Use `uv run --locked devduo-tools png-compare` for equal-sized lossless PNGs with explicit regions and exclusions. Use `uv run --locked devduo-tools contact-sheet --pair REFERENCE CANDIDATE --output NEW.png` for review sheets. It fits images into panels and cannot establish optical fidelity. See the [script inventory](012-SCRIPT-INVENTORY.md) and [archive reproduction notes](../../scripts/archive/README.md).

For material behavior preserve multiple frames/video, page colors, theme hints, scroll direction and capture timing. Native macOS glass is an approximation to iOS Safari. On the tested runtime a WK snapshot omits under-inset continuation that is visible in the app window; inspect the actual presented window as well. The [measurement contract](002-MEASUREMENT-CONTRACT.md) defines what may be claimed.

[English](003-CALIBRATION-WORKFLOW.md) | [中文](../zh/003-CALIBRATION-WORKFLOW.md)
