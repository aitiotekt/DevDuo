# Windows handoff

Windows uses the pinned WebKit Windows Port through Avalonia; macOS retains system WKWebView. Both Safari shell and immersive App WebView preserve the [measured scene contract](005-SAFARI-SCENES.md). This page owns current Windows acceptance and regression scope. [WebKit implementation](014-WINDOWS-WEBKIT.md), [release delivery](013-RELEASE.md) and [platform differences](015-PLATFORM-PARITY.md) have separate owners.

The 0.1.3 source build adds fixed-layer clipping and region-specific Safari material colors to the frame-bound continuation and retained composition in 0.1.2. The original-page bottom tint and fixed visibility have a scoped live retest. Sustained live-page responsiveness, full popup coverage, shared macOS service restoration and packaged delivery remain open.

## 0.1.3 affected checks

At **120 DPI and Fit**, the experimental Windows runtime and compositor v6 pass **47 render-stability checks** (`20261105T091316271Z`). The Closed portrait footer enters and exits through CSS transitions driven by native touch and wheel, and through a script that changes `transform` in `requestAnimationFrame`. Across **54 touch, 67 wheel and 69 scripted-transform WGC frames**, the red button is visible in the real viewport with no red pixels at the checked bottom extension point. The real footer geometry and viewport width are preserved. A finished transform with forwards fill yields its settled blue edge color.

With dark shell appearance, changing the bottom page paint while holding the local side backdrop constant keeps Back, Bookmarks and tab-group button pixels stable in **74 right-side portrait and 77 left-side landscape WGC frames**. The retained bookmarks buttons respond to native clicks after repeated compaction and expansion. These frame observations cover the generic fixtures. The original site and scanout cadence have separate acceptance.

The same patched runtime passes **336 frame-bound continuation checks** (`20261105T091427386Z`), **30 chrome transition checks** (`20261105T091511137Z`) and **42 upstream fixed-color comparisons** (`20261105T091524392Z`) at 120 DPI. These cover Closed and Open 90°/270° geometry, source and window pixels, both transport paths and patterned-underlay fixtures.

The fixed-layer investigation separated two failures. WC applied the anchor offset twice while committing a scroll position. The edge sampler also let a viewport-wide transparent hit target bypass continuity in the actual painted strip. Full-window capture and per-submission fills exposed both faults when polled edge state alone still showed white. The regression fixtures retain differently anchored fixed markers, a full-width gradient, and ordinary content that continues outside its positioned parent. The upstream fixed-color checks preserve the distinction between a card present at first paint and a card appearing over a previously confirmed sheet.

The broad `test-windows-fixed-overlay` run stops at its first absolute-edge load on both the preceding and final engine builds. The raw native material sample is RGB 254, 254, 254 and the displayed tint is RGB 251, 252, 253. The red channel differs from white by four against the fixture's tolerance of three. The separate upstream fixed-color suite directly verifies the native dialog backdrop and both dimming-sheet opening orders.

The authored exterior-sheet follow-up passes **41 checks** in one document at 120 DPI and Fit (`20261105T141508970Z`). A paint-free viewport overlay does not hide the modal's color source. A colored opaque backing produces RGB 15, 20, 25, and removing it preserves the two translucent contributions over the root. A painted foreground overlap, hidden or partial pieces, an obstruction, author clipping, and mismatched colors or owners do not authorize the gray fill. Both sampled fixed markers stay visible through 58 reversal frames, with no red inset fills or pixels. The earlier 114 upstream fixed-color and 336 frame-bound continuation checks are inherited.

On the original page at 120 DPI and Fit, all **371 settled WGC frames** show the white modal card and gray bottom band together. All **425 corresponding page submissions** fill the bottom inset with RGB 25, 25, 25. Closing the modal in that same document and performing 24 native pans yields 21 observed direction reversals across 382 × 580 and 382 × 598 viewports. The active interval has **895 WGC frames and 1,028 page submissions** with no red bottom pixels or fills, no retained modal gray, and no sampled fixed-widget absences. Private evidence is under `captures/desktop-validation/windows-live-edge/`.

An iPhone Duo simulator running iOS 27.1 Safari showed the same first-paint white and staged black distinction at 382 × 580, with the card bottom at 579.986 CSS px. The macOS 27.0.1 system WKWebView probe observed a white bottom extension layer on fresh direct load and a black layer in the staged run with view-level automatic fill enabled. ScreenCaptureKit access was denied, so the macOS result records layer state. The Safari observations use actual simulator display pixels. See `captures/desktop-validation/macos-dimming/20261105T094102Z` for evidence.

## Inherited 0.1.2 checks

The latest continuation, touch lifecycle, popup and live-page checks use **120 DPI**. Routine scope remains one available DPI. The broader milestone below is inherited.

| Current scope | Result and qualification |
| --- | --- |
| Frame-bound continuation | **336 checks**: 192 animated edge-pixel comparisons, 48 asymmetric source/window orientation samples, 36 actual transport/acknowledgement checks, 12 compositing-path checks and 48 input guards. Closed 0° and Open 90°/270° run at 1:1 and fractional Fit (about 95.9%/68.6%). Ninety-six animated pairs use the composed window; the 96 top-edge pairs use the presented page framebuffer because Open glass covers that inset. GPU and CPU paths both report zero GDI uploads. Evidence: `20261105T065602317Z`. |
| Popup/material presentation | **101 checks** passed on the current native-touch runtime at 120 DPI, Open 90° and 55.35% Fit (`20261105T053733174Z`). These cover nested overlays, replacement/crossfade and retained foreground/structure. Five consecutive comparisons of the settled composed window show zero changed pixels. The **300-check** shared-surface cohort retains the broader Fit and visible 1:1 evidence on its earlier revision (`20261104T230609419Z`). Original-site acceptance remains open. |
| Touch pointer and rotation | **189 presented pointer checks** passed at 120 DPI after the pointer timing update (`20261105T071006059Z`). **Nine fresh-document/rapid-rotation checks** confirm rAF completion across eight poses (`20261104T232204845Z`); those nine are document-liveness checks, not pixel acceptance. |
| Upstream rules and wheel transitions | **42 upstream-rule** and **42 transition** checks passed, including eight composed-window inset samples. Layout viewport/fixed geometry is preserved. |
| Ordinary-page layout | **152 paired comparisons** passed against the new engine, with test-only IPC disabled. These verify layout/viewport semantics; the probe does not certify application presentation. Evidence: `captures/desktop-validation/windows-webkit/20261104T222354596Z-ordinary`. |
| Widget/material transition | **30 checks** cover intermediate role optics and geometry, exact endpoints and retained visual structure at 120 DPI/Fit on Closed and Open 90°/270° (`20261105T071644477Z`). The cubic timing is a desktop approximation. |
| Input/frame correlation | **36 checks** passed on software and shared-GPU paths, using actual OS drag/wheel, carried input/frame IDs and machine-wide QPC timestamps through host acknowledgement. The local fixture still records occasional long rAF intervals; this is not arbitrary-site latency or DWM FPS. Evidence: `20261104T231920059Z`. |
| Live-page input sample | Closed touch at 120 DPI/Fit on the viewport publication revision. The drag and pause sample records 341 observed inputs with a 21.35 ms p95 first host submission and 0.60 ms p95 GPU stage, with zero source-frame drops. Visual follow-up reports that the wide seam is largely gone and touch still feels less responsive than mouse. Host timings end at submission. |
| Accessibility | **25 native UIA checks** passed with asynchronous snapshots, cached properties/actions, fresh-cache notifications and stale-document rejection (`20261105T030237464Z`). A real ValueProperty subscriber receives page-originated changes without polling. Notification refresh now requires a UIA/WinEvent consumer; reads remain available. Full AX traversal still runs on the Web process main thread; no CPU saving is established by this functional check. |
| Portable/build integration | Core **403** and Windows compilation passed. Unchanged localization/script/macOS behavior retains its prior scope. |

Evidence lives under `captures/desktop-validation/windows-runtime/`. Rows identify current shared-surface cohorts; older upstream-rule, transition, AX and motion evidence remains scoped to its recorded binary. Exact inputs and failed attempts stay private. Renderer claims require actual transport and GPU-stage evidence, rather than CSS promotion hints alone.

The native-touch experiment passes **58 lifecycle checks at 120 DPI/Fit** (`20261105T070852663Z`). The run covers inertia, nested hit testing, mode/focus/navigation cancellation, live overflow and range changes, release over chrome, a complete chrome click and recovery from a lost release. The release reaches WebKit and subsequent hover produces zero scroll delta. The precise-touch revision also passes **38 input and frame checks** on GPU and CPU paths (`20261105T071206184Z`). The GPU fixture records 18.52 ms p95 to first host submission for 46 drag inputs, with zero source-frame drops.

The fixed-footer fixture uses a translucent child inside a transparent fixed wrapper. At 120 DPI, the ordered viewport revision passes **16 paired source/window drag samples**, **45 viewport-transition submissions**, **530 rapid-reversal submissions** with main-thread work on resize, and **six settled Fit sizes** (`20261105T072708075Z`). The rapid-reversal run includes 246 independent frames. No footer displacement or exposed page was detected in the checked samples.

The tracking fixture runs at 120 DPI with a 1.2489 backing scale. All **32 physical-pixel steps** produce about 0.801 CSS pixels of asynchronous scroll and reversal returns to the starting position. The accepted scroll frame wakes pending pointer updates. The p95 gap between page and pointer submission falls from 18.17 ms to 1.96 ms in the paired fixture runs (`20261105T070632179Z`, `20261105T070809959Z`). The original-page follow-up reports substantially better tracking and the earlier symptoms largely gone. The fixture measures host submission timing.

The rebuilt experimental runtime passes **272 touch checks at 120 DPI** (`20261105T061318817Z`). GPU and CPU paths cover trusted events, cancellation, touch-action, overflow locks and nested scrolling. Touch pans emit no DOM wheel events. During a 2.4-second page-main-thread block, three independent GPU frames advance the root while retaining source paint. The DOM catches up within 21.37 ms after the block. Closed 0° and Open 90°/270° cover 1:1 and Fit. Both 1:1 Open cases have full source-framebuffer checks and cropped window evidence. Fit devices are fully visible.

On that preceding native-touch revision, **189 touch-pointer checks at 96 DPI and fractional Fit** pass (`20261105T041110544Z`), inherited after the lifecycle fixes: actual chrome movement pixels, restoration at previous positions, burst coalescing, mouse-mode suppression and unchanged page geometry. This is a scoped regression, not original-site frame-pacing acceptance.

A preceding **245-check follow-up at 120 DPI** (`20261105T032637186Z`) verifies the source-raster/HWND rounding repair, including a 582×847 source in a 582×848 output with no frame drops. It predates the final contact-timestamp and duplicate-move corrections. **38 input/performance checks** (`20261105T032111393Z`) on a deep overdraw fixture record touch rAF p95 of 18 ms on GPU versus 106 ms on CPU; this measures JavaScript cadence, not DWM FPS or arbitrary-site latency. A private static clone is responsive on both the reference and experimental runtimes, so it does not reproduce the original live-page slowdown or establish an improvement there.

The ordered viewport revision passes **242 busy-main-thread wheel checks at 120 DPI** (`20261105T072748774Z`). Six pose/Fit cases cover retained GPU motion, composed pattern pixels, fixed edges, continuation, DOM convergence and blocking listeners. Obscured Open top pixels use the presented framebuffer. The experimental runtime is selected for subsequent local launches.

CPU snapshots caught full AX-tree traversal in two of three triggered Web-process samples (`20261104T212839448Z-pipeline-profile`). This identifies an expensive active path, not a statistical CPU ranking. The host no longer synchronously rebuilds that tree for each notification. Page frames now carry their continuation rather than meeting an independently committed host fill; complete material-role recipes interpolate with geometry.

**Remaining release blockers:** original live-page material/fluidity and broader asynchronous input/lifecycle acceptance remain open. The accepted reference runtime uses main-thread scrolling and DOM touch emulation; the separate experiment establishes native single-contact policy and independent wheel/touch presentation only in the scopes above. High-speed inertia and dynamic structural changes need further acceptance. GPU production retains a GL completion wait. Shared macOS service restoration, packaging, multi-adapter compatibility and device-loss injection remain unverified. Do not substitute local fixture cadence or DOM geometry for those gates.

Unchanged fixed-overlay, accessibility and service-restoration behavior retain their recorded revisions. Upstream rules, transitions, ordinary-page layout and material-motion checks above were run on the preceding frame-bound raster runtime; do not count them as reruns on the shared-surface runtime. Earlier failed orientation, clock-order and static-glass evidence remains intact. Successful source builds select the runtime for subsequent checkout launches; running instances retain their loaded binaries.

## Current status

Milestone **`windows-desktop/2026-10-29`** is the scoped local baseline. Rotated select placement, real Microsoft Pinyin preedit and WebCore-backed page accessibility have local repairs and native evidence. Metadata selects the matching engine; engine publication and rebuilt-package acceptance remain separate delivery steps.

The baseline includes uncommitted host/test changes and native patch `0004`, with exact inputs frozen per evidence session. It is not a Git tag or stable-release approval. Reuse unaffected results as **inherited baseline**, never as tests rerun against a later binary. A public checkout without private evidence may cite this record but cannot claim independent verification.

| Current baseline scope | Result and qualification |
| --- | --- |
| Scenes and drawing | 1501 assertions at actual 192 DPI, covering 24 Safari states, eight mappings, 48 static boundary cases and six window sizes; missing-runtime handling passed separately. This cohort precedes the final host-only AX startup/cache guards. |
| Presentation | The same engine passed accelerated/software animation, scroll damage, scales below/above one, maximize/Inspector/restore, idle stability and document preservation. Source-raster checks are separate from final DWM presentation. |
| Input and menus | Combined-engine Microsoft Pinyin: 47 checks at 144 DPI; select: 56 checks across eight poses at 192 DPI. Earlier input/menu/dialog cohorts cover 96/144/192 DPI with their own binaries. |
| Final accessibility host | 24 native checks at each actual 96/144/192 DPI: public tree discovery, native canvas pixels, names/values/actions, physical hit testing, modal restoration, stale-document rejection and rotated close. Earlier mixed-monitor evidence belongs to its separate host revision. |
| Actual window and Narrator | Final 192-DPI Closed 0°/90°/180° windows were inspected at about 100% Fit. Closed 180° Tab/Shift+Tab navigation and Narrator speech recap confirmed document/edit labels. This is scoped navigation/speech evidence, not full text-navigation or Safari optical equivalence. |
| Touch pointer (separate host follow-up) | 187 affected pixel/coalescing/cancellation/device-capture checks and 19 interaction checks passed at actual 192 DPI and fractional Fit. Open 90°/270° and Closed 0°/180° retain viewport/fixed/scroll geometry. Original color-page Open landscape motion and scroll compaction were also observed; this is not a display-latency benchmark or a rerun of the inherited matrix. |
| Cold navigation | Final 96-DPI HTTP/inline/Network-recovery checks passed the existing 1.5-second gates. First document observation: 176.83/184.51 ms; matching HTTP source pixels observed by 261.94 ms. These are local-fixture timings, not DWM or external-site benchmarks. |
| Portable integration | `just verify` and bilingual docs checks/build passed: Core 370, localization 1769 with 200 keys/language, JS 50 plus a macOS skip, Python 32. Windows compilation had zero warnings/errors; the docs ES2024 warning remains. |

The 152 ordinary-page geometry pairs establish layout only and retain their recorded engine revision. Older full-DPI, overlay and optical passes do not silently certify a replacement renderer.

Remaining boundaries:

- UIA TextPattern/IAccessible2 and full surrounding-text/reconversion support are not implemented. Narrator label/value checks do not imply broad assistive-technology compatibility.
- File/color/context popups, broad website/stress-scroll coverage and device-removal/WARP fault injection remain unverified.
- Glass is a desktop approximation; physical hardware dimensions, radii and Apple's optical kernel remain unknown.
- Matching dependency/package delivery and redistribution review remain open. Full runtime acceptance stays false; that flag does not require repeating the entire audit after every edit.

## Windows milestone and incremental regression

Default to the affected checks on **one currently available actual DPI**. Do not switch system scale, request another monitor, rebuild WebKit or run all native recipes merely because a Windows file changed. Rebuild the engine only when its inputs change. Keep caches between routine checks.

| Change | Verification to select |
| --- | --- |
| Documentation | `just docs-check`, `just build-docs`, public audit and diff review; no native build/run. |
| Shared state or localization | Affected `just test-core` / `just test-localization`; platform build and focused native observation when runtime/UI behavior changes. |
| Accessibility | `just test-windows-accessibility`; short Narrator observation when discovery, focus or speech semantics change. |
| IME, select or JavaScript dialogs | The corresponding `just test-windows-ime`, `just test-windows-popups` or `just test-windows-page-dialogs`. |
| Material, painting or continuation | Relevant presentation/inset/fixed-overlay check and actual-window observation; add fractional Fit at the same DPI when raster boundaries change. |
| Touch-pointer presentation | `just test-windows-touch-pointer`, relevant interaction checks and actual-window motion; do not substitute pointer visibility flags for old-position pixels. |
| Startup/navigation or developer services | Affected navigation-latency, developer-tools or debugging recipe. |
| Shared viewport/mask/rotation or broad engine integration | Core/build, single-DPI `just test-windows` and affected native checks. Add ordinary-page geometry comparisons only when layout/inset semantics change. |
| Dependency selection or packaging | Release/build/package checks and single-DPI packaged startup/runtime-loading observation; reuse unchanged engine behavior evidence. |

Select rows; do not execute the table as a sequence. `just verify` is portable/build integration, not a wrapper for native Windows acceptance. After a fix, rerun affected checks; broaden only for another affected boundary, an unexplained failure or an explicit wider acceptance request.

Additional DPI is justified for changed scale conversion, physical popup/caret/hit coordinates, pixel rounding/clipping or cross-monitor resource lifetime, or a scale-specific failure. State the reason and use the smallest useful contrast; include 150% only when fractional DPI is relevant. Fit and DPI are distinct. If the environment is unavailable, record the gap instead of repeatedly asking to reconfigure the desktop.

The accessibility recipe does not move across monitors by default. `DEVDUO_TEST_MONITOR_TRANSITIONS=1` explicitly enables that case and requires displays with different actual scales. `DEVDUO_TEST_SCREEN_SCALE` optionally selects one unique matching display; neither option changes system scaling. Leave both unset for current-display checks. `DEVDUO_TEST_FOREGROUND_GATE=1` waits for the native Start button before injecting input. `DEVDUO_TEST_IME_PROFILE=microsoft-pinyin` selects an already enabled profile for the test process and restores its prior profile; it does not register a system keyboard.

`just test-windows-modal-sequence` covers nested popup edges, scalar color stability, retained composition and developer-service controls. `DEVDUO_TEST_MODAL_BODY_ANGLE=0`, `90` or `270` restricts a diagnostic run to Closed 0°, Open 90° or Open 270°; omit it for the affected three-pose cohort. `just test-windows-developer-restore` uses isolated preferences and eight real application starts to verify independent listener conflicts, malformed configuration blocking and next-launch recovery.

With an experimental native-touch runtime selected, `DEVDUO_TEST_TOUCH_LIFECYCLE_ONLY=1` selects contact termination, kinetic stopping and live scroll constraints in `just test-windows-native-touch`. `DEVDUO_TEST_TOUCH_TRACKING_ONLY=1` checks single physical-pixel movement and reversal at a fractional backing scale above one. `DEVDUO_TEST_TOUCH_SEAM_ONLY=1` selects paired source and window pixels, including rapid viewport transitions and settled fractional Fit sizes. Set `DEVDUO_TEST_TOUCH_SEAM_STATIC_ONLY=1` with the seam selector to run only the settled cases. Leave the selectors unset for the gesture-policy, paint-path and busy-page pose cohort. The checks use independent test profiles.

## Setup and platform code

Follow [Contributing](../../CONTRIBUTING.md) for mise, symlinks and normal commands. Build the engine once with `mise --env windows-webkit exec -- just build-windows-webkit-runtime`. Visual Studio C++/Clang and Windows SDK are system dependencies. Runtime discovery, required APIs and isolated profiles are specified in [WebKit setup](014-WINDOWS-WEBKIT.md#avalonia-backend).

| Responsibility | Source under `src/DevDuo/` unless stated |
| --- | --- |
| Runtime ABI and browser lifetime | `windows/WindowsWebKitApi.cs`, `WindowsWebKitView*.cs` |
| Native scene, input mapping and presentation | `windows/WindowsWebViewPanel.cs`, `WindowsScreenPresenter.cs` |
| Live composition and material | `windows/WindowsComposition.cs`, `HostCompositor.cpp`, `HostCompositor.h` |
| Shared host/browser/scene state | `Host/`, `Browser/`, `Device/` |
| Production engine inputs | `native/webkit/windows/` at repository root |
| Native checks, support and receipts | `tests/DevDuo.Windows.Tests/{Checks,Harness,Evidence}/` at repository root |

Only the separate developer-tools frontend uses system WebView2. The measured Windows page has no alternative backend.

## Native acceptance checklist

Apply the relevant items to the changed behavior; this is not a mandatory full run for every edit.

1. Record actual loaded runtime, DPI, Fit and native frames independently of requested settings. Verify observed orientation, including the Closed 180° rotation-history qualification.
2. Preserve logical viewport, fixed/sticky frames, viewport units and scroll range while changing presentation. Distinguish valid four-side document flow from document-exterior background and fixed-color extension.
3. Check actual window pixels and interactions as well as DOM/telemetry. A populated tree, snapshot, successful GPU submission or source raster cannot replace final-window evidence.
4. Exercise static marker-free pages as well as animation. Test relevant scroll/end/resize/navigation transitions without inserting fixed elements, forcing repaint loops or faking DOM dimensions.
5. Check focus, keyboard/IME, transformed menu/caret/hit positions and accessibility through the public application tree when those paths change. A committed Chinese character alone does not establish preedit support.
6. Verify navigation/disposal rejects stale callbacks and late HWND/DPI work; presentation-only changes must not navigate or reset the document.

## Evidence and delivery

The milestone summary is `captures/desktop-validation/pre-0.1.0/20261028T165509769Z-desktop-followup/final-combined/final-results.json`; its milestone sidecar identifies the baseline. Per-run sources, binaries, native inputs and observations remain under ignored `captures/desktop-validation/windows-runtime/`. Keep original bytes and failed attempts intact. Do not copy raw logs, machine identifiers or browsing records into public documentation.

A follow-up report needs only: baseline ID, affected behavior, checks actually executed, actual DPI/Fit, results and unverified boundaries. Hashes are provenance outputs, not a substitute for acceptance. Important implementation decisions and failure mechanisms belong in the [WebKit guide](014-WINDOWS-WEBKIT.md#decisions-and-failure-lessons); release steps belong in the [release guide](013-RELEASE.md).

The current pointer follow-up uses `captures/desktop-validation/windows-runtime/20261029T070548367Z` for automated pixels and `20261029T070628126Z` for original-page presented observations. The short-lived-trail report is consistent with per-message pointer commits sharing the glass device; permanent stale pixels were not reproduced in the static control. The fix removes that submission path, and the bounded latest-position behavior is tested directly. Hardware polling-rate and frame-time comparisons remain unmeasured.

[English](009-WINDOWS-HANDOFF.md) | [中文](../zh/009-WINDOWS-HANDOFF.md)
