# Windows handoff

Windows uses the pinned WebKit Windows Port through Avalonia; macOS retains system WKWebView. Both Safari shell and immersive App WebView preserve the [measured scene contract](005-SAFARI-SCENES.md). This page owns current Windows acceptance and regression scope. [WebKit implementation](014-WINDOWS-WEBKIT.md), [release delivery](013-RELEASE.md) and [platform differences](015-PLATFORM-PARITY.md) have separate owners.

## Current status

Milestone **`windows-desktop/2026-10-29`** is the scoped local baseline. Rotated select placement, real Microsoft Pinyin preedit and WebCore-backed page accessibility have local repairs and native evidence. Version 0.1.0 selects the matching engine through metadata; engine publication and rebuilt-package acceptance remain separate delivery steps.

The baseline includes uncommitted host/test changes and native patch `0004`, with exact inputs frozen per evidence session. It is not a Git tag or stable-release approval. Reuse unaffected results as **inherited baseline**, never as tests rerun against a later binary. A public checkout without private evidence may cite this record but cannot claim independent verification.

| Current baseline scope | Result and qualification |
| --- | --- |
| Scenes and drawing | 1501 assertions at actual 192 DPI, covering 24 Safari states, eight mappings, 48 static boundary cases and six window sizes; missing-runtime handling passed separately. This cohort precedes the final host-only AX startup/cache guards. |
| Presentation | The same engine passed accelerated/software animation, scroll damage, scales below/above one, maximize/Inspector/restore, idle stability and document preservation. Source-raster checks are separate from final DWM presentation. |
| Input and menus | Combined-engine Microsoft Pinyin: 47 checks at 144 DPI; select: 56 checks across eight poses at 192 DPI. Earlier input/menu/dialog cohorts cover 96/144/192 DPI with their own binaries. |
| Final accessibility host | 24 native checks at each actual 96/144/192 DPI: public tree discovery, native canvas pixels, names/values/actions, physical hit testing, modal restoration, stale-document rejection and rotated close. Earlier mixed-monitor evidence belongs to its separate host revision. |
| Actual window and Narrator | Final 192-DPI Closed 0°/90°/180° windows were inspected at about 100% Fit. Closed 180° Tab/Shift+Tab navigation and Narrator speech recap confirmed document/edit labels. This is scoped navigation/speech evidence, not full text-navigation or Safari optical equivalence. |
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
| Startup/navigation or developer services | Affected navigation-latency, developer-tools or debugging recipe. |
| Shared viewport/mask/rotation or broad engine integration | Core/build, single-DPI `just test-windows` and affected native checks. Add ordinary-page geometry comparisons only when layout/inset semantics change. |
| Dependency selection or packaging | Release/build/package checks and single-DPI packaged startup/runtime-loading observation; reuse unchanged engine behavior evidence. |

Select rows; do not execute the table as a sequence. `just verify` is portable/build integration, not a wrapper for native Windows acceptance. After a fix, rerun affected checks; broaden only for another affected boundary, an unexplained failure or an explicit wider acceptance request.

Additional DPI is justified for changed scale conversion, physical popup/caret/hit coordinates, pixel rounding/clipping or cross-monitor resource lifetime, or a scale-specific failure. State the reason and use the smallest useful contrast; include 150% only when fractional DPI is relevant. Fit and DPI are distinct. If the environment is unavailable, record the gap instead of repeatedly asking to reconfigure the desktop.

The accessibility recipe does not move across monitors by default. `DEVDUO_TEST_MONITOR_TRANSITIONS=1` explicitly enables that case and requires displays with different actual scales. `DEVDUO_TEST_SCREEN_SCALE` optionally selects one unique matching display; neither option changes system scaling. Leave both unset for current-display checks. `DEVDUO_TEST_FOREGROUND_GATE=1` waits for the native Start button before injecting input. `DEVDUO_TEST_IME_PROFILE=microsoft-pinyin` selects an already enabled profile for the test process and restores its prior profile; it does not register a system keyboard.

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

[English](009-WINDOWS-HANDOFF.md) | [中文](../zh/009-WINDOWS-HANDOFF.md)
