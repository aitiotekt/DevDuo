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

## Evidence and delivery

Save original screenshots and state in ignored `captures/`, following [Calibration and validation](003-CALIBRATION-WORKFLOW.md). The public config suffices to run; independent Safari source/image verifiers need separately reviewed original evidence. Missing originals are not passing tests. Image tool arguments live in the [Python guide](../../scripts/python/README.md).

Report observed results, missing cases and reproduction steps. Keep host UI changes separable from measured geometry. Physical millimeters/radii, hinge angles and the iOS material kernel remain unknown. Commit/push only when explicitly requested.

[English](009-WINDOWS-HANDOFF.md) | [中文](../zh/009-WINDOWS-HANDOFF.md)
