# DevDuo contributor and agent guidance

This file is the repository source of truth for coding-agent behavior. Read `README.md` and `CONTRIBUTING.md` for the normal workflow. Read `docs/en/002-MEASUREMENT-CONTRACT.md` before changing measured data, and the relevant platform/topic guide only when needed. Follow explicit user instructions when they override repository conventions.

## Collaboration and scope

- Converse in the user's language. Use English identifiers, code comments and canonical engineering documentation; maintain the matching Simplified Chinese documentation.
- Complete authorized work, including relevant verification and documentation. Resolve routine reversible implementation choices without extra approval steps.
- Preserve existing working-tree and index changes. Do not stage, commit, push, publish, release, discard changes or contact another task unless requested. Start agents only when delegation is requested.
- When an authorized commit contains Codex-assisted work, include `Co-authored-by: Codex <noreply@openai.com>` exactly once in the final commit-message trailer block. Preserve the maintainer's author identity and other genuine co-authors. Attribution does not authorize a commit, push or history rewrite.
- Report what changed, why, what was actually checked and what remains unverified. Compilation, simulation and native runtime acceptance are different claims.
- Ask only for information that materially affects the task. Never configure an interactive question to resolve automatically because of a timeout or inactivity; wait for an explicit answer when an answer is required.
- Use an OS temporary directory or the calling chat's scratch directory for one-off validation checkouts, edit scripts, backups and review logs. Remove disposable copies after verification; do not leave them in the repository merely because they are ignored.
- Keep predictable build/cache outputs under `temp/build/` and `temp/cache/`, and packaged apps under `temp/`. Keep original captures and measurement evidence intact. `just clean` removes only declared generated paths, never evidence or dependency environments. Do not rerun builds after cleanup unless a source change requires it.

## Command and dependency ownership

- `mise.toml` selects the environment. Use compatible runtime lines (currently .NET 10, Python 3.14, Node 26 and pnpm 12), rather than incidental installed patch versions. Keep utility tools on `latest` unless a demonstrated compatibility constraint requires a narrower selection. Dependency locks may retain exact dependency versions.
- `justfile` and `justfiles/` own repository workflows: setup, development, checks, native builds, packaging and their composition. Use `mise exec -- just <recipe>` outside an activated mise shell. Do not duplicate recipes in mise tasks or build another general task router.
- pnpm owns JavaScript/TypeScript and VitePress commands. uv owns the Python package, dependencies and `devduo-tools` entry. Both can be called directly for ecosystem-specific arguments. Neither should route unrelated native/repository tasks.
- Python comes from mise; uv uses the project environment with `UV_PYTHON_DOWNLOADS=never`. Do not fall back to a system interpreter, global pip packages, `py -3`, or a separately downloaded interpreter. Avoid ad hoc dependency downloads.
- Normal setup and verification use the committed pnpm, uv and per-target NuGet locks. Update manifests and affected locks deliberately when changing dependencies. Keep `global.json` compatible with the supported SDK line.
- Use process argument arrays for variable arguments. Avoid arbitrary shell interpolation, command strings supplied by page content and hidden fallbacks. Missing platform capabilities or private evidence must produce an explicit, actionable result.

## Architecture and measured invariants

- Preserve Avalonia with system WKWebView on macOS and the pinned Windows WebKit Port on Windows. Keep Safari shell and immersive App WebView modes available.
- Host application controls and measured device scenes have separate responsibilities. Improving the real address bar, buttons, switches or layout must not change the logical screen, viewport reservations, masks or body-to-scene correspondence without new accepted evidence.
- Preserve all eight Closed/Open body-angle mappings and the 24 initial/scrolled Safari states, including Open single/multiple-tab variants. A commanded rotation is not proof of the observed scene. Preserve the recorded Closed 180-degree rotation-history qualification.
- Keep screen-mask paths independent of exterior artwork. Keep body angle, scene angle, CSS pixels, native points, rendered DIP and framebuffer pixels distinct. Fit preview scales presentation while preserving the logical viewport; verify actual DOM measurements.
- Use complete visual tab containers for selected-tab backgrounds. Accessibility hit rectangles, optical surfaces and paint/text clipping rectangles are different measurements.
- Unsupported hardware dimensions, physical millimeters and real hardware radii stay `null` and parameterizable. Do not infer them from screenshot pixels, simulator DPI or artwork. Simulator geometry and desktop material approximations are not physical specifications or the iOS Liquid Glass kernel.
- Preserve original capture bytes. New observations belong in new sessions; never sanitize originals in place. Keep source provenance, failed attempts and missing evidence explicit. A fresh public checkout must not fabricate private-evidence passes.
- Dispose native resources and event subscriptions predictably. Async navigation, capture and initialization callbacks must reject stale/disposed containers. Do not recreate or navigate a WebView for a presentation-only setting change.

## Maintainable implementation

- Prefer readable C#, Swift, Python and TypeScript with clear responsibilities. Comments explain reasoning or constraints. Avoid compressed one-line programs, speculative abstractions and helpers that merely rename an operation.
- Keep shared application code in `src/DevDuo/Host`, `Browser`, `DeveloperTools` and `Device`; platform bridges and presentation belong in `macos` and `windows`. Application pages/scripts belong in `WebAssets/Pages` and `WebAssets/Scripts`. Production Windows engine patches, locks and build/archive support belong in `native/webkit/windows`, separate from standalone probes under `tests`.
- Keep maintained Python tools in `scripts/python/devduo_tools`, their tests in `tests/python`, Swift diagnostic utilities in `scripts/swift`, and JavaScript documentation/package helpers in `scripts/`. Keep orchestration in just.
- Review each script before moving or retaining it. Merge overlapping capabilities, remove obsolete forwarding wrappers and document each supported command. Archive only valuable historical algorithms or experiments under `scripts/archive/`, with source hashes, cohort assumptions and explicit reproduction instructions. Archives are not routine acceptance tools.
- Preserve the maintained Python/Ruff and TypeScript/Biome conventions. Use mature dependencies where they remove fragile custom parsing. Validate inputs and retain clear failure behavior.
- Localize application-owned visible/accessibility text through the English and `zh-Hans` catalogs. Keep key and format-placeholder parity, a defined English fallback and persisted language preference. Translate telemetry only for display; canonical records, identifiers and arbitrary developer pages remain unchanged.
- Language changes must preserve the current URL, document, scroll position, scene and viewport. Localized simulated tab labels retain measured frames, font metrics and clipping. See `docs/en/011-LOCALIZATION.md`.

## Documentation and publication

- `devduo-metadata.toml` owns release versions, application identity, URLs and artifact policy. Use `just set-version VERSION` or `just bump-version LEVEL`; `just sync-version` propagates a direct metadata version edit. Keep `just release-check` passing and update both changelogs separately.

- Canonical English convention documents live at the root without a language suffix. `docs/en` contains relative symlink aliases to those documents and numbered technical sources. `docs/zh` contains matching Chinese sources. Keep filenames, coverage, navigation and `scripts/docs-manifest.json` synchronized.
- `docsite/` is an independent VitePress package linked to the canonical sources. Do not maintain copied website pages or a generated source staging tree. Edit the source, then run `just docs-check` and `just build-docs`.
- Real relative file **and directory** symbolic links are required on every contributor platform, including Windows. Require a supporting filesystem, symlink privileges and `core.symlinks=true`. Copies, junctions and regular-file substitutes are unsupported. Do not overwrite a regular file during synchronization.
- Keep both languages current, use language-local links and add a bidirectional language footer. Describe current behavior and planned work clearly; keep historical change chronology in `CHANGELOG.md` or a documented archive.
- Public content must not contain personal host names, simulator UDIDs, account names, private absolute paths, secrets or raw browsing records. Use repository-relative paths and placeholders. Ignored `captures/`, `measurements/`, `.local/` and `temp/` are local evidence, not publication inputs.
- The publication audit is one check; review the actual diff and additions. Respect `THIRD_PARTY_NOTICES.md` and keep simulator-artwork redistribution uncertainty visible.
- Reviewed Device Hub canvas derivatives belong in `docs/shared/safari-reference/` with complete case coverage and explicit crop/source provenance. Keep originals private and unchanged. Pipelines and tools do not gate on content hashes unless the operation cannot run without one; recorded hashes are outputs, not checks. `just references-check` validates public derivatives; it does not establish native acceptance or redistribution rights. System appearance, page-derived color and meta hints are separate recorded inputs.

## Verification and handoff

- Run checks appropriate to the change. The standard integration path is `mise exec -- just verify`; use `just build-windows` for shared/native changes, `just test-core` for shared state/measured invariants, `just test-localization` for catalogs/language behavior and `just test-debugging` for CDP transport. Native Windows scenarios/support/evidence belong in `tests/DevDuo.Windows.Tests/Checks`, `Harness` and `Evidence`; their compilation does not execute native acceptance. Keep meaningful tests for boundaries, formats and failure states.
- Use `just format`, then inspect the diff. Do not broaden or repeat expensive testing once relevant checks pass unless a new change or unresolved concern requires it.
- For native UI work, verify the presented window and interactions as well as builds. WK snapshots may omit visible continuation under obscured insets. Record actual viewport and native frames separately from requested settings.
- Windows cross compilation does not verify Windows WebKit initialization, input, DPI, backing scale, clipping, drawing or accessibility. Keep those native acceptance items explicit in `docs/en/009-WINDOWS-HANDOFF.md`.
- The host address bar and native navigation bindings are implemented. Future host UI polish, accessibility acceptance and real tab management must remain independent of measured Safari chrome. Track criteria in the roadmap and platform gaps in `docs/en/015-PLATFORM-PARITY.md`.
