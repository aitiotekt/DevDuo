# Architecture

Avalonia owns the host window and shared device/scene selection. `src/DevDuo/NativeWebView.cs` hosts a platform-native child; platform renderers compose browser content and Safari illustrations within an independent screen mask. The exterior artwork is separate and can be toggled without removing the inner mask.

| Layer | Source | Responsibility |
| --- | --- | --- |
| Host UI | `src/DevDuo/MainWindow.cs` | Mode/device/orientation controls, URL submission, preview sizing and saved state |
| Shared configuration | `config/`, `DeviceProfile.cs`, `SafariScene.cs` | Screen/exterior paths, body-to-scene mappings, widget and tab geometry |
| macOS | `src/DevDuo/macos/WebViewBridge.swift` | WKWebView, native glass surfaces, clipping, obscured insets and page-color sampling |
| Windows | `src/DevDuo/WindowsWebViewPanel.cs` | WebView2, native region clipping, GDI tint/alpha composition and zoom-based Fit |
| Observation | `Fixtures/browser-observer.js` | Actual desktop page viewport, theme, title, hostname and scrolling |
| Calibration | `web/`, `scripts/python/`, `scripts/swift/`, `scripts/DeviceHubUITests/` | Browser records, real Safari native frames, original images and comparisons |
| Development | `justfiles/`, `mise.toml`, ecosystem manifests | just workflows, mise environment, pnpm JS/docs and uv Python ownership |
| Documentation | Root conventions, `docs/en`, `docs/zh`, `docsite` | Canonical bilingual sources shared through relative symlinks |

System browsers retain their actual desktop UA, DPR, orientation and CSS safe areas. Neither requested geometry nor a label makes the engine iPhone Safari. Record native frame, page viewport, optical surface, hit frame and screen coordinates separately.

macOS Closed uses public obscured content insets for actual content continuation beneath floating widgets. Open and Windows still reserve a smaller browser frame. Native macOS glass and Windows tint/alpha are different material backends, both approximations to iOS Safari. See [Container modes](004-CONTAINER-MODES.md) and [Under-chrome rendering](006-UNDER-CHROME-RENDERING.md).

The receiver only serves fixed calibration assets and records local measurement JSON. It does not control the desktop application or execute arbitrary commands. The development CLI invokes explicit subprocess argument arrays; Python interpreter selection comes from mise and dependencies from uv locks. See [Development](010-DEVELOPMENT.md), [Security](../../SECURITY.md) and [Privacy](../../PRIVACY.md).

[English](001-ARCHITECTURE.md) | [中文](../zh/001-ARCHITECTURE.md)
