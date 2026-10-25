# Platform capabilities and macOS acceptance

The [Safari scene matrix](005-SAFARI-SCENES.md) is the reference for both platforms. Windows uses the pinned WebKit Windows Port; macOS uses system WKWebView. Shared code and passing tests on one platform cannot certify another platform's native behavior.

## Current acceptance status

The maintainer confirmed macOS manual acceptance of the beta.3 candidate on 2026-10-28. Case-level receipts remain with that run; this Windows documentation work does not add new macOS passes. Later affected native changes still require scoped verification.

Windows uses milestone [`windows-desktop/2026-10-29`](009-WINDOWS-HANDOFF.md#current-status), with qualified runtime/host evidence and outstanding dependency/package delivery gates. Record implementation, compilation, native checks and unsupported behavior separately. Builds, signatures and shared tests do not establish optical or input parity.

## Current implementation and differences

| Area | Windows | macOS |
| --- | --- | --- |
| Engine/runtime | Source-built WebKit plus required host API manifest | System WKWebView; record actual OS/WebKit/SDK capabilities, without copying the Windows runtime or changing its locks |
| Website storage | Persistent store; absolute `DEVDUO_WEBKIT_PROFILE` isolates tests | Default persistent website store, no corresponding profile override. Preferences isolation does not isolate website data |
| Presentation | Full drawing frame, four-side obscured insets and independent backing scale | Closed side layout uses supported system insets; Open top continuation remains a reserved-frame/color approximation |
| Presentation changes | Rotation, language, Fit, outline, appearance and shell changes keep the view/document; fold/mode replaces it | Same ownership contract; native points/backing pixels must remain distinct from CSS and scene coordinates |
| Mobile identity/titles | Native compatibility UA before first request; document-confirmed titles | Same native UA and title policy through Swift callbacks/KVO; both containers share the observed identity |
| Local Inspector | Separate native Inspector, detached before showing | Capability-checked private selectors and hidden attachment target avoid asynchronous docking shrinking the measured viewport |
| Native remote inspection | Explicit unauthenticated WebKit TCP listener | System Safari Develop discovery through `isInspectable`; Windows listener address/port fields do not configure it |
| CDP/chii | Token-protected page bridge; separate system WebView2 frontend | Same page bridge; separate nonpersistent tools WKWebView. No engine-debugger/full-CDP compatibility claim |
| Proxy | System/direct/custom native HTTP/HTTPS/SOCKS5 | System/custom via supported website-store configuration; direct rejected. Default store is shared across measured views; neither platform supplies authenticated proxies |
| Fixed-container colors | Native four-edge API plus host fills | Capability-checked system extension; otherwise limited opaque full-width footer/root fallback, which cannot certify translucent masks |
| Scroll indicators | Native eligibility/input plus a passive scene-edge thumb | No matching host policy; system WebKit scrollbars/preferences remain authoritative |
| Glass and accessibility | DirectComposition approximation; scoped WebCore AX/UIA and Narrator support | System AppKit glass and native WKWebView accessibility; capability and assistive-technology coverage remain platform-specific |

`src/DevDuo/macos/WebViewBridge.swift`, `DeveloperWebViewBridge.swift` and `ScreenshotBridge.swift` own native macOS behavior. Shared host/browser/scene logic lives under `Host/`, `Browser/`, `Device/` and `DeveloperTools/`. Exact Windows API and unsupported capabilities belong in the [backend guide](014-WINDOWS-WEBKIT.md).

## Prepare a macOS run

Use [Contributing](../../CONTRIBUTING.md) for mise, Xcode/macOS SDK, real symlinks and build commands. `just test-macos` exercises AppKit layout, input policy, private-capability/fallback and Inspector attachment behavior. It is not end-to-end website or final-window acceptance.

Select the changed component below rather than restarting every scenario. Preserve existing receipts for unaffected behavior. Use isolated host preferences/output and a unique test origin/token; do not clear unrelated website data. `DEVDUO_WEBKIT_PROFILE` does not isolate macOS storage.

`just serve` supplies calibration on loopback port 8765. Serve reviewed standalone pages separately when needed:

```sh
mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages
```

The static server cannot verify request headers, redirects or proxy routing; those need a controlled recorder. Keep temporary recorder code and receipts local, and preserve provenance.

## Native acceptance matrix

Apply relevant rows to the exact candidate. For geometry changes, include both containers, all affected body mappings and initial/compact/tab variants; preserve Closed 180° history and verify observed orientation. Routine unrelated edits do not require the full matrix or display reconfiguration.

| Changed area | Observable requirements |
| --- | --- |
| Navigation, identity and titles | Offline welcome/Home, redirects/history/error handling, native first-document/subresource/fetch UA and dynamic/empty/long titles; no provisional title leakage |
| Storage and services | Same-origin tokenized localStorage/IndexedDB/persistent cookies survive normal restart; tools storage stays separate; services restart disabled |
| Scene and presentation | Presentation changes retain URL, document/time origin, form/history and valid scroll; record clamps rather than restoring by script. Check independent masks, complete tab containers and native frames at affected Fit values |
| Inspector and shortcuts | Actual separate DOM/Console, F12/Ctrl+Shift+I with native page focus, no docking viewport reservation, explicit missing-capability errors and no stale frontend |
| Remote/CDP/proxy | Actual Safari discovery or page bridge, target reconnect, listener cleanup, mutual exclusion and future traffic routing without reloading the current page. Do not bypass Console paste protection or claim trusted HTTPS from a failed TLS handshake |
| Continuation, alpha and material | Static and animated real flow, document boundaries, masks/backdrops and returned native colors; distinguish Open approximation and system/fallback capabilities. Compare actual windows as well as DOM/snapshots |
| Input, lifecycle and access | Transformed clicks, keyboard/IME, popups, VoiceOver, relevant resize/close/replacement races and resource cleanup; no callbacks into disposed containers |

For overlay/indicator fixtures, use their authored controls or test APIs one operation at a time and wait for the fixture's settled state. These inputs must not become production DOM workarounds. Viewport alpha, inset solid color and glass-covered pixels require distinct observations. System preferences, native indicator eligibility and the missing macOS scene-edge policy remain explicit.

## Decisions and lessons

- Native child logical sizes must be exact scene units; scale the ancestor for Fit. Floating-point size truncation must not change the CSS viewport.
- Inspector connection and docking restoration are asynchronous. Reserve a hidden attachment target and verify the visible detached frontend without shrinking the device view.
- A populated tools DOM can still paint blank if native layout/orientation is wrong. Inspect the actual tools window, not only successful protocol evaluation.
- Private SPI must be checked at runtime. Exercising a forced fallback branch proves its geometry/lifecycle, not system-extension pixels or another OS's capabilities.

## Evidence and acceptance report

Keep original state/window captures, runtime/capability branches and source/binary provenance in a new ignored evidence session. Report the changed scope, tests actually run, baseline inherited and remaining limits. macOS points, backing pixels, CSS viewports and native window captures are distinct evidence; Windows DPI or source-raster results do not substitute for them.

[English](015-PLATFORM-PARITY.md) | [中文](../zh/015-PLATFORM-PARITY.md)
