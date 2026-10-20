# Changelog

## Unreleased

- Restored native macOS document-end canvas fill, added passive solid fixed-footer color extension and synchronized native preview placement after layout.
- Rebuilt the host UI with SukiUI on Avalonia 12: a real address bar bound to WKWebView/WebView2 navigation state, grouped settings, status bar, light/dark theme, shortcuts, accessibility labels and persisted host settings (body angle excluded). Calibration labels now apply only to the local receiver page and keep query strings and fragments; presentation changes keep the loaded page.
- Consolidated 15 technical guides into eight, with one daily development guide and task-based documentation navigation in both languages.
- Made the Checks platform matrix follow metadata and disabled unverified Linux checks and releases.
- Added `devduo-metadata.toml` as the release source of truth with synchronized version setting, incrementing and drift checks.
- Added gated macOS/Windows release packaging, signed and notarized macOS releases, artifact integrity/provenance checks and main-only Pages deployment.
- Established DevDuo as an Avalonia desktop viewport workbench using WKWebView on macOS and WebView2 on Windows.
- Added independent screen/exterior geometry, Safari and immersive containers, eight body/fold mappings, initial/scrolled scenes and Open single/multiple-tab variants.
- Corrected selected-tab visible bounds and centered titles; preserved unknown hardware dimensions as null.
- Added capture, extraction, comparison and validation utilities, with private evidence excluded from Git.
- Made just the repository workflow driver, with broad mise tool selectors and ecosystem-specific pnpm/uv commands and frozen dependency locks.
- Reviewed all 20 former tools, consolidated maintained Python/Swift utilities, removed obsolete wrappers and documented historical-only archives.
- Added persisted English/Simplified Chinese application localization, live native/fixture updates and resource/scene-invariance tests.
- Organized numbered English/Chinese documentation and a VitePress site with mandatory relative symlinks and source-parity checks.
- Moved the macOS WKWebView bridge into `src/DevDuo/macos` and the Device Hub XCUITest probe into `scripts/DeviceHubUITests`.

Windows native runtime acceptance, including the new host UI, remains pending. This entry records development work and does not announce a published release. See [Validation](docs/en/003-CALIBRATION-WORKFLOW.md) and [Roadmap](docs/en/100-ROADMAP.md).

[English](CHANGELOG.md) | [中文](docs/zh/CHANGELOG.md)
