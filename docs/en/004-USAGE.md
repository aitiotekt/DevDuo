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

## Page proxy

Choose a proxy mode and click **Apply proxy**. Windows supports system, direct and custom modes; macOS supports system and custom. A custom endpoint uses unauthenticated `http`, `https` or `socks5` with a port. Applying it preserves the current document and affects future requests. Proxy and listener configuration persists; remote-service enablement and tokens do not. See [Privacy](../../PRIVACY.md) for stored settings and network data.

## Diagnostics and troubleshooting

**Save state** writes to local application data under `DevDuo/captures` in an installed app, or ignored `captures/desktop-validation/container-modes/` in a checkout. `DEVDUO_OUTPUT_DIR` selects another output directory. `DEVDUO_URL` overrides the remembered startup website. Review reports before attaching them to an issue; screenshots, titles and URLs can contain private data.

If a page will not load, check its full URL and whether the local development server is running, then use **Retry** or **Reload**. On Windows, a missing-runtime error usually means the application archive was only partly extracted: retain its entire `webkit/` folder. Runtime overrides and source builds belong to the [Windows WebKit guide](014-WINDOWS-WEBKIT.md#avalonia-backend); blocked app downloads belong to [installation help](013-RELEASE.md#download-trust-and-installation-help).

Both containers use the observed mobile Safari request identity, but the actual engine, platform and DPR remain native desktop values. See [mobile request identity](014-WINDOWS-WEBKIT.md#mobile-request-identity) for compatibility limits. Current platform gaps are tracked once in [Platform parity](015-PLATFORM-PARITY.md), and future host UI work in the [roadmap](100-ROADMAP.md).

[English](004-USAGE.md) | [中文](../zh/004-USAGE.md)
