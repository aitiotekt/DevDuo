# Measurement contract and reference data

Read this before changing calibrated geometry or collecting references. A public checkout runs from committed configuration without private captures; native builds still require the platform SDK. Simulator measurements do not establish physical hardware specifications.

## Browser and desktop records

Browser records use `schemaVersion: 1`. The receiver writes individual `UUID.json` files and appends `records.jsonl`, assigning an ID and UTC `receivedAt`; the browser supplies `clientTimestamp` and `reason`, with events debounced 350 ms. Device/pose/browser/manual state are labels, while `observedState` derives from actual scrollY. Keep a capture's explicit record UUID/PNG association; nearest timestamp alone is insufficient.

| Field | Meaning |
| --- | --- |
| `screen` | Actual browser CSS screen coordinates, orientation and color depth; DPR is not physical density |
| `viewport` | Inner/client/outer dimensions, document extent, scroll and visual viewport bounds/scale |
| `safeAreaCssPx` | Computed `env(safe-area-inset-*)` with viewport-fit=cover; zero does not establish hardware corners or absence of system insets |
| `components` | DOM layout-viewport and document coordinates; native browser chrome requires separate XCUI frames or image measurements |
| `environment` | Actual UA, platform, URL, preferences, locale and timezone |
| `unknown` | Hardware millimeters/radii and Liquid Glass parameters stay null without an independent authoritative source |

The calibration page draws no Safari chrome. Desktop records retain `macOS-WKWebView` / `Windows-WebView2` and `safari-shell-approx` / `app-webview` labels. Their screen APIs, DPR and safe areas remain native; they must not be labeled actual iPhone Safari. Desktop-only `window.devduoContainerTelemetry` and **Save state** record requested scene, actual viewport/native frame, scroll/chrome state, separate optical/hit bounds, tint source and material parameters. DOM observations and native snapshots are asynchronous, not atomic. Rendering/output behavior is described in [Architecture](001-ARCHITECTURE.md).

## Coordinates, curves and unknown physical values

Keep body angle, scene angle, CSS pixels, native points, rendered DIP and framebuffer pixels separate. The recorded simulator capabilities include 1398 × 2034 and 2007 × 2853 framebuffers at scale 3; display association must be verified in Device Hub. Screen-mask canvases are 466 × 678 and 1252 × 1780, with the second independently scaled rather than treated as screen CSS coordinates.

Outer artwork is independent of the screen path, not `outerRadius = innerRadius + bezel`:

| Source artwork | Canvas | Screen and offset in artwork units |
| --- | --- | --- |
| phone15 composite | 514 × 706 | 466 × 678 at (25, 14), including the asymmetric hinge |
| phone14 composite | 660 × 924 | 626 × 890 at (17, 17), with the source curve and 34-unit outline |

Corner metadata 8/59 and 55 has no established unit and is not the continuous Bezier transition extent. Preserve it without substituting CSS border-radius. Likewise, the simulator's 460 hdpi/vdpi fields cannot turn screenshot pixels into certified millimeters. Display diagonal, physical body/bezel sizes, fold angle, glass curvature and real hardware radii remain parameterized unknowns.

AX hit boxes, text boxes and visible glass are distinct. The Open compact address hit frame is 252 × 30.8. In the recorded 2853 × 2007 localhost PNG, estimated optical edges x1323…1531 / y71…133 imply about 69.3 × 20.7 CSS px, with ±2 image pixels of soft-edge ambiguity; hostname length changes width. Closed compact exposes no corresponding AX address field, so its adjustable hit target is not a measured native rectangle. Whole-tab versus title-hit bounds are specified in the [scene matrix](005-SAFARI-SCENES.md).

## Public configuration and provenance

| File | Responsibility |
| --- | --- |
| `config/device-profiles.json` | Independent screen/exterior paths and canvas-to-screen mapping |
| `config/safari-scenes.json` | Eight body/fold mappings and 24 initial/scrolled Safari states, including Open single/multiple tabs |
| `config/safari-shell.json` | Editable layout/material defaults, with estimates distinguished from interaction frames |
| `config/physical-parameters.json` | Explicit null physical dimensions and hardware radii |
| `config/measurement-matrix.json` | Intended collection plan, not proof of completed coverage |

The scene cohort is an Xcode 27.1 / iOS 27.1 iPhone Duo simulator observation. Runtime, Safari version, tab count and rotation history can change results. Logical portrait screens are Closed 466 × 678 and Open 669 × 951; the [scene matrix](005-SAFARI-SCENES.md) records actual body correspondence.

Public records contain source-relative evidence names and SHA-256 hashes; originals are absent from Git. A hash identifies a file without making it available or certifying hardware. Preserve original bytes, hashes, frozen sources and failed attempts in local evidence. Share only separately reviewed derivatives; keep private paths, simulator identifiers and browsing content out of public files. See [Privacy](../../PRIVACY.md) and [Third-party notices](../../THIRD_PARTY_NOTICES.md).

Historical extraction and full desktop/tab verification require the original local cohort. Their absence is an evidence limitation, not an app dependency failure. The [archive guide](../../scripts/archive/README.md) owns reproduction arguments and each tool's assumptions; generate into `temp/`, inspect against original frames/telemetry, and only then replace accepted configuration. New observations follow [Calibration and validation](003-CALIBRATION-WORKFLOW.md).

[English](002-MEASUREMENT-CONTRACT.md) | [中文](../zh/002-MEASUREMENT-CONTRACT.md)
