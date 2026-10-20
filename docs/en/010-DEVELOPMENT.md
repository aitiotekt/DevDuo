# Development toolchain

## Environment and ownership

Install Git and mise, then run:

```sh
mise install
mise exec -- just setup
mise exec -- just doctor
```

`mise.toml` selects compatible lines: .NET 10, Python 3.14, Node 26 and pnpm 12. uv, just and prek follow `latest`. These selectors permit reuse of compatible installed tools instead of demanding an incidental patch version. `global.json` defines the .NET SDK baseline and feature-band roll-forward. Dependency lockfiles still record exact packages. Xcode, WebView2 Runtime and Git are platform/bootstrap prerequisites.

| Layer | Owns |
| --- | --- |
| mise | Tool selection and environment only; no task aliases |
| just / `justfiles/` | Repository workflows and composition, native builds and packaging |
| pnpm | JavaScript/TypeScript tooling and VitePress commands |
| uv | Python environment, dependencies and `devduo-tools` |
| SwiftPM / Xcode | macOS diagnostic executable and native platform capabilities |

Use `just` as the main command driver. An activated mise shell can omit `mise exec --`. Do not add the same setup/check/build workflow to all four command layers. A custom Python argument belongs directly to its Python CLI; a custom VitePress argument belongs directly to its Node package.

## Python and diagnostic arguments

Python is selected by mise; uv creates the checkout's `.venv` and automatic interpreter downloads are disabled. Do not use global pip, system Python or a second uv-downloaded interpreter. Maintained tools are one installable package in `scripts/python/devduo_tools`; commands have their own argument parsers:

```sh
mise exec -- uv run --locked devduo-tools --help
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output captures/comparison
mise exec -- uv run --locked devduo-tools serve --host 127.0.0.1 --port 8765 --output measurements/session-local
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

Pillow is a locked runtime dependency for image decoding and review sheets. Ruff belongs to the development group. The portable suite checks service persistence/concurrency, image decoding/diffs, Unicode paths, error states, CLI behavior and public configuration. See the [script inventory](012-SCRIPT-INVENTORY.md) for each reviewed tool, active command and historical boundary.

macOS video diagnostics use `swift run --package-path scripts/swift --scratch-path temp/build/swift-diagnostics devduo-inspect-video VIDEO NEW_OUTPUT_DIRECTORY`. Poppler is optional and required only for local PDF-mask comparisons. Xcode/Device Hub and an installed simulator runtime are required for Safari captures, not portable checks.

## Main commands

| Command (inside mise) | Behavior |
| --- | --- |
| `just setup` | Check tools/symlinks, locked Python/JS dependencies and NuGet restore |
| `just serve` / `just dev` | Loopback calibration receiver / current-platform app |
| `just check` | Docs parity/links, public audit, Ruff and JS/Python/localization tests |
| `just verify` | Check, JS lint/typecheck, strict documentation build and current-platform native build |
| `just test-localization` | Catalog, preference, format and scene-invariance tests |
| `just build-windows` | Windows cross compilation; native runtime acceptance remains separate |
| `just package-macos` | Unsigned development bundle, requiring a .NET 10 runtime |
| `just docs` / `just docs-preview` | Documentation dev server / preview of the last build |
| `just docs-sync` / `just docs-check` | Synchronize / validate canonical-source relative symlinks and bilingual inventory |
| `just build-docs` | Strict VitePress build into ignored `temp/build/docsite/` |
| `just set-version VERSION` / `just bump-version LEVEL` | Update metadata and all managed manifests; choose an explicit version or SemVer increment |
| `just sync-version` / `just check-version` | Synchronize from `devduo-metadata.toml` / reject version drift |
| `just format` | Format maintained JS/TS and Python; inspect the resulting diff |
| `just clean` | Remove declared build outputs and caches; retain evidence, installed dependencies and packaged apps |

For a custom documentation port, run `mise exec -- pnpm --dir docsite dev --port 5174 --strictPort`. Restart a running preview after rebuilding so its static asset inventory matches the new output. Optional hooks require an explicit `mise exec -- prek install`.

Both pnpm workspaces set `verifyDepsBeforeRun: error`: running a command with missing or stale dependencies fails instead of implicitly installing or changing a lockfile. `doctor` runs its standard-library Node helper before dependency setup. Installation belongs to the explicit frozen setup recipes.

## Generated files and cleanup

Generated outputs belong under ignored `temp/`: packaged apps in `temp/DevDuo.app`, diagnostic builds in `temp/build/`, and Ruff/VitePress/Swift caches in `temp/cache/`. .NET retains its standard ignored `bin/` and `obj/` intermediates. `just clean` removes declared build/cache paths, including legacy cache locations, while retaining original evidence, dependency environments and packaged apps.

Use an OS temporary directory for one-off validation checkouts and remove it when finished. `.gitignore` excludes temporary directories, caches, logs and editor/patch backups. Keep source, tests and dependency locks tracked. Existing tracked files are unaffected by ignore rules; inspect the index before committing. Keep original measurements under `captures/` or `measurements/`, separate from disposable build output.

## Locks and upgrades

Normal setup/check/build does not update dependency locks. For a deliberate Python dependency change, edit `pyproject.toml`, run `mise exec -- uv lock`, then `mise exec -- uv sync --locked --all-groups`; inspect the lock diff and rerun checks. Keep the supported Python compatibility range evidence-based.

For root JS tooling run `mise exec -- pnpm install` after editing `package.json`; for the independent documentation package run `mise exec -- pnpm --dir docsite install`. Review each affected manifest and lockfile together. NuGet has separate macOS/Windows locks; update deliberately and validate both targets for shared changes. Do not add exact package-manager self-bootstrap pins that counteract the mise compatible line.

## Documentation and Windows prerequisites

Root English convention documents are canonical. `docs/en` contains symlink aliases and numbered technical sources; `docs/zh` contains paired Chinese sources. `docsite` links those sources without copying them. Update both languages, the manifest and language footers together.

Every contributor must support actual relative file and directory symlinks, including Windows. Enable appropriate privileges and use `core.symlinks=true` before cloning. `doctor` tests actual link creation; synchronization refuses to overwrite ordinary files. See [Contributing](../../CONTRIBUTING.md) and the [Windows handoff](009-WINDOWS-HANDOFF.md).

Release packaging, version commands, signing requirements and separate runtime locks are described in [Releases](013-RELEASE.md).

[English](010-DEVELOPMENT.md) | [中文](../zh/010-DEVELOPMENT.md)
