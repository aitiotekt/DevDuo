# DevDuo

Pesudo iPhone Duo Viewport Simulator for Web Developers

DevDuo is an experimental desktop workbench for testing web layouts against measured iPhone Duo simulator viewports. Avalonia hosts system WKWebView on macOS and Microsoft Edge WebView2 on Windows. Choose page-colored, floating Safari shell controls or an immersive App WebView.

The engine remains the actual desktop engine. Safari optics are approximate; physical millimeters, real hardware radii and the iOS Liquid Glass kernel remain unknown. DevDuo is independent of Apple.

## Quick start

Install Git and [mise](https://mise.jdx.dev/installing-mise.html). Native macOS builds need Xcode and the macOS 27 SDK/runtime; Windows needs WebView2 Runtime. **Real file and directory symlinks are required on every contributor platform**, including Windows. Setup details and privileges are in [Development and contributing](CONTRIBUTING.md).

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
```

Run in separate terminals; the same commands work in PowerShell:

```sh
mise exec -- just serve
mise exec -- just dev
```

Select Safari/immersive, Closed/Open and **Body +90°**. **Multiple tabs** selects the Open shell variant, not real browser tabs. macOS Fit preserves CSS dimensions while scaling presentation. The app supports persisted English, 简体中文 and system-default language choices.

**Save state** writes checkout diagnostics to ignored `captures/desktop-validation/container-modes/`; installed apps use local application data under `DevDuo/captures`. `DEVDUO_OUTPUT_DIR` changes that location and `DEVDUO_URL` changes the initial URL. Original receiver measurements and desktop reproduction records remain separate.

## Status

- macOS has a native macOS 27 WKWebView interactive baseline. Windows cross compilation passes; native input, DPI, zoom Fit and appearance acceptance remain pending. Linux application checks/releases are disabled in metadata.
- References cover Closed/Open × four body angles × initial/scrolled, plus Open single/multiple tabs. Screen masks and exterior paths are independent. Partial-fold geometry is unavailable.
- Checked-in config runs without private Safari captures or Apple PDFs. New Device Hub collection requires an installed simulator runtime; reference regeneration requires the original evidence.
- Host controls are functional; a genuine address bar, navigation bindings and visual refinements remain on the [roadmap](docs/en/100-ROADMAP.md).

## Development and reading guide

**For everyday development, read this README and [Contributing](CONTRIBUTING.md).** It owns setup, commands, locks, cleanup and documentation conventions. Other pages are task references:

| Task | Read when needed |
| --- | --- |
| Change browser composition/layout | [Architecture and containers](docs/en/001-ARCHITECTURE.md) |
| Change measured data or collect observations | [Measurement contract](docs/en/002-MEASUREMENT-CONTRACT.md), [Calibration and validation](docs/en/003-CALIBRATION-WORKFLOW.md), [Scene matrix](docs/en/005-SAFARI-SCENES.md) |
| Work on a platform or language | [Windows handoff](docs/en/009-WINDOWS-HANDOFF.md), [Localization](docs/en/011-LOCALIZATION.md) |
| Maintain distribution and infrastructure | [Releases and administration](docs/en/013-RELEASE.md) |

English conventions live at the root, paired Chinese sources in `docs/zh`, and numbered technical sources in `docs/en` / `docs/zh`. The VitePress workspace links them through relative symlinks. Its configured site is [dev-duo.aitiotekt.com](https://dev-duo.aitiotekt.com/); only `main` deploys after infrastructure setup. There is no manually maintained website copy.

## Evidence and distribution

Raw captures, browsing records and private notes stay local. Keep original bytes/hashes and review sanitized derivatives separately. First-party code uses [MIT](LICENSE); simulator-artwork redistribution remains unconfirmed in [Third-party notices](THIRD_PARTY_NOTICES.md). Development bundles are unsigned; release signing and platform policy belong to `devduo-metadata.toml` and the release guide. WebView2 Runtime is not bundled.

[Security](SECURITY.md) · [Privacy](PRIVACY.md) · [Changelog](CHANGELOG.md)

[English](README.md) | [中文](docs/zh/README.md)
