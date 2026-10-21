# Host UI roadmap

The current application is a calibration workbench with basic selectors, a URL text box, switches and tuning sliders. Improve the desktop application around the device preview while preserving the measured device viewport and Safari shell.

The app now has persisted English/Simplified Chinese localization with live language switching. Keep both catalogs and accessibility labels complete as the host controls evolve; this does not complete the navigation or visual-design work below.

## First delivery: functional browser controls

- Replace the simple URL field with a real host address bar bound to the active native WebView. Show the committed URL after redirects, current title, loading and failure state; support Enter to navigate, back/forward, reload/stop and clear validation feedback. Preserve query strings and fragments. Do not append calibration labels to arbitrary developer URLs; apply labels only to the local measurement page.
- Add shared navigation-state/callback abstractions with WKWebView and WebView2 implementations. The present host has no complete navigation-history/loading binding and rebuilds the view for several selections. Preserve a loaded page where possible and make resets explicit.
- Present fold, orientation, container mode, preview scale, outline and Open tab-strip controls in coherent groups. Disable inapplicable options with a concise explanation; make selected values clear. Decide and document preference persistence and reset behavior.

The host address bar and the simulated Safari address surface have different responsibilities. The latter belongs to the measured device scene; it must retain its calibrated frame, floating behavior and page-derived appearance. The multi-tab switch represents Safari shell variants and does not imply a working tab manager.

## Visual and interaction design

Create consistent typography, spacing, button sizes, icons, focus/hover/pressed states and light/dark presentation. Use Avalonia resources and reusable controls rather than independent per-control styling. Make the preview the primary visual focus; put optional material tuning and detailed metrics in secondary panels. Host status text should explain actionable loading/error state without exposing implementation details throughout the main interface.

Support a responsive window layout, keyboard navigation, accessible labels/tooltips, sufficient contrast and useful shortcuts such as focus-address and reload. Keep controls usable at Windows display scaling and macOS preview scales. Do not place ordinary Avalonia controls over native browser children without confirming the platform composition model.

## Acceptance boundaries

Host UI refinements must preserve both container modes, the eight body/fold mappings, Open single/multiple-tab variants, scroll transitions, independent inner/outer paths, and actual CSS viewport dimensions. A changed host panel may affect available preview space; it must not silently change logical device dimensions. Check before/after state records at the same settings.

Treat iOS Safari widgets, native glass material, hardware simulation and device geometry as separate evidence-dependent work. Do not use a general visual redesign to replace measured Safari frame references or claim optical equivalence. Windows runtime acceptance remains the first platform-specific prerequisite; see [windows-handoff.md](009-WINDOWS-HANDOFF.md).

[English](100-ROADMAP.md) | [中文](../zh/100-ROADMAP.md)
