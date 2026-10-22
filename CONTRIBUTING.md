# Development and contributing

This is the day-to-day development guide. Start with the [README](README.md) to run the app; consult the technical pages below only for the area you are changing.

## Environment and setup

Install Git and mise. Native macOS builds require Xcode with the macOS 27 SDK/runtime; Windows requires the source-built WebKit host runtime. **Every contributor must support real relative file and directory symlinks**, including Windows: use a supporting filesystem, Developer Mode or equivalent privileges, and `core.symlinks=true` before cloning. Copies, junctions and regular-file substitutes are unsupported.

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
mise exec -- just doctor
```

Windows requires a one-time native runtime build before `just dev` or `just test-windows`:

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
```

The app finds `temp/build/windows-webkit-host/bin` in a checkout automatically. Outside a checkout, provide `webkit/bin` and `webkit/runtime-manifest.json` beside the app, or set `DEVDUO_WEBKIT_RUNTIME` to the matching absolute bin directory. This selects a runtime location, not a browser backend. Missing or incompatible files fail explicitly. See the [Windows WebKit guide](docs/en/014-WINDOWS-WEBKIT.md).

The commands also work in PowerShell. `doctor` tests actual link creation; synchronization refuses to overwrite regular files. If links were checked out as text, preserve your changes and use a proper symlink checkout. See [Windows developer settings](https://learn.microsoft.com/en-us/windows/advanced-settings/developer-mode) and [Git core.symlinks](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks).

| Layer | Responsibility |
| --- | --- |
| mise | Compatible .NET 10, Python 3.14, Node 26 and pnpm 12 lines; current uv, just and prek; environment only |
| just | Repository workflows, their composition, native builds and packaging |
| pnpm | JavaScript/TypeScript tooling and the independent VitePress package |
| uv | The locked Python environment and `devduo-tools` package |

Use `mise exec -- just <recipe>` outside an activated mise shell. `global.json` sets the SDK baseline/roll-forward; dependency locks retain exact packages. Python comes from mise, uv downloads are disabled, and `.venv` contains its dependencies. Do not substitute global pip, system Python, `py -3` or ad hoc package downloads. Both pnpm packages reject missing/stale dependencies instead of implicitly installing them.

## Source organization

| Directory | Responsibility |
| --- | --- |
| `src/DevDuo/Host/`, `src/DevDuo/Browser/`, `src/DevDuo/DeveloperTools/`, `src/DevDuo/Device/` | Host UI/preferences, shared browser lifecycle/state, debugging services and device/scene models |
| `src/DevDuo/macos/`, `src/DevDuo/windows/` | Platform browser bridges and presentation, including the native Windows compositor |
| `src/DevDuo/WebAssets/Pages/`, `src/DevDuo/WebAssets/Scripts/` | Application-owned pages and runtime scripts, copied into app outputs |
| `native/webkit/windows/` | Production engine lock, host patches, build and archive support |
| `tests/` | Portable checks, native app acceptance and standalone engine probes; test pages stay with their harness |
| `scripts/` | Maintained tooling, packaging and documentation helpers; Python/Swift guides and archive scope are linked below |

Keep application runtime resources separate from test fixtures and original evidence. The [architecture code map](docs/en/001-ARCHITECTURE.md#code-map) identifies the entry points.

## Daily commands

The commands below assume an activated mise environment. Use separate terminals for the receiver and app.

| Command | Purpose |
| --- | --- |
| `just serve` / `just dev` | Loopback calibration receiver / native app |
| `just check` | Docs links/parity, public audit, Ruff and JS/Python/portable .NET tests |
| `just verify` | Check plus lint/typecheck, release consistency, docs build and current-platform build |
| `just test-core` | `DevDuo.Core.Tests`: preferences, URLs/navigation payloads, device/scene invariants, title and scroll policies |
| `just test-localization` | `DevDuo.Localization.Tests`: catalogs, language selection/persistence, in-place page translation and unchanged geometry |
| `just build-windows` | Windows compilation; native runtime acceptance is separate |
| `just build-native-tests` | Compile the current platform's native acceptance harness without running UI; used by CI |
| `just test-macos` | AppKit clip/resize, logical bounds and passive footer-color regressions (macOS only) |
| `just references-check` | Reviewed Safari reference image hashes, metadata and 48-case coverage |
| `just test-windows-safari` | Scoped original Safari fixture, appearance, native scroll and pose checks; start `just serve` first |
| `just test-windows` | Native Avalonia WebKit scene/lifecycle/viewport-unit checks and missing-runtime error; opens Windows UI |
| `just test-windows-developer-tools` | Native Inspector/WIP, page CDP lifecycle and proxy-routing checks; requires the rebuilt developer API runtime and opens Windows UI |
| `just test-windows-browser-identity` | Native first-document/subresource/fetch User-Agent, redirects and rotation preservation in both containers; opens Windows UI |
| `just test-windows-navigation-latency` | Cold HTTP/inline, first source pixels, resource-free navigation timing and a separate Network crash-recovery process; opens Windows UI |
| `just test-windows-presentation` | Physical WebKit bounds, repaint damage and live raster checks across maximize/Inspector/restore; opens Windows UI |
| `just test-windows-presentation-insets` | Isolated live-source checks for unmodified color-page pixels exposed by compact chrome; opens Windows UI, separate from final DWM acceptance |
| `just test-windows-fixed-overlay` | Native modal-overlay viewport alpha, inset colors and geometry preservation; opens Windows UI, not full Safari optical acceptance |
| `just test-windows-scroll-indicators` | Native input, fade, CSS suppression and screen-edge indicator pixels with unchanged page geometry; opens Windows UI, without claiming exact Safari thickness or fade timing |
| `just test-windows-run-loop` | Actual pinned WTF timers under an external Windows message pump and WTF's own loop; requires the native runtime build |
| `just test-debugging` | Real loopback CDP HTTP/WebSocket discovery, token/origin/lifecycle checks; does not certify native page injection |
| `just prepare-debugging` | Copy locked chii/chobitsu offline assets into `temp/build/debugging/`; included in app dev/build/package recipes |
| `just probe-windows-webkit` | Pinned official WebKit standalone continuation/boundary diagnostic; opens Windows UI |
| `just test-windows-webkit` | Ordinary HTTP pages with paired real viewports and top/side insets; opens Windows UI |
| `just probe-windows-app` | Presented Avalonia WebKit checks using the reviewed loopback pages |
| `just package-macos` | Unsigned, framework-dependent `temp/DevDuo.app` |
| `just package-local-windows` | Unsigned self-contained Windows preview with the existing local engine, under `temp/local-release/` |
| `just docs` / `just build-docs` | Documentation dev server / strict build to `temp/build/docsite/` |
| `just docs-sync` / `just docs-check` | Synchronize / check relative source links and bilingual coverage |
| `just format` / `just clean` | Format maintained tooling / remove declared build and cache outputs |

Core, localization and debugging checks use dependency-free .NET harnesses and their committed locks. They do not launch a native app. Windows app scenarios live in `tests/DevDuo.Windows.Tests/Checks/`, with lifecycle/server support in `Harness/` and capture/provenance support in `Evidence/`. Building that project does not execute its native checks. Keep portable results, compilation and presented-window acceptance separate; use [Platform parity and macOS acceptance](docs/en/015-PLATFORM-PARITY.md) for the cross-platform handoff.

Pass custom arguments directly to the relevant ecosystem, for example `mise exec -- uv run --locked devduo-tools --help` or `mise exec -- pnpm --dir docsite dev --port 5174 --strictPort`. Restart static preview after rebuilding. Install optional hooks explicitly with `mise exec -- prek install`.

Generated builds/caches belong in `temp/build/` and `temp/cache/`; .NET also uses standard ignored `bin/` and `obj/`. `just clean` retains packaged apps, installed dependencies and original evidence. Use OS temporary directories for disposable validation checkouts and remove them afterwards. Keep original captures and measurements in their dedicated ignored directories. `.gitignore` does not untrack existing index entries; review the diff before publishing.

The required Windows WebKit source build uses portable tools from `mise.windows-webkit.toml` and its lock. Run `mise --env windows-webkit install`, then `mise --env windows-webkit exec -- just build-windows-webkit-runtime`. Visual Studio C++/Clang and Windows SDK remain system dependencies. The [WebKit guide](docs/en/014-WINDOWS-WEBKIT.md#host-api-source-build) owns the pinned source patch and acceptance gate.

Windows engine releases are manual and independent from application releases. A dedicated persistent self-hosted runner keeps native caches; ordinary release packaging downloads the metadata-pinned runtime. `[webkit].archive_sha256` stays empty until the first actual engine publication. See [runtime release setup and commands](docs/en/013-RELEASE.md#windows-webkit-runtime-releases).

Native developer tools and proxy acceptance is separate from service tests: verify a detached Inspector without viewport changes, native remote connect/disconnect, actual chii DOM/Console interactions, target navigation reconnect, listener release and per-platform proxy routing. Windows requires a rebuilt runtime with `hostDeveloperApiVersion: 1` and the third host patch; macOS uses system WebKit and still needs native acceptance for these changes. Direct `dotnet` commands require `just prepare-debugging` first; packaged users need no Node installation. See [developer architecture](docs/en/001-ARCHITECTURE.md#developer-tools-and-page-proxy).

## Dependencies and versions

Normal setup/check/build uses frozen pnpm, uv and per-target NuGet locks. For intentional changes, update the manifest and affected lock together:

- Python: edit `pyproject.toml`, then `mise exec -- uv lock` and `mise exec -- uv sync --locked --all-groups`.
- JS/docs: edit the relevant package, then `mise exec -- pnpm install` or `mise exec -- pnpm --dir docsite install`.
- NuGet: update the relevant target deliberately and validate both platforms for shared changes.

`devduo-metadata.toml` owns version, identity, URLs and release policy. Use `just set-version VERSION` or `just bump-version LEVEL`; a direct metadata version edit needs `just sync-version`. Run `just release-check`. Signing, platform/runtime locks, deployment and repository administration are documented in [Releases](docs/en/013-RELEASE.md).

## Editing and review

Update English and Chinese sources together, with language-local links and a bidirectional footer. Root English convention files are canonical; `docs/en` aliases them with relative symlinks, while `docs/zh` holds their translations. `docsite` links the same sources. Update `scripts/docs-manifest.json` when adding or removing a technical page; do not create website copies.

Keep identifiers/comments in English. Describe changed behavior, relevant checks and untested platforms in a review. Native changes need platform builds and actual UI acceptance; Windows cross compilation is insufficient. Keep simulator coordinates distinct from hardware dimensions, preserve evidence hashes, and review the [measurement contract](docs/en/002-MEASUREMENT-CONTRACT.md) before changing calibrated data. Do not commit or push unless requested.

| When changing… | Read on demand |
| --- | --- |
| Native browser composition or host layout | [Architecture and containers](docs/en/001-ARCHITECTURE.md) |
| Reference data, capture or comparison | [Measurement contract](docs/en/002-MEASUREMENT-CONTRACT.md), [Calibration and validation](docs/en/003-CALIBRATION-WORKFLOW.md), [Scene matrix](docs/en/005-SAFARI-SCENES.md) |
| Platform behavior or app text | [Platform parity and macOS acceptance](docs/en/015-PLATFORM-PARITY.md), [Windows handoff](docs/en/009-WINDOWS-HANDOFF.md), [Localization](docs/en/011-LOCALIZATION.md) |
| Measurement utilities | [Python guide](scripts/python/README.md), [Swift guide](scripts/swift/README.md) |
| Historical reproduction | [Archive guide and review index](scripts/archive/README.md) |

Keep personal paths, machine/account identifiers, local network device/VLAN numbers, actual network addresses, raw browsing records, secrets and unreviewed evidence out of public files. Use reserved documentation endpoints for examples and loopback for local fixtures. Run `just public-audit-index` before committing to inspect both working-tree files and staged text without changing the index. Publication boundaries are in [Privacy](PRIVACY.md), [Security](SECURITY.md) and [Third-party notices](THIRD_PARTY_NOTICES.md).

[English](CONTRIBUTING.md) | [中文](docs/zh/CONTRIBUTING.md)
