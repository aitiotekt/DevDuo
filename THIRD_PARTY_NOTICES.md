# Third-party notices and provenance

The root MIT License covers first-party DevDuo implementation and documentation. It does not grant rights to Apple or Microsoft products, trademarks, SDKs, simulator artwork or dependency code. Dependency licenses remain with their respective projects; NuGet, pnpm and uv lock files identify the exact resolved packages.

Direct dependencies include Avalonia/Avalonia.Desktop 12, SukiUI (MIT) and Microsoft.Web.WebView2. SukiUI brings Avalonia.Themes.Simple, Avalonia.Controls.ColorPicker, Avalonia.Controls.DataGrid and System.Linq.Dynamic.Core; its bundled fonts and icon geometries keep their upstream licenses. The application uses operating-system WKWebView/AppKit on macOS and a separately installed WebView2 Runtime on Windows. Documentation uses VitePress/Vue and development checks use Node tooling. Optional presentation sheets use the locked Pillow runtime dependency; optional PDF-mask checks use a separately installed Poppler utility. These tools and runtimes are not vendored here.

`config/device-profiles.json` includes transformed screen paths and exterior geometry derived from artwork in a locally installed Apple Device Hub simulator profile. `config/safari-scenes.json` contains measured simulator Safari geometry and provenance hashes. These describe a simulator appearance; they do not establish actual hardware dimensions. Original Apple PDFs, binaries, screenshots, videos and extracted source bundles are excluded from Git.

No independent redistribution license for Apple's source artwork has been established in this project. Simulator-derived path data is identified here separately from first-party code and is not relicensed by the root MIT License. Review that provenance and the relevant rights before distributing derived artwork or packaged releases. This repository preparation does not claim that review has occurred.

`docs/shared/safari-reference/` contains reviewed, cropped WebP derivatives of Device Hub windows showing a first-party local fixture and Apple's simulator exterior/Safari UI. The manifest records original hashes, crop coordinates, encoding and observed geometry; original PNGs, native records and personal device metadata remain excluded. These derivatives document observed behavior and do not gain an MIT redistribution license for Apple UI/artwork. The redistribution uncertainty above still applies.

iPhone, Safari, Xcode and related product names belong to Apple. Microsoft Edge and WebView2 belong to Microsoft. DevDuo is an independent developer tool and implies no affiliation or endorsement.

[English](THIRD_PARTY_NOTICES.md) | [中文](docs/zh/THIRD_PARTY_NOTICES.md)
