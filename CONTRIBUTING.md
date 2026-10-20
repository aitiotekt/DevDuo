# Development and contributing

This is the day-to-day development guide. Start with the [README](README.md) to run the app; consult the technical pages below only for the area you are changing.

## Environment and setup

Install Git and mise. Native macOS builds require Xcode with the macOS 27 SDK/runtime; Windows requires WebView2 Runtime. **Every contributor must support real relative file and directory symlinks**, including Windows: use a supporting filesystem, Developer Mode or equivalent privileges, and `core.symlinks=true` before cloning. Copies, junctions and regular-file substitutes are unsupported.

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
mise exec -- just doctor
```

The commands also work in PowerShell. `doctor` tests actual link creation; synchronization refuses to overwrite regular files. If links were checked out as text, preserve your changes and use a proper symlink checkout. See [Windows developer settings](https://learn.microsoft.com/en-us/windows/advanced-settings/developer-mode) and [Git core.symlinks](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks).

| Layer | Responsibility |
| --- | --- |
| mise | Compatible .NET 10, Python 3.14, Node 26 and pnpm 12 lines; current uv, just and prek; environment only |
| just | Repository workflows, their composition, native builds and packaging |
| pnpm | JavaScript/TypeScript tooling and the independent VitePress package |
| uv | The locked Python environment and `devduo-tools` package |

Use `mise exec -- just <recipe>` outside an activated mise shell. `global.json` sets the SDK baseline/roll-forward; dependency locks retain exact packages. Python comes from mise, uv downloads are disabled, and `.venv` contains its dependencies. Do not substitute global pip, system Python, `py -3` or ad hoc package downloads. Both pnpm packages reject missing/stale dependencies instead of implicitly installing them.

## Daily commands

The commands below assume an activated mise environment. Use separate terminals for the receiver and app.

| Command | Purpose |
| --- | --- |
| `just serve` / `just dev` | Loopback calibration receiver / native app |
| `just check` | Docs links/parity, public audit, Ruff and JS/Python/localization tests |
| `just verify` | Check plus lint/typecheck, release consistency, docs build and current-platform build |
| `just build-windows` | Windows compilation; native runtime acceptance is separate |
| `just test-macos` | AppKit clip/resize, logical bounds and passive footer-color regressions (macOS only) |
| `just references-check` | Reviewed Safari reference image hashes, metadata and 48-case coverage |
| `just package-macos` | Unsigned, framework-dependent `temp/DevDuo.app` |
| `just docs` / `just build-docs` | Documentation dev server / strict build to `temp/build/docsite/` |
| `just docs-sync` / `just docs-check` | Synchronize / check relative source links and bilingual coverage |
| `just format` / `just clean` | Format maintained tooling / remove declared build and cache outputs |

Pass custom arguments directly to the relevant ecosystem, for example `mise exec -- uv run --locked devduo-tools --help` or `mise exec -- pnpm --dir docsite dev --port 5174 --strictPort`. Restart static preview after rebuilding. Install optional hooks explicitly with `mise exec -- prek install`.

Generated builds/caches belong in `temp/build/` and `temp/cache/`; .NET also uses standard ignored `bin/` and `obj/`. `just clean` retains packaged apps, installed dependencies and original evidence. Use OS temporary directories for disposable validation checkouts and remove them afterwards. Keep original captures and measurements in their dedicated ignored directories. `.gitignore` does not untrack existing index entries; review the diff before publishing.

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
| Windows behavior or app text | [Windows handoff](docs/en/009-WINDOWS-HANDOFF.md), [Localization](docs/en/011-LOCALIZATION.md) |
| Measurement utilities | [Python guide](scripts/python/README.md), [Swift guide](scripts/swift/README.md) |
| Historical reproduction | [Archive guide and review index](scripts/archive/README.md) |

Keep personal paths, machine identifiers, raw browsing records, secrets and unreviewed evidence out of public files. Publication boundaries are in [Privacy](PRIVACY.md), [Security](SECURITY.md) and [Third-party notices](THIRD_PARTY_NOTICES.md).

[English](CONTRIBUTING.md) | [中文](docs/zh/CONTRIBUTING.md)
