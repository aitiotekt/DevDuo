# Contributing

## Required contributor environment

Use mise for project tools and the committed lock files for dependencies. Install Git and mise before setup. Xcode is a macOS platform prerequisite; WebView2 Runtime is a Windows runtime prerequisite.

**Real file and directory symbolic links are mandatory on every platform.** The documentation sources are shared through relative links checked by the documentation helper. Windows contributors must use a supporting filesystem and enable Developer Mode or another appropriate symlink privilege. Set Git's symlink behavior before cloning:

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
git config core.symlinks true
mise install
mise exec -- just setup
```

See [Microsoft developer settings](https://learn.microsoft.com/en-us/windows/advanced-settings/developer-mode) and [Git core.symlinks](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks). `doctor` checks actual file/directory link creation; `docs check` checks the stored relative targets. Regular-file substitutes and junction fallbacks are not accepted. If a checkout materialized links as text files, preserve your changes and create a correct checkout; synchronization refuses to overwrite regular files.

## Commands and dependencies

```sh
mise exec -- just doctor
mise exec -- just check
mise exec -- just verify
mise exec -- just build-windows
```

just is the repository command driver. mise selects compatible tool lines, pnpm owns JS/docs, and uv owns Python. Pass custom Python arguments directly to the installed package:

```sh
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output captures/comparison
```

Do not use global pip, system Python, `py -3`, ad hoc `npx` downloads or a second uv-managed interpreter. Python comes from mise; uv uses that exact executable and a locked project environment. See [Development toolchain](docs/en/010-DEVELOPMENT.md) for dependency updates. `mise exec -- prek install` optionally installs the supplied hooks; setup does not silently change Git hooks.

## Documentation conventions

Root convention documents are canonical English sources. `docs/en` links to them and contains numbered technical sources. `docs/zh` contains matching Chinese files. Update both languages when behavior changes; keep filenames and the manifest inventory in sync. `docsite` is an independent VitePress workspace whose pages/directories are relative symlinks to those sources.

```sh
mise exec -- just docs-sync
mise exec -- just docs-check
mise exec -- just build-docs
```

Edit source files rather than creating website copies. Add bidirectional language footers. Keep new identifiers and code comments in English. Add application text to both localization catalogs and run `just test-localization`; see [Localization](docs/en/011-LOCALIZATION.md). Review script additions against the [script inventory](docs/en/012-SCRIPT-INVENTORY.md). Public text must be independent of personal machines, host names, local account names and simulator identifiers.

## Measurement and review boundaries

Keep body angle, scene angle, CSS pixels, native points, rendered DIP and framebuffer pixels separate. Preserve original evidence bytes/hashes. Keep unknown physical dimensions null. Host UI refinements must preserve logical viewport dimensions, reservations, masks and whole selected-tab bounds unless measured evidence justifies a change.

For native host changes, build both targets and perform the relevant platform's interactive acceptance checks. Windows cross compilation is not Windows runtime validation. Follow [Windows handoff](docs/en/009-WINDOWS-HANDOFF.md).

Keep raw evidence, generated builds, browsing data, secrets and private absolute paths out of Git. The publication audit checks known patterns and approved relative documentation links; review the diff as well. Describe observed behavior, validation and untested platforms in a pull request. Do not commit or push work without the user's request.

[English](CONTRIBUTING.md) | [中文](docs/zh/CONTRIBUTING.md)
