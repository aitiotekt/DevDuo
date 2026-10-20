check: docs-check check-python test

check-python:
    uv run --locked devduo-tools check-repository
    uv run --locked ruff check scripts/python tests/python
    uv run --locked ruff format --check scripts/python tests/python

test: test-js test-python test-localization

test-js:
    pnpm test

test-python:
    uv run --locked devduo-tools self-test

# Dependency-free .NET harness; restore still uses its committed lockfile.
test-localization:
    dotnet run --project tests/DevDuo.Localization.Tests -p:RestoreLockedMode=true

# Actual AppKit frame behavior; supplements the presented-window acceptance on macOS.
[macos]
test-macos:
    mkdir -p temp/build/native-tests temp/cache/swift-tests
    xcrun swiftc -parse-as-library src/DevDuo/macos/WebViewBridge.swift tests/macos/NativeLayoutTests.swift -module-cache-path temp/cache/swift-tests -framework AppKit -framework WebKit -framework QuartzCore -o temp/build/native-tests/native-layout-tests
    temp/build/native-tests/native-layout-tests

lint:
    pnpm lint

format:
    pnpm lint:fix
    uv run --locked ruff format scripts/python tests/python

typecheck:
    pnpm typecheck

docs-sync:
    node scripts/docs.mts sync

docs-check:
    node scripts/docs.mts check

verify: check lint typecheck release-check build-docs build

# Generated outputs only; evidence, dependencies and packaged apps are retained.
_clean_paths := "temp/cache temp/build .ruff_cache scripts/swift/.build scripts/python/devduo_tools/__pycache__ tests/python/__pycache__ scripts/archive/2026-10-21/__pycache__ src/DevDuo/bin src/DevDuo/obj tests/DevDuo.Localization.Tests/bin tests/DevDuo.Localization.Tests/obj docsite/.vitepress/cache docsite/.vitepress/dist"

[unix]
clean:
    rm -rf -- {{_clean_paths}}

[windows]
clean:
    foreach ($path in '{{_clean_paths}}'.Split(' ')) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop } }

release-check:
    node scripts/release.mts check
