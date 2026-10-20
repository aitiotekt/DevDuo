# Releases and repository administration

## Current scope

DevDuo is an experimental Avalonia workbench, with a macOS WKWebView baseline and Windows WebView2 implementation whose native input, DPI and rendering acceptance remains pending. Linux platform validation and releases are disabled by `[platforms.linux]` (`check = false`, `release = false`). Workflow planning, reports and documentation may still run on Ubuntu. Build success and signing do not establish native acceptance or rights to redistribute simulator-derived geometry. Preserve the [third-party notices](../../THIRD_PARTY_NOTICES.md).

The release workflow builds self-contained .NET 10 packages for **macOS 27 arm64** and **Windows x64**. Windows still requires WebView2 Runtime. macOS uses GitHub's `xcode-27` preview runner, verified against the [runner image inventory](https://github.com/actions/runner-images#available-images); runner availability is an external dependency. Existing `just package-macos` remains an unsigned, framework-dependent development bundle. Release packaging preserves the existing `local.DevDuo.Calibration` bundle identity and uses the native app host directly.

## Version and trigger rules

[`devduo-metadata.toml`](../../devduo-metadata.toml) owns the version, application identity, URLs and release policy. `just set-version VERSION` synchronizes it with both Node manifests and the native project. `just bump-version LEVEL` supports `major`, `minor`, `patch`, `alpha`, `beta`, `prerelease` and `stable`; prerelease counters start at zero. For example, `0.1.0-dev` → `alpha` yields `0.1.0-alpha.0`, then `prerelease` yields `0.1.0-alpha.1`.

After editing the TOML version directly, run `just sync-version`. `just check-version` detects version drift; `just release-check` also checks workflow branch/tag filters against metadata. Changing versions does not publish or create a tag. Update both changelogs separately.

| Source | Version | Result |
| --- | --- | --- |
| `release` branch | `X.Y.Z-dev` | Explicit successful skip; no package or publication |
| `release` branch | `X.Y.Z-alpha.N` / `X.Y.Z-beta.N` | Verify, build, sign, attest and publish a prerelease |
| Exact `vX.Y.Z[-alpha.N/-beta.N]` tag | Matching package version | Verify and release; stable versions require approval |
| Other branch or mismatched tag | Any | Reject before accessing signing credentials |

Pushes to `release` and `v*` tags trigger the workflow. Manual dispatch uses the selected ref and its immutable `github.sha`; it cannot nominate an unrelated source SHA. All jobs check out that same triggering commit. Reusable Checks derives its platform matrix from metadata and reruns portable tests, localization tests, lint/typecheck, a docs build and native macOS/Windows compilation before signing jobs can start. Release jobs are serialized. Credentials are never passed to PR or reusable verification jobs.

Stable versions require the `stable-release` environment approval. Both prerelease and stable macOS packages require Developer ID signing and Apple notarization; unlike the reference project's configurable unsigned prereleases, this workflow has no unsigned release fallback. Signing uses a secure timestamp, hardened runtime and the .NET JIT entitlement. Every bundled Mach-O is signed before the app, then the app is notarized, stapled and checked with Gatekeeper before archiving. See [Microsoft's macOS deployment guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos).

## Artifacts and retries

- `DevDuo-vVERSION-osx-arm64.zip`: signed/notarized `.app`, bundled .NET and notices.
- `DevDuo-vVERSION-win-x64.zip`: portable self-contained directory and notices; **no Authenticode signature**.
- `release-manifest.json`: exact version, source ref/SHA, expected files, sizes, SHA-256 hashes and platform signing facts.
- `SHA256SUMS`: hashes for both archives and the manifest. All four downloadable files receive GitHub build provenance attestations. These do not make Windows display a verified publisher.

Assembly requires exactly both platform reports and archives. Publication verifies the downloaded artifact set again, creates or verifies the tag, uploads a draft, downloads and verifies its bytes, then exposes the release. Existing releases are accepted only when every asset matches; mismatched or incomplete drafts fail without overwriting assets. To recover an interrupted upload, inspect the draft and complete it with the original final artifact set, or explicitly remove the unexposed draft before retrying. Rebuilding signed binaries can change bytes; a retry must not silently replace an already published version. No latest-release promotion occurs on a matching no-op retry.

Release staging uses ignored `temp/build/release/`; final archives and reports use `temp/release/`. Separate `packages.net10.0.osx-arm64.lock.json` and `packages.net10.0-windows.win-x64.lock.json` keep release runtime dependencies locked without changing ordinary development restores. To deliberately regenerate them, use `dotnet restore` with `-p:RuntimeIdentifier=osx-arm64` or `-p:RuntimeIdentifier=win-x64 -p:DevDuoWindows=true -p:EnableWindowsTargeting=true`, plus `--force-evaluate`; review dependency changes.

## GitHub administration and documentation

The separate **aitiotekt-infra** repository owns the DevDuo entry in `meta/github-repositories.yml`: read-only default workflow permissions, a protected `release` branch, approval/signing/Pages environments and Pages configuration. Maintain/Admin direct pushes may bypass the merge check, but cannot bypass release workflow verification; a separate safety rule forbids force pushes and deletion. PR merges require the `Checks report` status. Do not manually duplicate these settings in application scripts.

The `release-signing` environment accepts only `release` and `v*` tags and needs `APPLE_CERTIFICATE`, `APPLE_CERTIFICATE_PASSWORD`, `APPLE_SIGNING_IDENTITY`, `APPLE_ID`, `APPLE_PASSWORD` and `APPLE_TEAM_ID`. Infrastructure references the shared Developer ID certificate through SOPS and OpenBao; no credentials belong in this repository. Stable approval allows the sole maintainer to self-review and forbids administrator bypass.

Documentation is configured for **https://dev-duo.aitiotekt.com/**, with VitePress base `/`. Documentation builds on `main`, `release` and PRs; only `main` deploys through the `github-pages` environment. Infrastructure owns the DNS-only CNAME to `aitiotekt.github.io` and Pages custom domain. Apply those settings and provision environment secrets before running releases. Enable Pages HTTPS enforcement after GitHub has issued its certificate. Configuration in source does not itself prove that a deployment, certificate or release exists.

[English](013-RELEASE.md) | [中文](../zh/013-RELEASE.md)
