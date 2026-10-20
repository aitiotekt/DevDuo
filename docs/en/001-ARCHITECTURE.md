# Architecture and browser containers

Avalonia owns the host window and shared device/scene selection. `NativeWebView` hosts a system WKWebView on macOS or WebView2 on Windows. Platform renderers compose browser content and Safari illustrations inside an independent screen mask; exterior artwork can be toggled without removing that mask.

## Code map

| Layer | Source | Responsibility |
| --- | --- | --- |
| Host | `src/DevDuo/MainWindow.cs`, `NativeWebView.cs`, `NavigationState.cs`, `CalibrationUrl.cs` | Scene/option selection, navigation binding, calibration labels, preview sizing, saved state and native lifetime |
| Host UI | `HostAddressBar.cs`, `HostSettingsPanel.cs`, `HostStatusBar.cs`, `HostControls.cs`, `App.axaml`, `Styles/DevDuoStyles.axaml`, `PreferenceStore.cs` | SukiUI theme, design tokens, grouped controls, status, shortcuts and persisted host settings |
| Scene model | `config/`, `DeviceProfile.cs`, `SafariScene.cs`, `SafariContainerOptions.cs` | Paths, body/scene mapping, widget/tab frames and presentation options |
| macOS | `src/DevDuo/macos/WebViewBridge.swift` | WKWebView, native glass, clipping, obscured insets and color sampling |
| Windows | `src/DevDuo/WindowsWebViewPanel.cs` | WebView2, native region clipping, GDI composition and zoom-based Fit |
| Observation | `src/DevDuo/Fixtures/browser-observer.js` | Actual desktop viewport, scroll, theme, title and hostname |
| Calibration | `web/`, `scripts/python/`, `scripts/swift/`, `scripts/DeviceHubUITests/` | Browser records, actual Safari frames, captures and comparisons |

The receiver serves fixed calibration assets and writes local JSON; it does not control the app or execute page-supplied commands. Command ownership and setup are in [Contributing](../../CONTRIBUTING.md).

## Modes and interaction

- **Safari approximation** adds page-colored floating controls and changes the content viewport with actual scrolling. Open offers single/multiple-tab variants; the switch illustrates shell geometry rather than managing real tabs.
- **Immersive App WebView** removes browser chrome and uses the full logical scene. Changing the outer outline does not remove native screen clipping.

A fresh profile starts with Safari, Closed, body 0°, 1:1; saved host settings restore other choices, but every launch starts at body 0°. The eight measured mappings determine the actual scene, including the rotation-history qualification at Closed 180°. Fit follows the available preview area and never exceeds 1:1. macOS Fit scales the native coordinate space while preserving CSS dimensions; Windows Fit uses WebView2 zoom and needs native acceptance. Use the [scene matrix](005-SAFARI-SCENES.md) for viewport sizes, tab reservations and selected-tab geometry. A partial-fold perspective model is unavailable.

Back/Reload use native navigation; other illustrated widgets have limited demo interactions. The actual desktop UA, DPR, orientation, safe areas and keyboard remain native. A Safari-looking container does not make the engine iPhone Safari.

## Host controls and navigation

SukiUI 7 on Avalonia 12 supplies the host control theme. `Styles/DevDuoStyles.axaml` adds light/dark tokens, spacing, typography, icons and component classes. These styles apply only to Avalonia host controls; the device outline, screen mask, Safari widgets, tab text and viewport reservations remain with the platform renderers and measured configuration. Host surfaces are opaque and occupy separate grid cells. SukiUI toasts, dialogs and other in-window overlays are not used, because Avalonia content cannot draw above a native browser child. Avalonia moves native children but does not clip them to scrolling ancestors, so the native view is not inside the preview scroll viewer: a transparent slot scrolls with the outline, and the native view sits in a layer above it, sized to the slot's visible part (scroll bars stay uncovered). A native clip view (`devduo_set_viewport` on macOS, a parent window on Windows) offsets the full-size container inside it, so the container frame, scene scale, CSS viewport and telemetry do not change while scrolled.

The host address bar is bound to the active native view. macOS observes WKWebView `url`, `title`, `isLoading`, `estimatedProgress`, history flags and navigation failures, then calls back through `devduo_set_navigation_callback`. Windows uses WebView2 `NavigationStarting`, `SourceChanged`, `DocumentTitleChanged`, `HistoryChanged` and `NavigationCompleted`. Callbacks are posted to the UI thread and ignored once their native container is destroyed; cancelled or superseded loads are not failures. Addresses must be http/https. A missing scheme becomes `http://` for loopback, `.local` and private-network hosts and `https://` otherwise; other schemes are rejected. Query strings and fragments are kept. Measurement labels (`device`, `pose`, `browser`, `state`, `displayMode`, `notes`) are added only to the local receiver page (loopback, port 8765, path `/`), replace earlier labels and are hidden in the address bar.

| Change | Native view |
| --- | --- |
| Language, theme, multi-tab strip, material tuning, outline | Reconfigured or moved in place; page, history and scroll are kept |
| Preview scale | Resized in place; only the calibration page reloads so its `notes` label matches |
| Fold, body angle, container mode | Recreated at the current page or color laboratory; scroll and history restart and the status bar says so |

Shortcuts: ⌘L/Ctrl+L focuses the address, ⌘R/Ctrl+R or F5 reloads, Esc stops loading, ⌘[ ⌘] or Alt+←/→ move through history, ⌘S/Ctrl+S saves state and ⌘\\/Ctrl+\\ toggles settings. macOS also lists them in a native **Navigate** menu so they work while WKWebView has focus. Windows forwards WebView2 accelerator keys through the panel's command-key chain, which needs native acceptance. Host controls carry localized automation names, tooltips with shortcuts and help text for disabled options; the status message is a polite live region.

`PreferenceStore.cs` keeps host settings in the optional `host` object of `DevDuo/preferences.json`: `theme`, `containerMode`, `fold`, `previewScale`, `deviceOutline`, `showTabStrip`, the four material values (null keeps `config/safari-shell.json`), panel expansion and `lastUrl` (http/https only). Fields are validated separately; an invalid field uses its default and is reported. Body angle is never stored, so Closed 180° is reached only through the recorded clockwise sequence. `DEVDUO_URL` overrides `lastUrl`. **Reset settings** restores the defaults and body 0° without changing language or the current page. File format and language behavior are in [Localization](011-LOCALIZATION.md).

## Drawing below floating chrome

On the validated macOS 27 runtime, Closed mode keeps a full-size WKWebView and sets public `obscuredContentInsets`. A 466 × 678 native frame with side 84 / bottom 98 insets has a 382 × 580 page viewport; compact bottom 80 yields 382 × 598. Normal document flow continues below the address bar, while fixed-bottom elements stay within the smaller viewport. The 270° scene moves the side inset to the left.

`minimumViewportInset` / `maximumViewportInset` describe the bottom 80…98 range: the independent probe measured 100svh = 580, 100lvh = 598, and 100dvh = 580/598. Immersive mode clears these insets. Fit scales presentation without changing those CSS dimensions; state records distinguish actual viewport, drawing frame, DIP and obscured insets.

Open and Windows still reserve smaller native content frames. Open's top-chrome continuation is a derived-color approximation; an equivalent Windows under-chrome viewport implementation has not been established. On the tested WK runtime, `takeSnapshot` omits continuation visible in the actual window. Inspect the presented window as well as snapshots. The archived [inset probe](../../scripts/archive/README.md) and original local `captures/desktop-validation/under-chrome/` records document that distinction.

API references: [WKWebView obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets), [WebKit viewport support](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/).

WKWebView retains its native canvas/under-page background drawing; the host does not force transparency or override its resolved html/body background. Closed mode adds a passive AppKit color surface within the bottom inset only for a visible, opaque, solid, full-width fixed footer detected by read-only DOM observations. Narrow markers and absent footers clear it. This is an explicit approximation of Safari's native color extension: gradients, transparent surfaces, sticky elements and elements excluded from hit testing are not generalized. It changes no DOM style, viewport reservation, fixed frame or scroll range. `fixedBottomColorExtension` reports its visibility and frame.

Native placement is synchronized at Avalonia's `Loaded` dispatcher priority after layout, including the clip/content configuration. The AppKit containers disable implicit subview resizing and relayout when their frame size changes. This keeps screen content aligned with exterior artwork at startup, resize and Fit; `nativeClipGeometry` records the full container, logical bounds and clip offset. See [acceptance](003-CALIBRATION-WORKFLOW.md).

## Material and diagnostics

macOS composes WKWebView and sibling AppKit surfaces beneath one Bezier mask, avoiding ordinary Avalonia overlays above native browser children. The current macOS 27 package uses `NSGlassEffectView` clear/tint. Its older `NSVisualEffectView` branch requires a lower-target rebuild and separate runtime validation. Tint, material contribution, shadow and radius are tunable; the private iOS Liquid Glass blur/refraction kernel is unknown.

Theme-color takes priority; otherwise macOS samples a rendered WK snapshot edge strip, with computed background as a labeled fallback. Sampling is asynchronous and can lag moving content. Windows observes scroll/theme and samples preview-edge color, then draws WinForms/GDI tint and alpha. Native Windows backdrop blur is not implemented; runtime input, DPI and appearance remain pending.

**Color demo** is a separate fixture for theme hints and live content sampling. **Save state** and `window.devduoContainerTelemetry` expose requested/actual frames, scroll/chrome state, visible versus hit bounds, tint source/RGB and material parameters. `previewScale` records the actual presentation scale. Checkout state files go to `captures/desktop-validation/container-modes/`; installed apps use local application data under `DevDuo/captures`. `DEVDUO_OUTPUT_DIR` overrides that location.

For measurement semantics and provenance, use the [measurement contract](002-MEASUREMENT-CONTRACT.md). For acceptance and recorded platform limits, use [calibration and validation](003-CALIBRATION-WORKFLOW.md). Future host controls remain separate from measured Safari geometry; see the [roadmap](100-ROADMAP.md).

[English](001-ARCHITECTURE.md) | [中文](../zh/001-ARCHITECTURE.md)
