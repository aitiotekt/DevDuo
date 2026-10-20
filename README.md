# DevDuo

Pesudo iPhone Duo Viewport Simulator for Web Developers

DevDuo is an experimental desktop workbench for testing web layouts against measured iPhone Duo simulator viewports. Avalonia hosts system WKWebView on macOS and Microsoft Edge WebView2 on Windows. Choose a Safari shell with floating, page-colored controls or an immersive App WebView container.

The engine remains the actual desktop engine. Safari optics are approximate; physical millimeters, real hardware radii and the iOS Liquid Glass kernel remain unknown. DevDuo is independent of Apple.

## Status

| Area | State |
| --- | --- |
| macOS | Native build and interactive WKWebView baseline on macOS 27 |
| Windows | Cross compiled; native Runtime, input, DPI and appearance acceptance pending |
| Scenes | Closed/Open × four body rotations × initial/scrolled; Open adds single/multiple-tab variants |
| Geometry | Independent screen mask and outer artwork; simulator coordinates, no inferred hardware millimeters |
| Host UI | Functional calibration controls; address-bar/navigation and visual refinements are planned |

## Requirements

Install Git and [mise](https://mise.jdx.dev/installing-mise.html). **Every contributor must support real file and directory symbolic links**, including on Windows. Enable the appropriate Windows developer privileges and clone with `core.symlinks=true`; see [Contributing](CONTRIBUTING.md). Copies, junctions and text files standing in for symlinks are unsupported.

`mise.toml` selects compatible Python 3.14, Node 26, pnpm 12 and .NET 10 lines, with current uv, just and prek utilities. `global.json` sets the minimum SDK and allowed feature-band roll-forward. Python dependencies live in a project virtual environment and are locked in `uv.lock`; uv is forbidden to download a second interpreter. No system Python, global pip packages or Python launcher selection is required.

macOS native builds require Xcode and the macOS 27 SDK/runtime. Device Hub and an installed iOS runtime are needed only for new Safari reference captures. Windows needs WebView2 Runtime. Linux application support is unverified; its platform checks and releases are disabled in metadata.

## Setup and run

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
```

In separate terminals:

```sh
mise exec -- just serve
mise exec -- just dev
```

The same commands work in PowerShell. just owns repository workflows; mise selects tools, pnpm owns JavaScript/documentation commands, and uv owns the maintained Python package. For tool-specific arguments, use its ecosystem entry:

```sh
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output captures/comparison
```

The app supports **English**, **简体中文** and the system default, with a persisted language selector. Switching language keeps the current page and measured viewport. See [Localization](docs/en/011-LOCALIZATION.md).

Choose Safari/immersive, Closed/Open and **Body +90°**. The **Multiple tabs** switch selects Open Safari shell variants; it does not manage real tabs. macOS Fit preview scales presentation while preserving CSS dimensions; Windows Fit still needs runtime verification.

**Save state** writes checkout diagnostics under ignored `captures/desktop-validation/container-modes/`. Installed apps use the current user's local application data under `DevDuo/captures`. `DEVDUO_OUTPUT_DIR` overrides the output location; `DEVDUO_URL` overrides the initial URL. Receiver measurements and desktop reproduction records remain separate.

## Development commands

| Command | Purpose |
| --- | --- |
| `mise exec -- just doctor` | Check managed tool executables and required symlink privileges |
| `mise exec -- just check` | Check documentation links/parity, publication candidates, Python tooling and CLI regressions |
| `mise exec -- just verify` | Also lint/typecheck tooling, build the docs site and compile the current platform |
| `mise exec -- just build-windows` | Cross compile Windows; does not establish runtime correctness |
| `mise exec -- just package-macos` | Create unsigned, framework-dependent `temp/DevDuo.app` |
| `mise exec -- just docs` | Start bilingual documentation locally |
| `mise exec -- just build-docs` | Build the VitePress site with strict internal-link checking |
| `mise exec -- just docs-sync` | Synchronize the managed relative documentation symlinks |

`pnpm-lock.yaml`, `docsite/pnpm-lock.yaml`, `uv.lock` and per-target NuGet lock files are committed dependency contracts. Setup and normal checks use frozen/locked operations. See [Development toolchain](docs/en/010-DEVELOPMENT.md) for upgrades, optional tools and script arguments.

## Documentation

English convention documents live at the root and are linked into `docs/en`. Chinese convention documents and translated technical pages live in `docs/zh`. Numbered English technical pages live in `docs/en`. The independent VitePress workspace in `docsite/` links to those same sources, provides English/中文 navigation and local search, and builds for the root of `https://dev-duo.aitiotekt.com/`. The Documentation workflow deploys only `main` through GitHub Pages after infrastructure configuration is applied. There is no separate manually edited website copy.

- [Overview](docs/en/000-OVERVIEW.md) · [Architecture](docs/en/001-ARCHITECTURE.md)
- [Measurement contract](docs/en/002-MEASUREMENT-CONTRACT.md) · [Calibration workflow](docs/en/003-CALIBRATION-WORKFLOW.md)
- [Safari scenes](docs/en/005-SAFARI-SCENES.md) · [Validation](docs/en/008-VALIDATION.md)
- [Development toolchain](docs/en/010-DEVELOPMENT.md) · [Localization](docs/en/011-LOCALIZATION.md) · [Script inventory](docs/en/012-SCRIPT-INVENTORY.md)
- [Releases and repository administration](docs/en/013-RELEASE.md)
- [Windows handoff](docs/en/009-WINDOWS-HANDOFF.md) · [Host UI roadmap](docs/en/100-ROADMAP.md)
- [Security](SECURITY.md) · [Privacy](PRIVACY.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

## Repository layout

| Path | Responsibility |
| --- | --- |
| `src/DevDuo/` | Avalonia host, shared scenes, macOS WKWebView bridge, Windows WebView2 and fixtures |
| `config/` | Portable simulator geometry, Safari scenes and explicit unknowns |
| `web/` | Calibration page |
| `scripts/python/`, `scripts/swift/` | Maintained measurement package and macOS video diagnostics |
| `scripts/DeviceHubUITests/` | Device Hub XCUITest probe for real Safari frames |
| `scripts/archive/` | Reviewed historical cohort tools and experiments |
| `scripts/`, `justfiles/` | JS/docs helpers and repository task recipes |
| `docs/en/`, `docs/zh/`, `docsite/` | Canonical bilingual documentation and linked VitePress workspace |
| `captures/`, `measurements/`, `.local/` | Ignored original evidence and private notes |
| `temp/`, `.venv/`, `node_modules/` | Ignored generated outputs, caches and installed dependencies |

## Evidence and distribution

Checked-in configuration is sufficient to run the app. Original Safari captures, raw browsing records and Apple PDFs are private local evidence and are excluded from Git. Regeneration tools require explicitly supplied local inputs; missing evidence must never become an invented passing result. Poppler remains an optional external prerequisite for PDF-mask checks; the locked Pillow runtime dependency supports image inspection and contact sheets.

First-party code uses the [MIT License](LICENSE). Simulator-derived path data has a separate, unconfirmed redistribution status documented in [Third-party notices](THIRD_PARTY_NOTICES.md). There are no signed releases or bundled WebView2 Runtime.

[English](README.md) | [中文](docs/zh/README.md)
