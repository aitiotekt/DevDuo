# Platform parity and macOS native handoff

This handoff tracks macOS integration of the Windows implementation, using the qualified records in [Windows handoff](009-WINDOWS-HANDOFF.md) and [Calibration](003-CALIBRATION-WORKFLOW.md). Scoped native results appear below; untested cells remain pending. The reference is the [accepted scene matrix](005-SAFARI-SCENES.md), not the Windows desktop rendering. Preserve system WKWebView on macOS, the production Windows WebKit Port, and Safari/immersive containers.

Record four different statuses: **implemented** (source exists), **compiled** (the exact revision built on that platform), **native checked** (the scoped interaction ran against that binary), and **pending/unsupported**. A shared test, Windows pass, older macOS build or simulator observation cannot fill a current macOS native cell. The older macOS layout/material/language observations in Calibration remain historical. The later developer-tools, identity/title and fixed-container Swift changes require current macOS compilation and presentation checks.

## Current implementation and differences

| Area and source | Windows | macOS and acceptance boundary |
| --- | --- | --- |
| Measured page engine: `windows/WindowsWebKitApi.cs`, `macos/WebViewBridge.swift` under `src/DevDuo/` | Pinned source runtime plus required host ABI and integrity manifest | System WKWebView; no Windows engine build/archive is needed. Record the actual OS, WebKit and SDK. System capabilities may differ. |
| Storage | Persistent `DevDuo/WebKit` store; absolute `DEVDUO_WEBKIT_PROFILE` can isolate tests | `WKWebViewConfiguration` retains its default persistent website store; no matching macOS profile override is implemented. Preferences isolation does not isolate website storage. Use a unique test origin/token and the same app identity for cross-process checks. |
| Mobile identity: `Browser/BrowserIdentity.cs` | Native custom UA before first request, with Windows header/redirect checks recorded | `customUserAgent` is set before loading. Verify first-request headers and `navigator.userAgent` on macOS. The exact observed iPhone Safari string includes the frozen `OS 18_7` token and `Version/27.0`; it is a compatibility identity, not evidence of the actual desktop engine, iOS version or physical hardware. Both containers share it; no separate App WebView identity was measured. |
| Titles/welcome: `Browser/PageTitleState.cs`, `Browser/WelcomePage.cs`, `macos/WebViewBridge.swift` | Document confirmation gates selected-tab titles; offline welcome and localized owned text | Swift separately gates titles on committed/observed document identity. Welcome localization updates owned nodes without navigation. Check dynamic/empty/long titles and delayed provisional loads; shared title tests do not exercise native KVO ordering. |
| Presentation: `Browser/NativeWebView.cs`, `Host/MainWindow.cs` | Rotation/configuration updates an existing browser | `devduo_update_scene`/`devduo_configure` update the existing container. Rotation, language, Fit, outline and appearance must preserve the document; scroll may clamp to a new valid range. Fold and container-mode changes intentionally recreate it and its history. |
| Continuation | Four-side production inset/painting path; scoped Windows results in 009/014 | Closed side layout uses full-view obscured insets on supported systems. Open top layout remains a reserved-frame approximation; it is not the same four-side engine path. Side/root fills and native materials must retain their approximation labels. |
| Local Inspector | Separate native frontend; Windows F12/Ctrl+Shift+I window observations recorded | Runtime-checked private Inspector/attachment selectors reserve a hidden attachment target, making docking unavailable without resizing the measured browser. A bounded wait checks actual visibility. Missing capability produces an explicit error. WKWebView handles F12/Ctrl+Shift+I directly as well as the host menu. |
| Native remote inspection (WIP) | Explicit unauthenticated WebKit Inspector TCP listener, separate from CDP | `isInspectable` (macOS 13.3+) exposes Safari Develop discovery. The host does not create the Windows TCP listener; its address/port/LAN fields do not configure Safari discovery. Verify local-Inspector conflicts and discovery removal separately. |
| CDP/chii: `DeveloperTools/`, `macos/DeveloperWebViewBridge.swift` | Page instrumentation plus separate system WebView2 frontend | Same token-protected page CDP service; frontend is a separate, nonpersistent system WKWebView with its own tools UA. It must not replace the measured page or share its storage. Actual chii compatibility remains to be checked. The JavaScript CDP subset is not engine debugging, profiling or complete network coverage. |
| Proxy | System, direct, unauthenticated custom HTTP/HTTPS/SOCKS5 | System/custom through `WKWebsiteDataStore.proxyConfigurations` on macOS 14+; direct is disabled in UI and explicitly rejected by the bridge. No authenticated proxy support. Apply before first load and verify later requests without reloading the existing document. The default store is shared across measured views. |
| Fixed/container colors | Native fixed-container color API plus host composition; scoped alpha/dialog checks recorded | Capability-checked system fixed-container extension is enabled when available, and competing side/footer host fills are removed. Otherwise the legacy fallback is only an eligible opaque full-width footer/root approximation. Translucent fixed layers and dialog backdrops are not certified by that fallback. |
| Scroll indicators | Native eligibility/CSS/input state plus host-painted transient screen-edge thumb | Telemetry explicitly reports `host-scene-policy-unavailable; native-acceptance-pending`. System WKWebView scrollbars and user system preferences remain unchanged; there is no matching host screen-edge policy. Do not mark parity from native scrolling alone. |

Source names in the table are relative to `src/DevDuo/`; other paths are repository-relative. Measured layout, system capabilities and actual observed pixels are separate inputs.

## Beta integration record: 2026-10-24

Source baseline: `43161d9e535ae1fafce80308c46c999a20b2c09e`, plus the uncommitted `0.1.0-beta.1` preparation diff. Private receipts, screenshots, recorder source and hashes are kept in `captures/desktop-validation/platform-parity/20261023T221413Z-macos/`. This session uses isolated host preferences and a controlled loopback origin, not an isolated website store. Environment: macOS 27.0.1 (26A434), Xcode 27.1, macOS SDK 27.0, system WebKit 22625.1.29.11.28, .NET SDK 10.0.402, arm64, backing scale 2.

| Scope | Result and limits |
| --- | --- |
| Build/check boundary | `just verify`, `just build-windows` and `just test-macos` passed. The self-contained macOS candidate built and opened with `DOTNET_ROOT` unset; loaded-library inspection confirmed its bundled CoreCLR. This was an unsigned local candidate, not a signing/notarization pass. Cross-compilation checks managed Windows sources without requiring a local C++ compositor; native Windows builds still build and bundle it. AppKit assertions now run in macOS CI. |
| M01/M02 | Offline welcome, real address entry and redirect navigation worked. In both Safari and immersive containers, first-request, redirect-destination, subresource and fetch headers matched `navigator.userAgent`. |
| M03 | Normal close/relaunch preserved test localStorage, IndexedDB and a separately authored persistent cookie at the same origin. Services restarted disabled and the developer section restarted collapsed. The tools configuration has an asserted ephemeral store; complete cross-store isolation acceptance remains pending. |
| M04 | All eight initial body mappings produced the expected viewport classes while retaining the document/form within each fold. Closed retained scrollY 400. Open also showed a 400-to-340 movement during the session; this is not a general scroll-preservation pass. The complete 24-state transition matrix remains pending. Open single-tab switching retained the document and changed its portrait-body viewport from 951 × 543 to 951 × 589. |
| M05 | Reproduced asynchronous Inspector docking shrinking the page to 951 × 169. `isConnected` becomes true before the backend restores docking. A hidden attachment target fixes that race; the actual separate Inspector window retained the 951 × 589 viewport and document identity. Both F12 and Ctrl+Shift+I subsequently opened it from the focused native page; AppKit assertions also cover their dispatch. |
| M07 | Native injection and token-protected CDP `Runtime.evaluate` round trips worked. The native tools window initially painted blank despite a populated DOM. An explicit flipped native layout host fixed its composited presentation: the actual offline frontend displayed DOM, styles and box model, and its Console evaluated `1+1` to `2`. Separate native layout assertions cover resize and the ephemeral store. Navigation/reconnect, complete protocol coverage and CSP/HTTPS cases remain pending. |
| M12/presentation | Reproduced 90.3446% Fit turning a requested 669-point height into 668.9999999999999 and a 668-CSS-pixel viewport. Native children now use exact scene units, with scaling confined to the ancestor. A new regression failed before the fix and passed at 100%, 90.3446%, 94%, 75% and 50%. The presented application retained 951 × 669 DOM/visual viewports at about 90% Fit. Switching Chinese to English also preserved document identity, viewport and scroll. Other display/DPI and input cases remain pending. |
| M09–M13 | This system reports the fixed-container extension unavailable. Legacy solid-footer/root filling remains an approximation; alpha/dialog extension is unsupported by that fallback. Open continuation, screen-edge indicators, trusted HTTPS/proxy routing, complete Fit/input/accessibility and lifecycle coverage are not certified by these checks. |

The Inspector fix follows WebKit's [asynchronous connection/docking implementation](https://github.com/WebKit/WebKit/blob/main/Source/WebKit/UIProcess/Inspector/WebInspectorUIProxy.cpp) and [macOS attachment eligibility](https://github.com/WebKit/WebKit/blob/main/Source/WebKit/UIProcess/Inspector/mac/WebInspectorUIProxyMac.mm). Private SPI capability checks are still required on each supported system version.

## Prepare the macOS run

Use a real unlocked macOS desktop, the repository-selected mise environment and Xcode/macOS 27 SDK prerequisites from [Contributing](../../CONTRIBUTING.md). Verify real file/directory symlinks and `core.symlinks=true`. Do not copy the Windows runtime into the macOS app or rewrite Windows locks.

```sh
mise install
mise exec -- just setup
mise exec -- just doctor
sw_vers
xcodebuild -version
xcrun --sdk macosx --show-sdk-version
mise exec -- just verify
mise exec -- just test-macos
```

Save command exits and full logs in the new local session. `verify` compiles the app bridge on macOS. `build-native-tests` compiles the native harness, including both Swift bridge files. macOS CI runs `test-macos`, a focused AppKit geometry/input-policy test, not end-to-end browser acceptance. It does not cover all developer tools, first-request UA, persistence, overlays or final display output. No macOS counterparts to the Windows-specific native browser harness modes currently exist; run the interactive cases below and add maintained automation only where needed.

`NativeLayoutTests.swift` checks whether the legacy footer surface defers to the actual system capability, then temporarily disables the capability flag to exercise fallback geometry, passive hit testing and removal before restoring it. It also checks the hidden Inspector attachment target and native shortcut dispatch. These assertions passed in the beta integration session; the forced fallback branch does not certify system extension pixels. Record the actual capability branch separately in the presented application.

For an isolated host-preference/evidence session, use a fresh terminal:

```sh
session_dir="$PWD/captures/desktop-validation/platform-parity/$(date -u +%Y%m%dT%H%M%SZ)-macos"
mkdir -p "$session_dir"
export DEVDUO_PREFERENCES_PATH="$session_dir/preferences.json"
export DEVDUO_OUTPUT_DIR="$session_dir/states"
unset DEVDUO_URL
mise exec -- just dev
```

This leaves the normal preferences untouched but still uses the default measured-page website store. Do not clear unrelated website data. Start each fixture server in a separate terminal:

```sh
mise exec -- just serve
mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages
```

The two commands are separate long-running servers: calibration on port 8765 and reviewed HTML fixtures on port 8878. Open `http://127.0.0.1:8878/fixed-overlay.html` or `scroll-indicators.html` through the real address bar. A custom request-header/redirect/proxy recorder, if needed, belongs in the OS temporary directory; freeze its source/hash in the evidence session. The static server alone does not verify UA headers, redirects or proxy routing.

## Native acceptance matrix

Run these against the exact built candidate. Use both Safari and immersive mode unless the case names Safari chrome. Within each fold, rotate through all four body angles without changing mode; mode/fold switches are separate lifecycle cases. Expand Safari coverage to all 24 initial/scrolled states, including Open single/multiple illustrative tabs. Keep the recorded Closed 180-degree rotation sequence; verify observed content orientation independently of the command.

| ID | Action | Required observation and evidence |
| --- | --- | --- |
| M01 Startup and owned pages | With fresh isolated preferences and no startup URL, launch offline; use Home, Color demo, Calibration, then restart. Switch English/简体中文/system language on the welcome page. | Offline welcome works without a receiver; repository/docs links are correct. Calibration alone needs its server and measurement labels. Welcome strings/title update in place; URL, document identity/time origin and any scroll remain. Remembered website startup works separately. Developer tools starts collapsed in every window, stays expanded within that window across language changes, and its color/calibration actions remain last. |
| M02 Native UA and titles | Record headers at the first HTTP request, redirect, subresource and fetch; compare `navigator.userAgent` with `BrowserIdentity.MobileUserAgent`. Test delayed loads, dynamic/empty/long titles and history. | Same observed mobile UA before the first request in both containers, including after rotation. Do not alter actual platform/DPR. No previous-document title leaks into the selected illustrative tab during provisional load; committed title updates and native ellipsis preserve measured whole-tab/text frames. Record HTTP receipts and presented text, not only a constant comparison. |
| M03 Persistent storage | On a unique stable HTTP origin, write tokenized localStorage, IndexedDB and a persistent cookie; quit normally, relaunch the same app identity, then read them. Test two containers and the tools frontend separately. | Values survive separate app processes in the measured store; sessionStorage is not treated as cross-process persistence. Tools nonpersistent storage is separate. Record write/read values and origin/token locally. `DEVDUO_WEBKIT_PROFILE` is a Windows facility and must not be claimed to isolate this test. |
| M04 Preserve the document | Enter form text, create history and scroll; rotate all eight fold/body mappings; change language, theme, outline, Fit and Open tab-strip setting. | No navigation/recreation for these presentation changes. Compare URL, time origin/document ID, form/history, actual viewport, document extent, scroll range and fixed rectangles. Rotation/viewport growth may clamp scroll; record the reason instead of restoring it with script. Fold/mode switches intentionally replace the container. |
| M05 Inspector and shortcuts | With focus in the address bar, native page, editable field and Inspector, use its sidebar button, F12 and Ctrl+Shift+I; on keyboards requiring it, record Fn use. Close/reopen and resize. | Real DOM/Console in a separate detached window; no device viewport reservation, stale Inspector state or duplicate frontend. Check ⌘L, ⌘R, ⌘[/⌘], ⌘S and ⌘\\ through the native menu with page focus. Unsupported selectors fail visibly. Keyboard menu registration alone is insufficient. |
| M06 Native remote | Start/stop native remote inspection and discover the target in Safari Develop. Try local Inspector while remote is enabled and vice versa; close the container during startup. | Correct target/document, explicit conflict handling, disappearance on stop/disposal. Record system Safari discovery/configuration requirements actually encountered. No claim of a host-controlled Windows TCP port or token-protected native discovery on macOS. |
| M07 CDP and chii | Enable CDP; open the packaged frontend, inspect real DOM/styles and evaluate a harmless expression. Navigate, return through history, rotate, close/reopen frontend, stop/start quickly and destroy the container. Include an HTTPS/CSP page. | Separate tools WKWebView, current target and frontend reconnect after navigation; same document survives rotation. Token discovery and WebSocket routing work against the real page. Stop closes frontend/listener and removes future injection; existing page hooks may remain until refresh. No stale callback accesses disposed views. Do not bypass upstream Console paste protection. |
| M08 Proxy | Use recorded local HTTP, trusted HTTPS and SOCKS5 proxy endpoints with request receipts. Apply a custom proxy, request document/assets/fetch, then return to system. Test rejected direct/authenticated/malformed inputs. | Existing document/time origin/scroll survive applying settings; future traffic reaches the selected proxy with no silent direct fallback. A TLS ClientHello followed by an untrusted-certificate failure proves only that handshake attempt, not a trusted HTTPS session. Record default-store sharing effects. |
| M09 Fixed and dialog alpha | Use the fixture procedure below in Closed expanded/compact and both side directions; include Open top and immersive controls. Show/hide fixed masks, element opacity and dialog backdrops over root/opaque footer. | Viewport receives the authored alpha exactly once; inset extension uses the appropriate native fixed-container color. Record the actual capability branch, top/side/bottom regions and before/during/after pixels. No residual host fill, stale dimming, changed document dimensions or viewport padding. Unsupported fallback cases remain explicit. |
| M10 Continuation and boundaries | Load static-gradient, static-quirks, transparent, dynamic-height and live-glass fixtures; scroll near/end, shrink/restore, return to top, and change compact state. | Actual document flow remains live only within document bounds; root/background begins beyond them, with no copied rows, black strip or stale pixels. Observe Closed bottom/side independently from Open's known reserved-frame approximation. Sample actual window pixels as well as DOM/native state. |
| M11 Scroll indicators | Use fresh `scroll-indicators.html` documents for auto/short/forced-scroll/none/custom-width cases. Exercise wheel, momentum, keyboard, script, canceled wheel and endpoint/fade. | Record actual system preference and native scrollbar geometry/gutter; page CSS suppression and viewport/fixed/range invariants matter. Compare against the Device Hub target but retain the current macOS host-policy gap. Never force system settings or CSS to manufacture a screen-edge pass. |
| M12 Scale and native input | Test 1:1 and Fit near 100/94/75/50% where available, narrow clipping, resize/minimize/restore, and real display backing-scale/monitor transitions. Click transformed controls/links, select text, drag and use IME. | Record actual NSWindow/NSView points, backing pixels/scale, CSS viewport and scene transform independently. No input drift, one-pixel seams, overlays over host controls or hidden native child outside the preview. Requested Fit is not measured backing scale; macOS points are not Windows DPI. |
| M13 Lifecycle and access | Navigate/reload/stop/retry, change fold/mode rapidly, close while Inspector/CDP/proxy/navigation work is pending, relaunch and repeat. Exercise keyboard traversal, VoiceOver and popups. | Predictable cleanup of timers/KVO/message handlers/native references and services; no callbacks into an old container, crashes or unwanted profile deletion. Observe independent address/menu/reload/tab controls, passive clock/Wi-Fi/camera, and minute changes on a static page. Unavailable accessibility/popup/monitor scenarios remain pending. |

For M09, use the fixture's authored controls from the Inspector Console; these are test inputs, not page-geometry workarounds:

```js
setFixedOverlay(true, false, 'fixed');
setFixedOverlay(true, true, 'opacity');
setFixedOverlay(true, true, 'dialog');
setFixedOverlay(false);
fixedOverlayState();
```

Run each operation separately, wait for `fixedOverlaySettled === fixedOverlayRevision`, and save its own state/window capture. Repeat with colored roots/overlays and a partially transparent alpha using the fixture's exposed `fixedOverlayRootRgb`, `fixedOverlayRgb` and `fixedOverlayAlpha`. At clear viewport sample points, compare against the authored foreground/background alpha equation with a declared RGB tolerance. In inset regions compare the resolved extension color against the corresponding root/footer/background case; do not demand repeated page texture. Exclude glass/chrome-covered pixels from claims of direct page sampling, and retain a separate final-window observation there. Ancestor group opacity, arbitrary filters and all website stacking contexts are not generalized by this fixture.

For M11, the fixture exposes `indicatorFixture.configure(short, forceScroll, suppress, authorStyle)`, `trapWheel(enabled)` and `snapshot()`. Cover `authorStyle` values `''`, `display-none`, `zero-width`, `subpixel-width`. A fresh positive-control document is required after suppression; reused-page history contaminated an earlier Device Hub cohort. Exact universal fade timing and physical thickness are not established by sparse captures.

## Evidence and acceptance report

Keep originals and failed attempts in the new ignored session. Use Save state before/after each transition and actual presented-window captures (for example, macOS interactive window capture) during live content/scrolling. WK snapshots can omit continuation beneath obscured insets. Native snapshot/source pixels, GPU submission and final-window pixels are distinct observations; animation callback timing is not desktop FPS.

Each case record needs its ID/result; exact source revision and dirty diff/hash; app/native bridge/assets hashes; build commands/exits; actual macOS/SDK/WebKit versions; native display/backing scale and local/remote context; fixture URL/source hash; fold/body/scene angles, mode, tab variant, language and appearance; timestamps; DOM document identity/time origin/URL/scroll/extent/viewport/fixed rectangles; actual native frames/insets; capture names/hashes; expected versus observed behavior; and remaining qualifications. Obtain actual WebKit build metadata from the installed system framework when available; the overridden UA cannot supply it. Freeze any temporary recorder source. Tokens and raw browsing records stay private. Publish only reviewed relative-path derivatives without account names, hostnames, absolute private paths or simulator UDIDs.

A read-only Inspector sample can accompany Save state. Capture it before and after each operation; fixture-specific state functions add authored receipts and fixed rectangles:

```js
JSON.stringify({
  url: location.href, title: document.title, timeOrigin: performance.timeOrigin,
  documentId: window.devduoShellObservation?.documentId,
  userAgent: navigator.userAgent, dpr: devicePixelRatio,
  inner: [innerWidth, innerHeight],
  visual: [visualViewport.width, visualViewport.height, visualViewport.scale],
  scroll: [scrollX, scrollY],
  document: [document.scrollingElement.scrollWidth, document.scrollingElement.scrollHeight],
  fixed: Array.from(document.querySelectorAll('*')).filter(element =>
    ['fixed', 'sticky'].includes(getComputedStyle(element).position)
  ).map(element => ({ id: element.id, rect: element.getBoundingClientRect().toJSON() }))
});
```

Report M01–M13 as passed, failed, unsupported or not run, with concrete evidence references. Do not convert a known implementation difference into an accepted Safari deviation without new reviewed evidence. Preserve all eight mappings, 24 states and the Closed 180-degree qualification. Physical millimeters, real hardware radii and iOS Liquid Glass parameters remain unknown/`null`; desktop glass tuning and simulator pixels do not supply them.

The current outstanding macOS work is the exact-revision build/focused-test run, native storage/identity/title/welcome acceptance, preservation across current scene/localization changes, Inspector/remote/CDP/chii/proxy lifecycle and routing, system fixed-container alpha/dialog presentation, scrollbar-policy alignment, backing-scale/input/accessibility and disposal. Open continuation and the missing host screen-edge indicator are implementation differences to investigate, not merely unchecked test cells. [Windows native results and remaining Windows gates](009-WINDOWS-HANDOFF.md) remain separate.

[English](015-PLATFORM-PARITY.md) | [中文](../zh/015-PLATFORM-PARITY.md)
