# Host UI roadmap

The desktop host is a calibration workbench around the measured device preview: a real address bar, a grouped settings sidebar and a status bar, themed with SukiUI on Avalonia 12. Host improvements must keep the measured device viewport and Safari shell unchanged. Persisted English/Simplified Chinese localization covers every host control.

## Implemented host behavior

- **Browser controls.** The host address bar is bound to the active native WebView: committed URL after redirects, page title, loading progress, failure state with Retry, Enter to navigate, back/forward, reload/stop and inline validation. Query strings and fragments are kept. Measurement labels apply only to the local calibration page; arbitrary developer URLs are opened unchanged.
- **Shared navigation state.** WKWebView and Windows WebKit report one `NavigationState`; stale callbacks from destroyed containers are ignored. Presentation changes (outline, scale, language, theme, tab strip, material) keep the loaded page. Body rotation updates the current native view; fold and container mode recreate it at the current page, and the status bar says so. See [Architecture](001-ARCHITECTURE.md).
- **Grouped settings and persistence.** Developer tools comes first and starts collapsed for each window, followed by Device, Container, Preview and Appearance groups, with collapsible material tuning and diagnostics. Inapplicable options are disabled with an explanation. Host settings except body angle and Developer tools expansion persist in `preferences.json`; **Reset settings** restores Safari · Closed · body 0° · 1:1.
- **Visual and interaction design.** SukiUI control themes plus DevDuo tokens for typography, spacing, icons, focus/hover/pressed/disabled states and light/dark themes. The preview is the visual focus; detailed metrics moved to Diagnostics and the status bar shows actionable state. Fit follows the available preview area.
- **Keyboard and accessibility.** Shortcuts for address focus, reload, stop, history, save state and the settings panel (also in the macOS **Navigate** menu); localized automation names, help text for disabled options, shortcut tooltips and a live status region.

Current platform capabilities and acceptance are recorded in [Platform capabilities](015-PLATFORM-PARITY.md); subsequent changes verify only affected native behavior.

## Remaining work

- Reuse the accepted Windows baseline and verify changed native behavior or uncovered boundaries under [Windows handoff](009-WINDOWS-HANDOFF.md).
- Deliver and accept the rebuilt desktop API v1 engine/application pair after the scoped popup, Microsoft Pinyin and WebCore UIA repairs. Extend accessibility text navigation, surrounding-text/reconversion and broader assistive-technology compatibility separately; current Value/Invoke and Narrator label/value checks do not implement TextPattern or IAccessible2.
- Further host control and visual polish, with keyboard/accessibility and presented-window checks at each affected platform.
- Real tab management is separate future work; the current two-tab strip remains an illustration of measured shell variants.
- Optional: keep the page across fold/mode changes by reconfiguring the mask in place. Body rotation already preserves the native view; any broader preservation needs new native evidence that viewport, insets and Closed 180° behavior match the accepted scene.

## Acceptance boundaries

The host address bar and the simulated Safari address surface have different responsibilities. The latter belongs to the measured device scene; it keeps its calibrated frame, floating behavior and page-derived appearance. The multi-tab switch represents Safari shell variants and does not imply a working tab manager.

Host UI refinements must preserve both container modes, the eight body/fold mappings, Open single/multiple-tab variants, scroll transitions, independent inner/outer paths, and actual CSS viewport dimensions. A changed host panel may affect available preview space; it must not silently change logical device dimensions. Check before/after state records at the same settings. Do not place ordinary Avalonia controls over native browser children.

Treat iOS Safari widgets, native glass material, hardware simulation and device geometry as separate evidence-dependent work. Do not use a general visual redesign to replace measured Safari frame references or claim optical equivalence.

[English](100-ROADMAP.md) | [中文](../zh/100-ROADMAP.md)
