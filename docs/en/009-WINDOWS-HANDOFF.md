# Windows handoff

Establish a native Windows baseline for the existing Avalonia/WebView2 route, then improve host controls through the [roadmap](100-ROADMAP.md). macOS has interactive evidence; Windows has only been cross compiled. No workstation name, personal checkout path or simulator identifier belongs in the handoff.

## Setup and platform code

Follow [Contributing](../../CONTRIBUTING.md), including real file/directory symlinks and `core.symlinks=true`; install WebView2 Runtime separately. Run `just check`, then `just serve` / `just dev` in separate mise-enabled terminals. Windows automatically selects `net10.0-windows` and its NuGet lock; do not rewrite the macOS lock for a Windows-only issue. The browser profile stays under local application data at `DevDuo/WebView2`.

| Area | Source |
| --- | --- |
| WebView2 lifecycle, clipping and shell | `src/DevDuo/WindowsWebViewPanel.cs`, `NativeWebView.cs` |
| Host UI and sizing | `src/DevDuo/MainWindow.cs` |
| Shared scene/options and telemetry | `SafariScene.cs`, `SafariContainerOptions.cs`, `Fixtures/browser-observer.js`, `config/safari-scenes.json` |
| macOS behavior to preserve | `src/DevDuo/macos/WebViewBridge.swift` |

## Native acceptance checklist

1. Launch a fresh public checkout. Check Runtime initialization and actionable failure when unavailable. Record OS/runtime/DPI without machine identifiers.
2. Exercise navigation/reload, focus, text input, scrolling, native disposal, resize and repeated mode/device changes. Native children must remain clipped to the inner mask and below host controls.
3. Check both containers, Closed/Open and four body angles; Safari adds initial/scrolled and Open single/multiple tabs. Use the [scene matrix](005-SAFARI-SCENES.md) as the sole target-size table. Compare actual viewport/frame/widget/collision records, not requested settings; Closed 180° retains its rotation-history qualification.
4. Exercise 100%, 150%, 200% scaling and monitor transitions where available. Distinguish CSS pixels, zoom, DIP and image pixels; macOS Fit semantics do not establish Windows zoom correctness.
5. Immersive must remove Safari widgets and old reservations while retaining screen clipping. Toggle the exterior independently. In all four Open angles, preserve whole selected-tab bounds, the close region and centered 14-point scene text.
6. Use the color fixture and live content to check theme hints, edge sampling and scroll transitions. GDI tint/alpha is a fallback without native backdrop blur. Windows currently reserves a smaller WebView2 frame; equivalent under-chrome flow is unverified. See [Architecture](001-ARCHITECTURE.md).
7. Check English/简体中文/system default, restart persistence, errors and accessibility, including a Runtime failure. Switch language while scrolled and compare URL, scroll and actual viewport; use the [localization guide](011-LOCALIZATION.md) for resource behavior.
8. Exercise the host UI: committed URL and title after redirects, back/forward/reload/stop, loading and failure states with **Retry**, and Ctrl+L, Ctrl+R/F5, Alt+←/→, Ctrl+S and Ctrl+\ with focus both in host controls and inside WebView2. Scroll a 1:1 preview in a narrow window and confirm WebView2 stays inside the preview area and never covers the sidebar, toolbar, status bar or scroll bars. Check SukiUI rendering and title bar, ComboBox and tooltip popups above the WebView2 child, Narrator and keyboard traversal, light/dark themes and sidebar/status layout at 100%, 150% and 200%. Outline, scale, language and theme changes must keep the page and scroll position; fold, angle and mode changes recreate the view as described in [Architecture](001-ARCHITECTURE.md).

## Evidence and delivery

Save original screenshots and state in ignored `captures/`, following [Calibration and validation](003-CALIBRATION-WORKFLOW.md). The public config suffices to run; independent Safari source/image verifiers need separately reviewed original evidence. Missing originals are not passing tests. Image tool arguments live in the [Python guide](../../scripts/python/README.md).

Report observed results, missing cases and reproduction steps. Keep host UI changes separable from measured geometry. Physical millimeters/radii, hinge angles and the iOS material kernel remain unknown. Commit/push only when explicitly requested.

## Document-end continuation research (2026-10-22)

The separate official Windows WebKit `324423@main` probe is experimental, not the integrated WebView2 route above. Its reported old rows at `scrollY=1820`, viewport height 580 and document height 2400 are not a Safari target. On the local iOS 27.1 Device Hub Safari, a controlled 2400-CSS-pixel striped page showed no repeated rows: at Y=1770, 50 pixels of real flow continued below the viewport and the rest used the page background; at Y=1820, the entire 98-pixel inset used background. A qualifying full-width purple fixed footer extended its color into that inset; a narrow marker after clearing the footer used the root background. Color-extension eligibility/history is distinct from stale document pixels. These observations use fixture `scrollTo`, not an independently repeated Windows wheel test. Original window PNGs, DOM records and fixture sources are local under `captures/desktop-validation/document-end/`.

For a top-zero, unzoomed scene, let `D` be the root document height, `Y` actual scrollY, `V` the unobscured viewport height and `B` the bottom inset. Valid continuation length is `L = clamp(D - Y - V, 0, B)`. Preserve live content in `[V, V+L)` and explicitly clear/fill `[V+L, V+B)` in the final native rendering surface on each relevant update. Here the fill starts appearing after `2400 - 580 - 98 = 1722`. Keep `D`, `V`, the scroll range and fixed frames unchanged; adding DOM padding or filling the whole inset prematurely hides valid content. Convert CSS coordinates through the actual zoom/DPI transform. Painting only the parent HWND background will not remove stale pixels retained by an opaque child/backing surface.

Use the engine's resolved under-page/canvas background, with an explicitly labeled html/body background fallback; do not copy the last numbered row or use the toolbar theme tint as the page background. Treat fixed-footer color extension as a separate fidelity feature. [WebCore's `insetClipLayerRect`](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebCore/page/LocalFrameView.cpp#L2763) clips continuation at the document extent; [Cocoa's fixed-color extension views](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebKit/UIProcess/API/Cocoa/WKWebView.mm#L3692) are additional native behavior, and [underPageBackgroundColor](https://developer.apple.com/documentation/webkit/wkwebview/underpagebackgroundcolor) defines the color beyond page bounds. Inspect Windows Port clipping, invalidation and backing-surface clearing; these are investigation targets, not a confirmed root cause. Recheck wheel/programmatic paths, short documents, document-height changes, both bottom insets and scroll-away/return before Avalonia integration.

The initial macOS comparison exposed a separate gray canvas/no-footer-extension defect, without repeated rows. It is now corrected by restoring WebKit canvas painting and adding a passive solid full-width fixed-footer color approximation. Both 98/80 bottom insets and footer removal were checked without changing actual viewport, fixed frames or scroll range; see [the regression workflow and limits](003-CALIBRATION-WORKFLOW.md#document-end-and-native-placement-regression). This macOS fix does not resolve or certify the experimental Windows backing-surface behavior.

[English](009-WINDOWS-HANDOFF.md) | [中文](../zh/009-WINDOWS-HANDOFF.md)
