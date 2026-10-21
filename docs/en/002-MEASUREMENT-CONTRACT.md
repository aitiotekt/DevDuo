# Measurement contract

Each browser record is JSON schemaVersion 1. The POST receiver writes an individual UUID.json and appends records.jsonl under the chosen output directory. It adds id and receivedAt in UTC. The browser supplies clientTimestamp and reason; event records are debounced 350 ms. Device, pose, browser and manually chosen state are labels from the URL/UI, not automatic proof. observedState derives from actual scrollY. Capture metadata links the record UUID to a PNG. Keep both files; do not associate captures by nearest timestamp without checking.

- screen: browser-reported CSS screen coordinates, orientation and color depth. DPR is a browser ratio, not physical density.
- viewport: inner/client/outer dimensions, document extent, scrollX/Y, visual viewport bounds/scale. The visual viewport is affected by browser controls, keyboard and zoom.
- safeAreaCssPx: computed env(safe-area-inset-*) in this page with viewport-fit=cover. A zero value means this browser page reported zero; it does not establish a square hardware edge or absence of system insets.
- components: getBoundingClientRect values in layout viewport CSS coordinates plus document coordinates. Browser native chrome needs XCUI frames or screenshot measurements recorded separately.
- environment: user agent, OS platform, URL, media preferences, locale and timezone. Desktop WKWebView records must never be labeled iPhone Safari.
- unknown: physical dimensions in mm, real hardware corner radius and Liquid Glass implementation parameters remain null unless obtained from a separate authoritative source.

The calibration page itself contains no Safari toolbar or material imitation. The desktop host may wrap it in the explicitly labeled Safari approximation container; that host chrome is separate from the page DOM and never counted as real iPhone Safari. Observe actual Safari native controls in initial/scroll-down/scroll-up/keyboard states. Capture repeated frames/video if describing material motion: one screenshot only proves one sampled appearance. A simulator rendering does not establish behavior on a physical device.

## Coordinates and curve provenance

The local device type capabilities include 1398×2034 and 2007×2853 framebuffers with scale 3. Their primary display association must be checked in Device Hub. The first mask canvas is 466×678. The second is 1252×1780 and is scaled to fit; it is not the screen's CSS coordinate system. Corner metadata 8/59 and 55 has no established unit and does not directly equal the continuous Bezier transition extents. Preserve the raw values. Do not substitute border-radius using those numbers.

The shell uses an independently extracted outer path, not `outerRadius = innerRadius + bezel`. phone15 composite is 514×706 with screen 466×678 offset (25,14), including an asymmetric hinge. phone14 composite is 660×924 with screen 626×890 offset (17,17); its body is the measured source screen curve with a 34-unit outline. The historical `scripts/archive/2026-10-21/extract_geometry.py` can reproduce `config/device-profiles.json` from saved original artwork; see [Reference data](007-REFERENCE-DATA.md). macOS clips the actual WKWebView backing layer with the Bezier mask; Windows clips the native parent region with the same path. Avalonia drawing clip alone cannot reliably clip a hosted native view.

The 460 hdpi/vdpi profile fields are simulator metadata. Do not derive or present millimeter dimensions from screenshot pixels divided by 460. Device Hub artwork and framebuffer sizes do not certify display diagonal, body dimensions, fold angle, glass curvature or real hardware corner radius. Those values remain parameters with an unknown status. Outer shell artwork units are for faithful simulator proportions only.


## Desktop container labels

Desktop browser records retain `labels.browser=macOS-WKWebView` or `Windows-WebView2`. `labels.displayMode` distinguishes `safari-shell-approx` and `app-webview`; URL notes retain requested screen rotation and 1:1/scaled preview. Source screen fields remain the real desktop screen API. The host never injects iPhone DPR or hardware safe-area claims.

The injected desktop-only observer exposes `window.devduoContainerTelemetry`; **Save state** writes the same native container state under `captures/desktop-validation/container-modes/`. It records the requested scene, actual page viewport and native view frame, chrome state, page scrollY, optical address surface separately from its hit frame, color source, material backend and tunable parameters. These are desktop reproduction records, independent of canonical Safari telemetry. Meta theme-color takes priority; macOS otherwise samples an actual WKWebView snapshot near the chrome edge, with computed style only as a labeled fallback. DOM observation and snapshot capture are asynchronous.

AX hit boxes, text boxes and visible glass boundaries are different measurements. The open compact address AX frame252×30.8 is not the optical pill. The current localhost canonical PNG yields an estimated69.3×20.7 CSS optical surface with about ±2 image pixels of soft-edge ambiguity; host text changes the width. Configuration keeps that approximation separate. Closed compact has no exposed AX address field in these samples, so its adjustable hit target is not presented as measured.

[English](002-MEASUREMENT-CONTRACT.md) | [中文](../zh/002-MEASUREMENT-CONTRACT.md)
