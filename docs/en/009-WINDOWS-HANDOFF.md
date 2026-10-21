# Windows handoff

## Objective and starting point

Continue the existing Avalonia/WebView2 implementation on a Windows workstation. Start from the DevDuo repository and SSH remote in the README. No machine-specific checkout location, device name or simulator identifier is required.

The macOS WKWebView implementation has interactive evidence. Windows has only been cross compiled on macOS; it has no accepted native Windows runtime baseline yet. The task is to establish that baseline, repair platform-specific behavior, and then improve the host UI described in [host-ui-roadmap.md](100-ROADMAP.md).

## Setup

Install Git and mise, enable real file and directory symbolic links, and use a checkout made with `core.symlinks=true`. This is mandatory for Windows contributors; see [Contributing](../../CONTRIBUTING.md). Install WebView2 Runtime separately. From the repository root in PowerShell:

```powershell
mise install
mise exec -- just setup
mise exec -- just doctor
mise exec -- just check
mise exec -- just serve
```

In a second terminal:

```powershell
mise exec -- just build
mise exec -- just dev
```

Python, uv, Node, pnpm and just are managed by mise. The CLI selects the exact mise Python and locked environment; do not substitute the Windows Python launcher or global packages. See [Development toolchain](010-DEVELOPMENT.md).

Windows automatically selects `net10.0-windows` and `packages.net10.0-windows.lock.json`. Do not regenerate the macOS lock file to resolve a Windows-only dependency problem. WebView2 uses the current user's application-data folder under `DevDuo/WebView2`; do not put that profile in the repository.

## Code map

| File | Responsibility |
| --- | --- |
| `src/DevDuo/MainWindow.cs` | Host window, controls, URL submission, diagnostics and preview sizing |
| `src/DevDuo/NativeWebView.cs` | Avalonia native-host lifetime and platform dispatch |
| `src/DevDuo/WindowsWebViewPanel.cs` | WebView2, native clipping, GDI shell rendering, scroll/theme observations |
| `src/DevDuo/SafariScene.cs` | Scene parsing and selected-tab geometry |
| `src/DevDuo/SafariContainerOptions.cs` | Shared container configuration |
| `src/DevDuo/Fixtures/browser-observer.js` | Actual desktop viewport/theme/scroll observation |
| `src/DevDuo/macos/WebViewBridge.swift` | macOS behavior to preserve while modifying shared code |
| `config/safari-scenes.json` | Eight measured body/fold mappings and 24 initial/scrolled reference states |

## Required Windows acceptance

1. Launch from a fresh checkout using only public files. Verify WebView2 initializes and reports a useful failure if the Runtime is unavailable. Record OS, runtime and DPI as generic platform metadata; exclude machine identifiers.
2. Check navigation, reload, keyboard focus, text entry, scrolling, native-view disposal, window resize and repeated device/mode switches. Confirm native children stay clipped to the independent screen mask and do not cover host controls.
3. Exercise Closed/Open × body 0/90/180/270 × initial/scrolled. In Open, exercise both tab-strip settings. Save original screenshots and state records in ignored `captures/`. Confirm actual page viewport dimensions, widget positions and collisions, rather than trusting requested options.
4. Check 100%, 150% and 200% display scaling plus movement between monitors when available. Distinguish CSS pixels, WebView2 zoom, native DIP and image pixels. Windows Fit uses WebView2 zoom and remains unverified; do not assume macOS scaling semantics apply.
5. In immersive mode confirm Safari widgets disappear, the complete requested scene becomes content, and no previous insets persist. Toggle the outer outline independently; the inner mask must remain.
6. Check multi-tab selected and unselected slots in all four Open body angles. The selected background uses the whole title container, including the close-button region; its title hit rectangle alone is too narrow. Preserve the common 14-point scene font and centered text.
7. Check initial/scroll transitions with the color fixture, theme-color, rendered-edge sampling and live page content. Windows uses tint/alpha composition, with no native backdrop blur implemented. Do not label that fallback as validated iOS Liquid Glass.

Expected Safari content sizes:

| Scene | Initial | Scrolled |
| --- | --- | --- |
| Closed 466 × 678 | 382 × 580 | 382 × 598 |
| Closed 678 × 466 | 594 × 368 | 594 × 386 |
| Open 951 × 669, multiple tabs | 951 × 543 | 951 × 618 |
| Open 669 × 951, multiple tabs | 669 × 825 | 669 × 900 |
| Open 951 × 669, single tab | 951 × 589 | 951 × 618 |
| Open 669 × 951, single tab | 669 × 871 | 669 × 900 |

These are simulator-derived targets, not proof that Windows already matches. Closed body 180° retains the preceding landscape in the recorded clockwise sequence. See [safari-scenes.md](005-SAFARI-SCENES.md) before changing the mapping.

## Evidence and limitations

Checked-in configuration suffices for app execution. Canonical raw Safari images/native records are private local evidence and are not in a public clone. The full comparison verifiers require that original evidence; request an explicitly reviewed, separately transferred evidence set if needed. Do not manufacture passing records or treat a missing image as a pass.

Use `uv run --locked devduo-tools png-compare` for equal-scale PNG comparisons with explicit crops. `uv run --locked devduo-tools contact-sheet` accepts explicit image pairs and an optional `--font`; its presentation resizing is not a fidelity test. Both use locked Pillow. See the [script inventory](012-SCRIPT-INVENTORY.md).

macOS Closed mode has real flow continuation below the floating address bar through WKWebView obscured insets. Windows currently reserves a smaller WebView2 frame. No equivalent native Windows solution has been established; investigate actual rendering and fixed-element behavior before claiming parity. See [under-chrome-rendering.md](006-UNDER-CHROME-RENDERING.md).

Keep hardware millimeters, real corner radii, hinge angles and the iOS material kernel unknown. Finish with a platform validation note listing observed results, missing cases and exact reproduction steps. Keep future host UI changes separate enough to detect viewport regressions. Commit and push only when explicitly requested.

## Language and command ownership

Use just for repository tasks and the uv/pnpm entries only for their ecosystems; see [Development](010-DEVELOPMENT.md). On Windows, verify English, 简体中文 and system-default resolution, preference persistence, error/accessibility text and language changes while a page is scrolled. Compare before/after URL, scroll and actual viewport. Cross compilation and portable localization tests do not establish these native runtime results. See [Localization](011-LOCALIZATION.md).

[English](009-WINDOWS-HANDOFF.md) | [中文](../zh/009-WINDOWS-HANDOFF.md)
