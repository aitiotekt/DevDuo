# Architecture and browser containers

Avalonia owns the host window and shared device/scene selection. `NativeWebView` hosts a system WKWebView on macOS or WebView2 on Windows. Platform renderers compose browser content and Safari illustrations inside an independent screen mask; exterior artwork can be toggled without removing that mask.

## Code map

| Layer | Source | Responsibility |
| --- | --- | --- |
| Host | `src/DevDuo/MainWindow.cs`, `NativeWebView.cs` | Controls, URL submission, preview sizing, saved state and native lifetime |
| Scene model | `config/`, `DeviceProfile.cs`, `SafariScene.cs`, `SafariContainerOptions.cs` | Paths, body/scene mapping, widget/tab frames and presentation options |
| macOS | `src/DevDuo/macos/WebViewBridge.swift` | WKWebView, native glass, clipping, obscured insets and color sampling |
| Windows | `src/DevDuo/WindowsWebViewPanel.cs` | WebView2, native region clipping, GDI composition and zoom-based Fit |
| Observation | `src/DevDuo/Fixtures/browser-observer.js` | Actual desktop viewport, scroll, theme, title and hostname |
| Calibration | `web/`, `scripts/python/`, `scripts/swift/`, `scripts/DeviceHubUITests/` | Browser records, actual Safari frames, captures and comparisons |

The receiver serves fixed calibration assets and writes local JSON; it does not control the app or execute page-supplied commands. Command ownership and setup are in [Contributing](../../CONTRIBUTING.md).

## Modes and interaction

- **Safari approximation** adds page-colored floating controls and changes the content viewport with actual scrolling. Open offers single/multiple-tab variants; the switch illustrates shell geometry rather than managing real tabs.
- **Immersive App WebView** removes browser chrome and uses the full logical scene. Changing the outer outline does not remove native screen clipping.

The initial selection is Safari, Closed, body 0°, 1:1. The eight measured mappings determine the actual scene, including the rotation-history qualification at Closed 180°. macOS Fit scales the native coordinate space while preserving CSS dimensions; Windows Fit uses WebView2 zoom and needs native acceptance. Use the [scene matrix](005-SAFARI-SCENES.md) for viewport sizes, tab reservations and selected-tab geometry. A partial-fold perspective model is unavailable.

Back/Reload use native navigation; other illustrated widgets have limited demo interactions. The actual desktop UA, DPR, orientation, safe areas and keyboard remain native. A Safari-looking container does not make the engine iPhone Safari.

## Drawing below floating chrome

On the validated macOS 27 runtime, Closed mode keeps a full-size WKWebView and sets public `obscuredContentInsets`. A 466 × 678 native frame with side 84 / bottom 98 insets has a 382 × 580 page viewport; compact bottom 80 yields 382 × 598. Normal document flow continues below the address bar, while fixed-bottom elements stay within the smaller viewport. The 270° scene moves the side inset to the left.

`minimumViewportInset` / `maximumViewportInset` describe the bottom 80…98 range: the independent probe measured 100svh = 580, 100lvh = 598, and 100dvh = 580/598. Immersive mode clears these insets. Fit scales presentation without changing those CSS dimensions; state records distinguish actual viewport, drawing frame, DIP and obscured insets.

Open and Windows still reserve smaller native content frames. Open's top-chrome continuation is a derived-color approximation; an equivalent Windows under-chrome viewport implementation has not been established. On the tested WK runtime, `takeSnapshot` omits continuation visible in the actual window. Inspect the presented window as well as snapshots. The archived [inset probe](../../scripts/archive/README.md) and original local `captures/desktop-validation/under-chrome/` records document that distinction.

API references: [WKWebView obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets), [WebKit viewport support](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/).

## Material and diagnostics

macOS composes WKWebView and sibling AppKit surfaces beneath one Bezier mask, avoiding ordinary Avalonia overlays above native browser children. The current macOS 27 package uses `NSGlassEffectView` clear/tint. Its older `NSVisualEffectView` branch requires a lower-target rebuild and separate runtime validation. Tint, material contribution, shadow and radius are tunable; the private iOS Liquid Glass blur/refraction kernel is unknown.

Theme-color takes priority; otherwise macOS samples a rendered WK snapshot edge strip, with computed background as a labeled fallback. Sampling is asynchronous and can lag moving content. Windows observes scroll/theme and samples preview-edge color, then draws WinForms/GDI tint and alpha. Native Windows backdrop blur is not implemented; runtime input, DPI and appearance remain pending.

**Color demo** is a separate fixture for theme hints and live content sampling. **Save state** and `window.devduoContainerTelemetry` expose requested/actual frames, scroll/chrome state, visible versus hit bounds, tint source/RGB and material parameters. Checkout state files go to `captures/desktop-validation/container-modes/`; installed apps use local application data under `DevDuo/captures`. `DEVDUO_OUTPUT_DIR` overrides that location.

For measurement semantics and provenance, use the [measurement contract](002-MEASUREMENT-CONTRACT.md). For acceptance and recorded platform limits, use [calibration and validation](003-CALIBRATION-WORKFLOW.md). Future host controls remain separate from measured Safari geometry; see the [roadmap](100-ROADMAP.md).

[English](001-ARCHITECTURE.md) | [中文](../zh/001-ARCHITECTURE.md)
