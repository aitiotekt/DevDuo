# DevDuo

Pesudo iPhone Duo Viewport Simulator for Web Developers

DevDuo is an experimental desktop workbench for testing web layouts against measured iPhone Duo simulator viewports. Avalonia hosts system WKWebView on macOS and the pinned Windows WebKit Port on Windows. Choose page-colored, floating Safari shell controls or an immersive App WebView.

The engine remains the actual desktop engine. Safari optics are approximate; physical millimeters, real hardware radii and the iOS Liquid Glass kernel remain unknown. DevDuo is independent of Apple.

## Quick start

Install Git and [mise](https://mise.jdx.dev/installing-mise.html). Native macOS builds need Xcode and the macOS 27 SDK/runtime; Windows needs the source-built WebKit host runtime. **Real file and directory symlinks are required on every contributor platform**, including Windows. Setup details and privileges are in [Development and contributing](CONTRIBUTING.md).

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
```

Windows requires a one-time native runtime build before `just dev` or `just test-windows`:

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
```

The app finds `temp/build/windows-webkit-host/bin` in a checkout automatically. Outside a checkout, provide `webkit/bin` and `webkit/runtime-manifest.json` beside the app, or set `DEVDUO_WEBKIT_RUNTIME` to the matching absolute bin directory. This selects a runtime location, not a browser backend. Missing or incompatible files fail explicitly. See the [Windows WebKit guide](docs/en/014-WINDOWS-WEBKIT.md).

Launch the application; the same command works in PowerShell:

```sh
mise exec -- just dev
```

Without an explicit startup address or a remembered website, the app opens its offline welcome page with repository and documentation links. **Home** returns to that page. Normal use needs no calibration server. **Developer tools** is the first group in the settings sidebar and starts collapsed on every launch. **Color demo**, **Calibration page** and its service hint appear last inside it, below Inspector, remote inspection, CDP and proxy controls. Calibration requires `mise exec -- just serve` in a separate terminal and is the only page that receives measurement labels.

Open any http/https page from the address bar: Enter navigates, and back/forward, reload/stop, loading progress and failures with **Retry** follow the active native browser. The settings sidebar groups Device (fold, **Body +90°**), Container (Safari/immersive, **Multi-tab strip**), Preview (1:1 or Fit, outline) and Appearance (language, light/dark theme); material tuning and diagnostics are collapsible. **Multi-tab strip** selects the Open shell variant with exactly two illustrative tabs; it does not manage real browser tabs. Its active slot shows the current document title, using native tail truncation inside the measured text frame. Fit scales presentation to the available preview area while preserving CSS dimensions.

Host settings except body angle and Developer tools expansion persist between launches; material and diagnostics expansion still persist. Opening Developer tools lasts for the current window session, including language changes. **Reset settings** returns to Safari · Closed · body 0° · 1:1. Shortcuts: ⌘L/Ctrl+L address, ⌘R/Ctrl+R or F5 reload, ⌘[ ⌘] or Alt+←/→ history, ⌘S/Ctrl+S save state, ⌘\\/Ctrl+\\ settings. The app supports persisted English, 简体中文 and system-default language choices.

Windows website storage persists under local application data, `DevDuo/WebKit`, including localStorage, IndexedDB, persistent cookies and caches. Rotation keeps the document, form state and history. The **Open Web Inspector** sidebar button, **F12** and **Ctrl+Shift+I** toggle a separate native Inspector window. Build an unsigned self-contained local preview, including the existing local WebKit runtime, with `mise exec -- just package-local-windows`; output goes to `temp/local-release/`.

Pages use the observed iPhone Safari User-Agent before their first request, consistently in HTTP headers and native `navigator.userAgent`. Both container modes use this mobile compatibility identity; the actual engine, platform and DPR remain native. See [the identity source and limits](docs/en/014-WINDOWS-WEBKIT.md#mobile-request-identity).

**Save state** writes checkout diagnostics to ignored `captures/desktop-validation/container-modes/`; installed apps use local application data under `DevDuo/captures`. `DEVDUO_OUTPUT_DIR` changes that location and `DEVDUO_URL` overrides the remembered initial URL. Original receiver measurements and desktop reproduction records remain separate.

The **Developer tools** group separates the native Web Inspector, native remote inspection, **Chrome DevTools / CDP** and page proxy settings. Both remote services start disabled; enabling one uses loopback unless **Allow LAN access** is selected. Windows native remote inspection uses WebKit's inspector TCP protocol; macOS exposes the page through Safari's Develop discovery. These are separate from CDP. CDP injects pinned chobitsu into the current page and future documents and opens the packaged chii frontend in a separate system WebView window. Its copied token URL also provides `/json/list`, `/json/version` and a page WebSocket endpoint relative to that URL. It implements a JavaScript CDP subset; engine breakpoints, profiling, complete network coverage and browser-wide targets are unavailable. The frontend reconnects after target navigation by refreshing its own panel. Disabling CDP closes the listener and removes future injection; existing page hooks remain until you refresh that page yourself. Offline debugging assets are bundled; users do not install Node.

Proxy settings apply to the page's native network store: Windows supports system, direct and custom modes; macOS supports system and custom, and reports direct mode as unsupported. Custom endpoints use unauthenticated `http`, `https` or `socks5` with a port. Applying a proxy preserves the current document; future requests use the new settings. Listener preferences and proxy settings persist, while service enablement and access tokens do not. The macOS bridge changes still require macOS native acceptance. See [Architecture](docs/en/001-ARCHITECTURE.md#developer-tools-and-page-proxy) and [Privacy](PRIVACY.md).

## Status

- macOS uses system WKWebView. Windows exclusively uses the [Windows WebKit backend](docs/en/014-WINDOWS-WEBKIT.md#avalonia-backend), with real four-side continuation, a non-testing host API and live DirectComposition glass. Native checks cover 24 Safari states and 152 standalone page comparisons; full DPI/local-and-remote presentation acceptance remains pending. Linux application checks/releases are disabled in metadata.
- References cover Closed/Open × four body angles × initial/scrolled, plus Open single/multiple tabs. Screen masks and exterior paths are independent. Partial-fold geometry is unavailable.
- The [Safari scene gallery](docs/en/005-SAFARI-SCENES.md#checked-in-device-hub-reference-gallery) includes 48 reviewed WebP canvas references, four matrix overview sheets and a color-trigger comparison, with actual viewport/frame data and source hashes.
- Checked-in config runs without private Safari captures or Apple PDFs. New Device Hub collection requires an installed simulator runtime; reference regeneration requires the original evidence.
- The host address bar and native navigation bindings are implemented. Further UI polish, accessibility acceptance and real tab management remain on the [roadmap](docs/en/100-ROADMAP.md); platform gaps are tracked in [Platform parity and macOS acceptance](docs/en/015-PLATFORM-PARITY.md).

## Development and reading guide

**For everyday development, read this README and [Contributing](CONTRIBUTING.md).** It owns setup, commands, locks, cleanup and documentation conventions. Other pages are task references:

| Task | Read when needed |
| --- | --- |
| Change browser composition/layout | [Architecture and containers](docs/en/001-ARCHITECTURE.md) |
| Change measured data or collect observations | [Measurement contract](docs/en/002-MEASUREMENT-CONTRACT.md), [Calibration and validation](docs/en/003-CALIBRATION-WORKFLOW.md), [Scene matrix](docs/en/005-SAFARI-SCENES.md) |
| Work on a platform or language | [Platform parity and macOS acceptance](docs/en/015-PLATFORM-PARITY.md), [Windows handoff](docs/en/009-WINDOWS-HANDOFF.md), [Localization](docs/en/011-LOCALIZATION.md) |
| Maintain distribution and infrastructure | [Releases and administration](docs/en/013-RELEASE.md) |

English conventions live at the root, paired Chinese sources in `docs/zh`, and numbered technical sources in `docs/en` / `docs/zh`. The VitePress workspace links them through relative symlinks. Its configured site is [dev-duo.aitiotekt.com](https://dev-duo.aitiotekt.com/); only `main` deploys after infrastructure setup. There is no manually maintained website copy.

## Evidence and distribution

Raw captures, browsing records and private notes stay local. Keep original bytes/hashes and review sanitized derivatives separately. First-party code uses [MIT](LICENSE); simulator-artwork redistribution remains unconfirmed in [Third-party notices](THIRD_PARTY_NOTICES.md). Development bundles are unsigned; release signing and platform policy belong to `devduo-metadata.toml` and the release guide. The pinned Windows WebKit runtime is supplied separately; see the Windows setup above and third-party notices.

[Security](SECURITY.md) · [Privacy](PRIVACY.md) · [Changelog](CHANGELOG.md)

[English](README.md) | [中文](docs/zh/README.md)
