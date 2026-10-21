# Desktop container modes

The desktop demo offers two containers for the same system WKWebView on macOS or WebView2 on Windows. The screen geometry and device outline are shared; changing browser mode does not change the device selection or its mask.

- **Safari · approximation** adds page-colored floating controls and expands/contracts the content viewport as the actual page scrolls.
- **App WebView · immersive** removes browser chrome and uses the complete requested native viewport. The outer device artwork remains independently switchable; its switch does not remove the inner screen mask.

The default mode is Safari, Closed display, body0°, 1:1 CSS dimensions. The8 measured body/fold mappings determine the actual scene. On macOS Fit preview scales the native view coordinate space and preserves the CSS viewport. Open Safari exposes a multi-tab strip checkbox; all titles use one centered14-point scene font. See [the updated scene matrix](005-SAFARI-SCENES.md).

## Layout evidence

`config/safari-shell.json` contains editable CSS/logical layout parameters from the local canonical capture cohort in `captures/safari/2026-10-21/` and corresponding browser telemetry. It never contains millimeters.

| Screen-coordinate scene | Expanded content viewport | Compact content viewport |
|---|---|---|
| Closed466×678 |382×580|382×598|
| Closed678×466 |594×368|594×386|
| Open951×669 |951×543|951×618|
| Open669×951 |669×825|669×900|

The closed side area is84 logical units; expanded bottom reserve98 and compact reserve80. Open top reserve changes126→51. These match the refreshed formal cohort, where restarting Safari resolved an earlier fold-transition idle problem. An exploratory Closed portrait run retained580 while scrolling; that historical observation remains in its original records and does not override the refreshed configurable baseline.

The secondary profile's nativeRotation270 means body portrait and screen portrait are different. The current8-entry reference matrix chooses the actual screen for each body pose, including the recorded retained landscape at Closed180°. The demo has no measured partial-fold hinge-angle or perspective model; partial geometry remains a limitation.

The floating address has separate visible, text and hit geometry. The open compact AX box252×30.8 describes interaction, not glass. The current localhost optical edge was estimated near x1323..1531,y71..133 in the2853×2007 raw PNG: approximately69.3×20.7 CSS px, with soft edges around ±2 image pixels. The visible pill grows with hostname text. Closed compact optical bounds are an image estimate; its hit target is a usability parameter because no corresponding compact AX address was exposed. Defaults and provenance are explicit in the config.

## Native rendering and color

The macOS parent is an AppKit view containing WKWebView and sibling floating control surfaces. It does not attempt to draw Avalonia controls over a hosted native browser. The parent applies the actual screen Bezier mask, clipping both content and chrome.

The current compiled bridge has minimum macOS27.0 (confirmed from its Mach-O build version) and uses public AppKit `NSGlassEffectView` with clear style and tintColor. The source includes an availability branch for `NSVisualEffectView` on older systems, but that branch is not reachable in this packaged binary; using it requires a lower deployment target rebuild and separate runtime validation. Only this machine's macOS27 runtime was tested. Tint alpha, material blend, shadow and radius are adjustable. **Glass blend** changes the material contribution; AppKit does not expose its optical blur kernel here. The macOS material is real platform rendering, but its appearance is not the iOS Safari Liquid Glass implementation or a measured optical model. Closed mode now keeps a full-size native WKWebView and uses public obscuredContentInsets to preserve the smaller page viewport while ordinary content paints behind/below the address bar. Open mode still reserves a smaller native frame and uses a derived-color approximation; no exact top-chrome extension is claimed. See [the follow-up verification](006-UNDER-CHROME-RENDERING.md).

Desktop-only JavaScript observes scroll direction, actual viewport dimensions, hostname, theme-color and computed background. Theme-color has priority. Without it, macOS samples an actual `WKWebView.takeSnapshot` edge strip, including rendered gradients/images; computed background is an explicitly labeled fallback. Snapshot sampling is asynchronous and can lag a moving frame. The injected observer is separate from the calibration page. Original capture-time web source snapshots remain in ignored local evidence.

Windows retains WebView2 with both modes, shared observed layout, native-region clipping, live scroll/theme observation and a native preview edge-color sample. Floating controls use WinForms/GDI tint and alpha composition. **Native Windows backdrop blur is not implemented**, and material blend there controls a visual fallback. Windows was cross compiled on macOS; runtime rendering, DPI, input and transparency remain unverified on Windows.

Back and Reload use the native browser. Other illustrated widgets are limited demo interactions, not a full Safari tabs/bookmarks application. Neither mode spoofs an iPhone UA, DPR, safe area or keyboard. App mode's measurements are its real desktop browser measurements.

## Reproduce and inspect

Run the receiver, launch `mise exec -- just dev`, then choose the container mode. Scroll the page down/up to change chrome state. **Color demo** opens a separate local fixture with Ocean/Orchid theme hints and a content-sampling option. Material settings are under the expandable panel.

**Save state** writes requested scene, actual native/page viewport, scrollY, chrome state, optical/hit bounds, tint source/RGB and backend/settings under `captures/desktop-validation/container-modes/`. `window.devduoContainerTelemetry` exposes the native state inside the desktop page. Packaged apps outside a checkout use local application data under `DevDuo/captures`; `DEVDUO_OUTPUT_DIR` overrides the output folder. These records are kept separate from the real Safari matrix.

Build products and local logs belong under ignored `temp/`. The local Finder app is `temp/DevDuo.app`. GUI proof captures and state files are retained in `captures/desktop-validation/container-modes/`; their manifest states the actual image encoding. CUA captures are JPEG observations, rather than lossless simulator framebuffers. Use canonical explicit-display PNGs for exact simulator pixel comparisons.

Follow-up: [native closed-mode under-chrome rendering](006-UNDER-CHROME-RENDERING.md) now separates the full native drawing frame from the smaller page viewport. The earlier GUI manifest records its own prior build; new evidence is in captures/desktop-validation/under-chrome/.

[English](004-CONTAINER-MODES.md) | [中文](../zh/004-CONTAINER-MODES.md)
