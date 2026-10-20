# Host UI roadmap

The desktop host is a calibration workbench around the measured device preview: a real address bar, a grouped settings sidebar and a status bar, themed with SukiUI on Avalonia 12. Host improvements must keep the measured device viewport and Safari shell unchanged. Persisted English/Simplified Chinese localization covers every host control.

## Delivered on macOS

- **Browser controls.** The host address bar is bound to the active native WebView: committed URL after redirects, page title, loading progress, failure state with Retry, Enter to navigate, back/forward, reload/stop and inline validation. Query strings and fragments are kept. Measurement labels apply only to the local calibration page; arbitrary developer URLs are opened unchanged.
- **Shared navigation state.** WKWebView and WebView2 report one `NavigationState`; stale callbacks from destroyed containers are ignored. Presentation changes (outline, scale, language, theme, tab strip, material) keep the loaded page; fold, body angle and container mode still recreate the native view at the current page, and the status bar says so. See [Architecture](001-ARCHITECTURE.md).
- **Grouped settings and persistence.** Device, Container, Preview and Appearance groups, with collapsible material tuning and diagnostics. Inapplicable options are disabled with an explanation. Host settings except the body angle persist in `preferences.json`; **Reset settings** restores Safari · Closed · body 0° · 1:1.
- **Visual and interaction design.** SukiUI control themes plus DevDuo tokens for typography, spacing, icons, focus/hover/pressed/disabled states and light/dark themes. The preview is the visual focus; detailed metrics moved to Diagnostics and the status bar shows actionable state. Fit follows the available preview area.
- **Keyboard and accessibility.** Shortcuts for address focus, reload, stop, history, save state and the settings panel (also in the macOS **Navigate** menu); localized automation names, help text for disabled options, shortcut tooltips and a live status region.

These items were exercised against an actual macOS WKWebView (scripted controls, native telemetry and rendered host UI; see [Calibration and validation](003-CALIBRATION-WORKFLOW.md)). Interactive keyboard, VoiceOver and popup-over-native observation remain to be recorded.

## Remaining work

- Windows native acceptance of the same host UI, including WebView2 navigation events, accelerator keys while WebView2 has focus, DPI scaling and SukiUI rendering: follow [Windows handoff](009-WINDOWS-HANDOFF.md).
- Interactive macOS checks of shortcuts with WKWebView focus, VoiceOver reading order, popup placement over the native child and the SukiWindow title bar.
- Optional: keep the page across fold/angle/mode changes by reconfiguring the mask in place. This needs new native evidence that viewport, insets and Closed 180° behavior match a freshly created container.

## Acceptance boundaries

The host address bar and the simulated Safari address surface have different responsibilities. The latter belongs to the measured device scene; it keeps its calibrated frame, floating behavior and page-derived appearance. The multi-tab switch represents Safari shell variants and does not imply a working tab manager.

Host UI refinements must preserve both container modes, the eight body/fold mappings, Open single/multiple-tab variants, scroll transitions, independent inner/outer paths, and actual CSS viewport dimensions. A changed host panel may affect available preview space; it must not silently change logical device dimensions. Check before/after state records at the same settings. Do not place ordinary Avalonia controls over native browser children.

Treat iOS Safari widgets, native glass material, hardware simulation and device geometry as separate evidence-dependent work. Do not use a general visual redesign to replace measured Safari frame references or claim optical equivalence.

[English](100-ROADMAP.md) | [中文](../zh/100-ROADMAP.md)
