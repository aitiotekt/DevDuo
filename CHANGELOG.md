# Changelog

## Unreleased

- Established DevDuo as an Avalonia desktop viewport workbench using WKWebView on macOS and WebView2 on Windows.
- Added independent screen/exterior geometry, Safari and immersive containers, eight body/fold mappings, initial/scrolled scenes and Open single/multiple-tab variants.
- Corrected selected-tab visible bounds and centered titles; preserved unknown hardware dimensions as null.
- Added capture, extraction, comparison and validation utilities, with private evidence excluded from Git.
- Made just the repository workflow driver, with broad mise tool selectors and ecosystem-specific pnpm/uv commands and frozen dependency locks.
- Reviewed all 20 former tools, consolidated maintained Python/Swift utilities, removed obsolete wrappers and documented historical-only archives.
- Added persisted English/Simplified Chinese application localization, live native/fixture updates and resource/scene-invariance tests.
- Organized numbered English/Chinese documentation and a VitePress site with mandatory relative symlinks and source-parity checks.
- Moved the macOS WKWebView bridge into `src/DevDuo/macos` and the Device Hub XCUITest probe into `scripts/DeviceHubUITests`.

Windows native runtime acceptance and host UI refinements remain pending. This entry records development work and does not announce a published release. See [Validation](docs/en/008-VALIDATION.md) and [Roadmap](docs/en/100-ROADMAP.md).

[English](CHANGELOG.md) | [中文](docs/zh/CHANGELOG.md)
