check: docs-check check-python test

public-audit:
    uv run --locked devduo-tools check-repository

# Also inspect staged text; never replace the owner's index with worktree fixes.
public-audit-index:
    uv run --locked devduo-tools check-repository --index

check-python: public-audit
    uv run --locked ruff check scripts/python tests/python
    uv run --locked ruff format --check scripts/python tests/python

test: test-js test-python test-core test-localization test-debugging

test-js:
    pnpm test

test-python:
    uv run --locked devduo-tools self-test

# Public derivatives only; does not claim absent private originals passed native acceptance.
references-check:
    uv run --locked devduo-tools reference-library verify docs/shared/safari-reference

# Dependency-free .NET harness; restore still uses its committed lockfile.
test-core:
    dotnet run --project tests/DevDuo.Core.Tests -p:RestoreLockedMode=true

# Catalog, language persistence and document-preserving translation checks.
test-localization:
    dotnet run --project tests/DevDuo.Localization.Tests -p:RestoreLockedMode=true

# Exercises real loopback HTTP/WebSocket boundaries without a native WebView.
test-debugging: prepare-debugging
    dotnet run --project tests/DevDuo.Debugging.Tests -p:RestoreLockedMode=true

# Actual AppKit frame behavior; supplements the presented-window acceptance on macOS.
[macos]
build-native-tests:
    mkdir -p temp/build/native-tests temp/cache/swift-tests
    xcrun swiftc -parse-as-library src/DevDuo/macos/DeveloperWebViewBridge.swift src/DevDuo/macos/WebViewBridge.swift tests/macos/NativeLayoutTests.swift -module-cache-path temp/cache/swift-tests -framework AppKit -framework WebKit -framework QuartzCore -o temp/build/native-tests/native-layout-tests

# Compile the interactive harness without claiming an available desktop or runtime.
[windows]
build-native-tests: prepare-debugging
    dotnet build tests/DevDuo.Windows.Tests --artifacts-path temp/build/windows-runtime-tests -p:RestoreLockedMode=true

[macos]
test-macos: build-native-tests
    temp/build/native-tests/native-layout-tests

# Opens the real Avalonia/WebKit host; requires an unlocked desktop and the source-built runtime.
[windows]
test-windows: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/windows-runtime-tests -p:RestoreLockedMode=true
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/windows-runtime-tests -p:RestoreLockedMode=true -- --initialization-failure

# Native inspector/CDP transport and real browser proxy routing; opens Windows UI.
[windows]
test-windows-developer-tools: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/developer-tools-tests -p:RestoreLockedMode=true -- --developer-tools

# Actual main/subresource/fetch headers and page-visible identity in both containers.
[windows]
test-windows-browser-identity: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/browser-identity-tests -p:RestoreLockedMode=true -- --browser-identity

# First HTTP request starts only after the local server is listening.
[windows]
test-windows-navigation-latency: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/navigation-latency-tests -p:RestoreLockedMode=true -- --navigation-latency
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/navigation-latency-tests -p:RestoreLockedMode=true -- --navigation-latency-inline
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/navigation-latency-tests -p:RestoreLockedMode=true -- --navigation-latency-network-retry

# Existing live raster checks across maximize/Inspector/restore; opens Windows UI.
[windows]
test-windows-presentation: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/presentation-damage-tests -p:RestoreLockedMode=true -- --presentation-damage

# Checks viewport alpha and native fixed-container colors in actual obscured insets.
[windows]
test-windows-fixed-overlay: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/fixed-overlay-tests -p:RestoreLockedMode=true -- --fixed-overlay

# Exercises input-gated screen-edge indicators without changing the page viewport.
[windows]
test-windows-scroll-indicators: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/scroll-indicator-tests -p:RestoreLockedMode=true -- --scroll-indicators

# Isolates plain-page software damage when compact chrome exposes new content.
[windows]
test-windows-presentation-insets: prepare-debugging
    dotnet run --project tests/DevDuo.Windows.Tests --artifacts-path temp/build/presentation-damage-tests -p:RestoreLockedMode=true -- --presentation-insets

# Requires the source-built pinned runtime and its native headers/import library.
[windows]
test-windows-run-loop:
    & ./tests/DevDuo.WebKitProbe/RunLoopChecks.ps1

# Standalone Win32/C API capability probe; does not reference Avalonia.
[script('powershell.exe', '-NoLogo', '-NoProfile', '-File')]
[windows]
build-windows-webkit-probe:
    $ErrorActionPreference = 'Stop'
    $installation = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Visual Studio C++ tools are required' }
    & (Join-Path $installation 'MSBuild/Current/Bin/MSBuild.exe') @('tests/DevDuo.WebKitProbe/DevDuo.WebKitProbe.vcxproj', '/p:Configuration=Release', '/p:Platform=x64', '/nologo', '/verbosity:minimal')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    . ./tests/DevDuo.WebKitProbe/Evidence.ps1
    Write-ProbeBuildManifest ([IO.Path]::GetFullPath((Get-Location).Path))

[windows]
prepare-windows-webkit-probe:
    & ./tests/DevDuo.WebKitProbe/PrepareRuntime.ps1

# Interactive cold-inset probe. Exit zero is a layout diagnostic, not render acceptance.
[windows]
probe-windows-webkit: prepare-windows-webkit-probe build-windows-webkit-probe
    & ./tests/DevDuo.WebKitProbe/RunProbe.ps1 -StartInset -Hold

# Scoped original Safari fixture checks. Start just serve on port 8765 first.
[windows]
test-windows-safari:
    dotnet run --project tests/DevDuo.Windows.Tests -p:RestoreLockedMode=true -- --safari-reference

# Source-built production host API, eight pages and 152 paired geometry cases.
[windows]
test-windows-webkit: build-windows-webkit-probe
    & ./tests/DevDuo.WebKitProbe/RunAcceptance.ps1 -HostApi -RuntimeDirectory ./temp/build/windows-webkit-host/bin

# Use mise --env windows-webkit exec -- just build-windows-webkit-runtime.
[windows]
build-windows-webkit-runtime:
    & ./native/webkit/windows/BuildRuntime.ps1

# Start RunAcceptance.ps1 -Hold first to serve the reviewed pages on loopback.
[windows]
probe-windows-app:
    dotnet run --project tests/DevDuo.Windows.Tests -p:RestoreLockedMode=true -- --presented

lint:
    pnpm lint
    actionlint

format:
    pnpm lint:fix
    uv run --locked ruff format scripts/python tests/python

typecheck:
    pnpm typecheck
    pnpm typecheck:docsite

docs-sync:
    node scripts/docs.mts sync

docs-check:
    node scripts/docs.mts check

verify: check lint typecheck release-check build-docs build

# Generated outputs only; evidence, dependencies and packaged apps are retained.
_clean_paths := "temp/cache temp/build .ruff_cache scripts/swift/.build scripts/python/devduo_tools/__pycache__ tests/python/__pycache__ scripts/archive/2026-10-21/__pycache__ src/DevDuo/bin src/DevDuo/obj tests/DevDuo.Core.Tests/bin tests/DevDuo.Core.Tests/obj tests/DevDuo.Localization.Tests/bin tests/DevDuo.Localization.Tests/obj tests/DevDuo.Debugging.Tests/bin tests/DevDuo.Debugging.Tests/obj docsite/.vitepress/cache docsite/.vitepress/dist"

[unix]
clean:
    rm -rf -- {{ _clean_paths }}

[windows]
clean:
    foreach ($path in '{{ _clean_paths }}'.Split(' ')) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop } }

release-check:
    node scripts/release.mts check
