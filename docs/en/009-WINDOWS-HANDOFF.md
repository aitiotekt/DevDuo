# Windows handoff

Windows uses the pinned WebKit Windows Port through Avalonia; macOS retains system WKWebView. Both Safari shell and immersive App WebView preserve the [measured scene contract](005-SAFARI-SCENES.md). This page owns current Windows acceptance and regression scope. [WebKit implementation](014-WINDOWS-WEBKIT.md), [release delivery](013-RELEASE.md) and [platform differences](015-PLATFORM-PARITY.md) have separate owners.

## Current status

Milestone **`windows-desktop/2026-10-29`** is the scoped local baseline. Later releases add scoped follow-up checks on top of it. The baseline includes host/test changes and native patch `0004` that were uncommitted at the time, with exact inputs frozen per evidence session. It is not a Git tag or stable-release approval. Reuse unaffected results as **inherited baseline**, never as tests rerun against a later binary. A public checkout without private evidence may cite this record but cannot claim independent verification.

Metadata selects the matching engine release. The default engine build enables asynchronous scrolling and native touch; release tooling rejects runtimes with missing capabilities or mismatched metadata (see [runtime releases](013-RELEASE.md#windows-webkit-runtime-releases)).

The table keeps the latest result for each area. Earlier runs stay in evidence.

| Area | Latest result and qualification |
| --- | --- |
| Scenes and drawing | Baseline: 1501 assertions at 192 DPI covering 24 Safari states, eight mappings, 48 static boundary cases and six window sizes; missing-runtime handling passed separately. 152 ordinary-page geometry pairs (`20261104T222354596Z-ordinary`) establish layout/viewport semantics only. |
| Presentation and continuation | Baseline: accelerated/software animation, scroll damage, scales below/above one, maximize/Inspector/restore and document preservation. At 120 DPI: 336 frame-bound continuation checks covering Closed 0° and Open 90°/270° at 1:1 and fractional Fit, on GPU and CPU paths with zero GDI uploads (`20261105T091427386Z`); 101 popup/material checks (`20261105T053733174Z`); 30 chrome-motion checks (`20261105T172340844Z`). |
| Fixed overlays and modal dimming | Render stability: 121 checks at 120 DPI and 1:1, including both Closed landscape side insets (`20261111T124812261Z`), and 99 checks at 96 DPI covering Closed portrait side buttons, a transparent inspector canvas, clearing and reversal frames (`20261105T172212152Z`). 42 upstream fixed-color checks (`20261105T091524392Z`) and 41 authored exterior-sheet checks (`20261105T141508970Z`). On the original page in Closed portrait, at 1:1 and at 120 DPI Fit, every settled frame shows the white card above the gray band, and dismissal releases the gray. |
| Native touch and asynchronous scrolling | At 120 DPI: 272 touch checks (`20261105T061318817Z`), 58 lifecycle checks (`20261105T070852663Z`), 242 busy-main-thread wheel checks (`20261105T072748774Z`), 38 input/frame checks (`20261105T071206184Z`) and paired fixed-footer seam samples (`20261105T072708075Z`). These cover trusted events, cancellation, `touch-action`, overflow locks, nested scrolling, a blocked page main thread, and 1:1 and Fit. Host timings end at submission and do not measure DWM scanout. |
| Touch pointer | 189 checks at 120 DPI (`20261105T071006059Z`). Baseline: 187 pixel/coalescing/cancellation checks and 19 interaction checks at 192 DPI and fractional Fit. |
| Input, menus and dialogs | Microsoft Pinyin: 47 checks at 144 DPI. Select: 56 checks across eight poses at 192 DPI. Dialog cohorts cover 96/144/192 DPI on their own binaries. |
| Accessibility | 25 UIA checks covering asynchronous snapshots, notifications and stale-document rejection (`20261105T030237464Z`). Baseline: 24 checks at each of 96/144/192 DPI, plus Closed 180° Tab/Shift+Tab and Narrator label checks. |
| Cold navigation | HTTP/inline/Network-recovery checks pass the 1.5-second gates at 96 DPI on a local fixture. |

Evidence lives under ignored `captures/desktop-validation/windows-runtime/` and `windows-live-edge/`. Each row is scoped to its recorded binary; results from an earlier runtime are inherited, not rerun. Renderer claims require actual transport and GPU-stage evidence, not CSS promotion hints.

Open gaps:

- On original live pages, touch still feels less responsive than mouse. High-speed inertia, dynamic structural changes and the GPU producer's GL completion wait need further work.
- The original-page Closed landscape modal case is unverified. `just test-windows-fixed-overlay` stops at its first absolute-edge case: the native material red channel differs from white by four, against the fixture's tolerance of three.
- File/color/context popups, broad website and stress-scroll coverage, multi-adapter sharing and device-loss/WARP fault injection are unverified.
- UIA TextPattern/IAccessible2 and full surrounding-text/reconversion support are not implemented. Narrator label/value checks do not imply broad assistive-technology compatibility.
- Shared service restoration has no macOS native acceptance. Redistribution review remains open, and the runtime's full-acceptance flag stays false.
- Glass is a desktop approximation; physical hardware dimensions, radii and Apple's optical kernel remain unknown.

## Windows milestone and incremental regression

Default to the affected checks on **one currently available actual DPI**. Do not switch system scale, request another monitor, rebuild WebKit or run all native recipes merely because a Windows file changed. Rebuild the engine only when its inputs change. Keep caches between routine checks.

| Change | Verification to select |
| --- | --- |
| Documentation | `just docs-check`, `just build-docs`, public audit and diff review; no native build/run. |
| Shared state or localization | Affected `just test-core` / `just test-localization`; platform build and focused native observation when runtime/UI behavior changes. |
| Accessibility | `just test-windows-accessibility`; short Narrator observation when discovery, focus or speech semantics change. |
| IME, select or JavaScript dialogs | The corresponding `just test-windows-ime`, `just test-windows-popups` or `just test-windows-page-dialogs`. |
| Material, painting or continuation | Relevant presentation/inset/fixed-overlay/render-stability check and actual-window observation; add fractional Fit at the same DPI when raster boundaries change. |
| Touch-pointer presentation | `just test-windows-touch-pointer`, relevant interaction checks and actual-window motion; do not substitute pointer visibility flags for old-position pixels. |
| Startup/navigation or developer services | Affected navigation-latency, developer-tools or debugging recipe. |
| Shared viewport/mask/rotation or broad engine integration | Core/build, single-DPI `just test-windows` and affected native checks. Add ordinary-page geometry comparisons only when layout/inset semantics change. |
| Dependency selection or packaging | Release/build/package checks and single-DPI packaged startup/runtime-loading observation; reuse unchanged engine behavior evidence. |

Select rows; do not execute the table as a sequence. `just verify` is portable/build integration and does not run native Windows acceptance. After a fix, rerun affected checks; broaden only for another affected boundary, an unexplained failure or an explicit wider acceptance request.

Additional DPI is justified for changed scale conversion, physical popup/caret/hit coordinates, pixel rounding/clipping or cross-monitor resource lifetime, or a scale-specific failure. State the reason and use the smallest useful contrast; include 150% only when fractional DPI is relevant. Fit and DPI are distinct. If the environment is unavailable, record the gap instead of repeatedly asking to reconfigure the desktop.

The accessibility recipe does not move across monitors by default. `DEVDUO_TEST_MONITOR_TRANSITIONS=1` explicitly enables that case and requires displays with different actual scales. `DEVDUO_TEST_SCREEN_SCALE` optionally selects one unique matching display; neither option changes system scaling. Leave both unset for current-display checks. `DEVDUO_TEST_FOREGROUND_GATE=1` waits for the native Start button before injecting input. `DEVDUO_TEST_IME_PROFILE=microsoft-pinyin` selects an already enabled profile for the test process and restores its prior profile; it does not register a system keyboard.

`just test-windows-modal-sequence` covers nested popup edges, scalar color stability, retained composition and developer-service controls. `DEVDUO_TEST_MODAL_BODY_ANGLE=0`, `90` or `270` restricts a diagnostic run to Closed 0°, Open 90° or Open 270°; omit it for the affected three-pose cohort. `just test-windows-developer-restore` uses isolated preferences and eight real application starts to verify independent listener conflicts, malformed configuration blocking and next-launch recovery.

In `just test-windows-native-touch`, `DEVDUO_TEST_TOUCH_LIFECYCLE_ONLY=1` selects contact termination, kinetic stopping and live scroll constraints. `DEVDUO_TEST_TOUCH_TRACKING_ONLY=1` checks single physical-pixel movement and reversal at a fractional backing scale above one. `DEVDUO_TEST_TOUCH_SEAM_ONLY=1` selects paired source and window pixels, including rapid viewport transitions and settled fractional Fit sizes; add `DEVDUO_TEST_TOUCH_SEAM_STATIC_ONLY=1` to run only the settled cases. Leave the selectors unset for the gesture-policy, paint-path and busy-page pose cohort. The checks use independent test profiles.

## Platform code

Setup follows [Contributing](../../CONTRIBUTING.md); runtime discovery, required APIs and isolated profiles are in the [WebKit guide](014-WINDOWS-WEBKIT.md#avalonia-backend).

| Responsibility | Source under `src/DevDuo/` unless stated |
| --- | --- |
| Runtime ABI and browser lifetime | `windows/WindowsWebKitApi.cs`, `WindowsWebKitView*.cs` |
| Native scene, input mapping and presentation | `windows/WindowsWebViewPanel.cs`, `WindowsScreenPresenter.cs` |
| Live composition and material | `windows/WindowsComposition.cs`, `HostCompositor.cpp`, `HostCompositor.h` |
| Shared host/browser/scene state | `Host/`, `Browser/`, `Device/` |
| Production engine inputs | `native/webkit/windows/` at repository root |
| Native checks, support and receipts | `tests/DevDuo.Windows.Tests/{Checks,Harness,Evidence}/` at repository root |

## Native acceptance checklist

Apply the relevant items to the changed behavior.

1. Record actual loaded runtime, DPI, Fit and native frames independently of requested settings. Verify observed orientation, including the Closed 180° rotation-history qualification.
2. Preserve logical viewport, fixed/sticky frames, viewport units and scroll range while changing presentation. Distinguish valid four-side document flow from document-exterior background and fixed-color extension.
3. Check actual window pixels and interactions as well as DOM/telemetry. A populated tree, snapshot, successful GPU submission or source raster cannot replace final-window evidence.
4. Exercise static marker-free pages as well as animation. Test relevant scroll/end/resize/navigation transitions without inserting fixed elements, forcing repaint loops or faking DOM dimensions.
5. Check focus, keyboard/IME, transformed menu/caret/hit positions and accessibility through the public application tree when those paths change. A committed Chinese character alone does not establish preedit support.
6. Verify navigation/disposal rejects stale callbacks and late HWND/DPI work; presentation-only changes must not navigate or reset the document.

## Evidence and reporting

The milestone summary is `captures/desktop-validation/pre-0.1.0/20261028T165509769Z-desktop-followup/final-combined/final-results.json`; its milestone sidecar identifies the baseline. Per-run sources, binaries, native inputs and observations remain under ignored `captures/desktop-validation/windows-runtime/`. Keep original bytes and failed attempts intact. Do not copy raw logs, machine identifiers or browsing records into public documentation.

A follow-up report needs only: baseline ID, affected behavior, checks actually executed, actual DPI/Fit, results and unverified boundaries. Update the matching row above instead of appending a new run. Hashes are provenance outputs, not a substitute for acceptance. Implementation decisions and failure mechanisms belong in the [WebKit guide](014-WINDOWS-WEBKIT.md#decisions-and-failure-lessons); release steps belong in the [release guide](013-RELEASE.md).

[English](009-WINDOWS-HANDOFF.md) | [中文](../zh/009-WINDOWS-HANDOFF.md)
