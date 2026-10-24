# Using DevDuo

For downloads, installation and your first page, start with the [README](../../README.md). This guide covers the controls you use after opening the app.

## Navigate and compare

The top address bar controls the real browser: enter an `http` or `https` URL and press Enter. Back, Forward, Reload/Stop, loading progress and **Retry** follow the active page. **Home** opens the bundled offline welcome page; normal browsing needs no calibration receiver.

| Settings group | What it does |
| --- | --- |
| Device | Choose Closed/Open and rotate with **Body +90°**. Rotation preserves the document, form state and browser history. Changing the fold recreates the preview and reloads the current URL. |
| Container | Choose Safari or immersive App WebView. Changing modes recreates the preview. **Multi-tab strip** selects the Open Safari variant with two illustrative tabs; the active label uses the current page title. It is not real tab management. |
| Preview | **Fit preview** scales presentation without changing the logical CSS viewport. **1:1 CSS px** removes that presentation scale. Toggle **Device outline** independently. |
| Appearance | Choose System, English or 简体中文, and system/light/dark theme. Language changes preserve the current document, scroll position and scene. |
| Material settings | Tune the desktop approximation of Safari glass. These controls do not establish iOS material or hardware measurements. |
| Diagnostics | Read page/native dimensions and scene information, or **Save state** for a local report. |

In Safari mode, downward scrolling compacts the shell and upward scrolling expands it; scroll position alone does not determine the state. The extra Open tab row is visible only with the multi-tab variant and expanded navigation. The [scene reference](005-SAFARI-SCENES.md) records viewport sizes, orientation history and the Device Hub gallery.

Settings and language persist, but body angle resets and the **Developer** group starts collapsed on each launch. Material/diagnostics expansion persists. **Reset settings** returns to Safari, Closed, body 0° and 1:1 while retaining the language, current page and website data. Storage and deletion details belong to [Privacy](../../PRIVACY.md).

## Touch and mouse interaction

New profiles default to **Touch · single finger** under **Preview → Interaction**. Tap with the left button, or hold and drag to pan; the page wheel/trackpad wheel is blocked at the native browser boundary. Inside the simulated phone screen, including Safari chrome, a translucent circular pointer replaces the system cursor. Its 20-DIP diameter stays constant at Fit scales and rotations. The host address bar and settings sidebar retain the normal cursor; the marker is excluded from WebKit snapshots and backdrop color sampling. The settings sidebar keeps its normal scrolling. **Mouse** restores desktop wheel scrolling, text selection and mouse events. The selected mode persists, and switching keeps the current document, viewport, history and scroll while cancelling an active contact.

Single-finger emulation sends synthetic touch PointerEvents and touch events, pans nested scroll containers, respects `touch-action`, `preventDefault()` and scroll chaining/containment, and attributes Safari shell expansion/compaction to the gesture direction. Taps prefer the engine compatibility click and use a single fallback if needed. Pages see emulated `navigator.maxTouchPoints = 1`; Mouse restores the native value. Existing and future frames receive the selected mode.

This is desktop DOM input emulation, not trusted iOS input: touch/pointer events have `isTrusted = false`, constructor availability depends on the engine, and native CSS pointer/hover media capabilities are unchanged. Multi-touch, pinch, long-press, inertial flings and native iOS form-control gestures are not reproduced. Use Mouse when a page requires desktop/native activation or selection. The mode is also available through `PATCH /api/settings` with `{"interactionMode":"touch"}` or `{"interactionMode":"mouse"}`; live input diagnostics are in `/api/state` telemetry.

## Keyboard shortcuts

| Action | macOS | Windows |
| --- | --- | --- |
| Focus address | ⌘L | Ctrl+L |
| Reload | ⌘R or F5 | Ctrl+R or F5 |
| Back / Forward | ⌘[ / ⌘] | Alt+← / Alt+→ |
| Show / hide settings | ⌘\ | Ctrl+\ |
| Save diagnostic state | ⌘S | Ctrl+S |
| Toggle Web Inspector | F12 or Ctrl+Shift+I | F12 or Ctrl+Shift+I |

## Inspect a page

Expand **Developer** at the top of the sidebar. **Open Web Inspector** toggles the separate native Inspector window. **Color demo** opens the built-in page-color example without a server. **Local calibration page** is for measurement work and needs the receiver described in [Calibration](003-CALIBRATION-WORKFLOW.md).

For a Chrome-style DOM/Console panel, enable **chobitsu CDP**, then open **Chrome DevTools**. Its frontend is bundled offline; Node is not required. This JavaScript CDP subset does not offer engine breakpoints, profiling, full network coverage or browser-wide targets. After navigating the target page, refresh the tools panel to reconnect. Disabling CDP stops the service; refresh the target page yourself to remove hooks already injected into that document.

**Native remote inspection** is separate: macOS uses Safari's Develop discovery, while Windows uses WebKit's native Inspector TCP protocol. Both remote services start disabled; enable only the one you need. Connection exposure, tokens and sharing precautions are documented in [Security](../../SECURITY.md); protocol details belong to [Architecture](001-ARCHITECTURE.md#developer-tools-and-page-proxy).

## Agent clients and fixed CDP addresses

Enable **chobitsu CDP** in **Developer**. The default base address is `http://127.0.0.1:9222/`; the configured port and paths stay the same across restarts and scene changes. The listener still starts disabled on app launch. **Copy CDP address** copies the base address without credentials.

```sh
curl http://127.0.0.1:9222/json/list
curl http://127.0.0.1:9222/json/version
```

Read `webSocketDebuggerUrl` from `/json/list` and connect to the advertised page socket, normally `ws://127.0.0.1:9222/devtools/page/devduo`. An empty list means the page bridge is not ready. Only one debugger can connect at a time; close the built-in panel before attaching an agent. Rediscover/reconnect after navigation or a scene change recreates the container.

**CDP bearer token (optional)** is empty by default, allowing local connections without authentication. To enable header authentication, enter the same token that your agent will use and click **Apply listener settings**. It is masked in the UI and stored in local preferences. LAN CDP requires a nonempty token. Both discovery HTTP requests and the WebSocket handshake use `Authorization: Bearer <token>`; the persistent token is never put into the URL. For example, with the matching token in your client's environment:

```sh
curl -H "Authorization: Bearer $DEVDUO_CDP_TOKEN" http://127.0.0.1:9222/json/list
```

The variable above belongs to the client; it does not configure the app. Public bundled frontend assets need no credential. The built-in tools panel uses a separate per-start socket capability because browser WebSockets cannot attach a custom Authorization header. It continues to work when header authentication is enabled; its internal frontend URL may contain that temporary capability, while the copied agent address stays fixed. Credential storage and exposure are described in [Privacy](../../PRIVACY.md) and [Security](../../SECURITY.md).

This is an endpoint for a compatible agent/MCP/skill client, not a bundled MCP server or a complete Chromium CDP implementation. Select the desired fold/orientation in DevDuo, then use supported DOM/CSS/Runtime operations to inspect the page and viewport. The host handles `Page.captureScreenshot` using native viewport pixels. Fold/rotation controls and full browser/engine operations remain separate from this page bridge.

## REST control API

**9222 is only CDP's default port.** Change **CDP port** and apply the listener settings when another service uses it. Fixed addresses mean a stable configured port/path without a random prefix; an occupied port reports an error rather than silently choosing another.

Enable **REST control API** under **Developer** when needed. It starts **off on every launch**, independently of CDP. Its **Control API port** defaults to **9224** and is editable; **Apply control API settings** saves the port/token and restarts an already running listener. It binds only `127.0.0.1`, regardless of **Allow LAN connections**. Closing the app or resetting settings stops it; changing the device/container keeps it running.

- Open `http://127.0.0.1:9224/api/openapi` for offline **Swagger UI**, including **Try it out**.
- Fetch `http://127.0.0.1:9224/api/openapi.json` for the OpenAPI 3.1 JSON contract. Its server address and authentication requirement reflect the running configuration.
- Leave the optional control token empty for local unauthenticated access, or configure a token and send `Authorization: Bearer <token>`. In Swagger UI use **Authorize** and enter the token without the `Bearer` prefix. Documentation and its static assets remain public; control/state endpoints require the configured header. Swagger does not persist authorization or send the specification to an external validator.

| Resource | Method | Effect |
| --- | --- | --- |
| `/api/state` | GET | Read settings, navigation, scene-coordinate mapping and actual native/page telemetry. |
| `/api/settings` | PATCH | Change fold, absolute body angle, Safari/App mode, illustrative tab strip, touch/mouse interaction, preview scale/outline, theme, language and material tunables. Only supplied fields change; null material values restore defaults. |
| `/api/navigation` | POST | `navigate` with an http/https `url`; or `back`, `forward`, `reload`, `stop`, `home`, `color-demo`, `calibration`. |
| `/api/inspector` | PUT | Set native Inspector `visible` to true/false. |
| `/api/screenshot` | POST | Return PNG/JPEG bytes; `target` selects `viewport` (default) or `device` including Safari shell and enabled outline. |
| `/api/proxy` | PUT | Set proxy `mode`; custom mode takes an unauthenticated `endpoint`. macOS direct bypass remains unsupported. |

```sh
curl http://127.0.0.1:9224/api/state
curl -X PATCH http://127.0.0.1:9224/api/settings \
  -H 'Content-Type: application/json' \
  -d '{"fold":"open","bodyAngle":90,"containerMode":"safari","showTabStrip":true}'
curl -X POST http://127.0.0.1:9224/api/navigation \
  -H 'Content-Type: application/json' \
  -d '{"action":"navigate","url":"http://localhost:5173"}'
```

With authentication, add the same bearer header to each control request. Settings use canonical English identifiers in both UI languages. Unknown fields, invalid values and duplicate fields are rejected. The JSON contract documents ranges and failure responses. Request bodies are limited to 64 KiB; cross-origin browser access and CORS are unavailable.

Except for binary screenshot responses, a successful response returns current host state; page loading and native layout can still be pending. Poll `/api/state` and compare `settings` with actual `telemetry`/`navigation`, rather than assuming requested dimensions have rendered. Fold/container changes recreate the page and reset scroll/history, as in the UI. Rotation, preview, material and language changes keep the document. The recorded Closed 180° rotation-history qualification still applies; an angle command does not certify a native scene. Tab-strip control remains illustrative, not real tab management. Use CDP separately for page DOM/CSS/evaluation/scroll operations; this API does not provide remote shell, filesystem access or a bundled MCP server.

## Native screenshots

Enable the REST API, then choose the capture target in the JSON body:

```sh
curl -X POST http://127.0.0.1:9224/api/screenshot \
  -H 'Content-Type: application/json' \
  -d '{"target":"viewport","format":"png"}' --output viewport.png
curl -X POST http://127.0.0.1:9224/api/screenshot \
  -H 'Content-Type: application/json' \
  -d '{"target":"device","format":"jpeg","quality":85}' --output device.jpg
```

Add the configured bearer header as for other control requests. `format` defaults to `png`; JPEG `quality` is 0–100, default 80, and must be omitted for PNG. The response is image bytes, not JSON or a saved server-side file. `X-DevDuo-Width`, `X-DevDuo-Height`, `X-DevDuo-Target` and `X-DevDuo-Pixel-Scale` describe the encoded image.

| Target | Captured pixels |
| --- | --- |
| `viewport` | Current webpage viewport, upright, one image pixel per CSS pixel. Excludes Safari chrome, exterior artwork and the host touch pointer. Fit and desktop DPI do not change output dimensions. |
| `device` | Final native composition of the whole presented phone, including Safari chrome, under-chrome continuation, screen mask and enabled outline. Uses current body orientation, Fit and desktop backing scale. Excludes the real host address bar/settings and OS cursor; a visible simulated touch pointer is part of the device composition. App WebView mode has no Safari shell. |

Capture never changes viewport, scroll, fixed layout, insets or preview scale and never stitches a full document. Device capture requires the entire phone to fit inside the visible preview; select Fit yourself if needed. Hidden/minimized windows, clipping, unavailable native capture or a viewport/document/presentation change during capture return `409`. Retry after layout settles. Live animations are not frozen, and before/after geometry checks do not make DOM observations and pixel capture atomic. See [native implementation](001-ARCHITECTURE.md#native-screenshot-pipeline).

On the existing page WebSocket, `Page.captureScreenshot` returns `{"data":"<base64 image>"}` using the same **viewport** path:

```json
{"id":1,"method":"Page.captureScreenshot","params":{"format":"png","fromSurface":true,"captureBeyondViewport":false}}
```

PNG/JPEG and JPEG quality are supported. Omit `clip`; region/full-document capture, WebP and `fromSurface:false` are unsupported and return `-32602`. `captureBeyondViewport` without a clip still captures only the current viewport; `optimizeForSpeed` is accepted as a hint. Native capture failures return `-32000`. This addition does not make the page bridge a complete CDP or a Playwright browser endpoint.

## Page proxy

Choose a proxy mode and click **Apply proxy**. Windows supports system, direct and custom modes; macOS supports system and custom. A custom endpoint uses unauthenticated `http`, `https` or `socks5` with a port. Applying it preserves the current document and affects future requests. Proxy/listener configuration and an explicitly configured CDP bearer token persist; remote-service enablement does not. See [Privacy](../../PRIVACY.md) for stored settings and network data.

## Diagnostics and troubleshooting

**Save state** writes to local application data under `DevDuo/captures` in an installed app, or ignored `captures/desktop-validation/container-modes/` in a checkout. `DEVDUO_OUTPUT_DIR` selects another output directory. `DEVDUO_URL` overrides the remembered startup website. Review reports before attaching them to an issue; screenshots, titles and URLs can contain private data.

If a page will not load, check its full URL and whether the local development server is running, then use **Retry** or **Reload**. On Windows, a missing-runtime error usually means the application archive was only partly extracted: retain its entire `webkit/` folder. Runtime overrides and source builds belong to the [Windows WebKit guide](014-WINDOWS-WEBKIT.md#avalonia-backend); blocked app downloads belong to [installation help](013-RELEASE.md#download-trust-and-installation-help).

Both containers use the observed mobile Safari request identity, but the actual engine, platform and DPR remain native desktop values. See [mobile request identity](014-WINDOWS-WEBKIT.md#mobile-request-identity) for compatibility limits. Current platform gaps are tracked once in [Platform parity](015-PLATFORM-PARITY.md), and future host UI work in the [roadmap](100-ROADMAP.md).

[English](004-USAGE.md) | [中文](../zh/004-USAGE.md)
