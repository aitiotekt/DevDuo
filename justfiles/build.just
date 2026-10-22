prepare-debugging:
    node scripts/package-debugging.mts

build: prepare-debugging
    dotnet build src/DevDuo -p:RestoreLockedMode=true

build-windows: prepare-debugging
    dotnet build src/DevDuo -p:RestoreLockedMode=true -p:DevDuoWindows=true -p:EnableWindowsTargeting=true

build-docs: docs-check
    pnpm --dir docsite build

[macos]
package-macos: prepare-debugging
    dotnet publish src/DevDuo/DevDuo.csproj -c Release --self-contained false -p:RestoreLockedMode=true -o temp/DevDuo.app/Contents/MacOS
    node scripts/package-macos.mts

# Release packages include .NET and use separate RID-specific dependency locks.
[macos]
package-release-macos: prepare-debugging
    node scripts/package-release.mts prepare osx-arm64
    dotnet publish src/DevDuo -c Release -r osx-arm64 --self-contained true -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false -o temp/build/release/osx-arm64/DevDuo.app/Contents/MacOS
    node scripts/package-release.mts bundle osx-arm64

[windows]
package-release-windows: prepare-debugging
    node scripts/package-release.mts prepare win-x64
    dotnet publish src/DevDuo -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false -o temp/build/release/win-x64/DevDuo
    node scripts/package-release.mts bundle win-x64
    just bundle-windows-webkit-runtime
    node scripts/package-release.mts archive win-x64

# Local previews use the existing source-built engine, without a published runtime pin.
[windows]
package-local-windows: prepare-debugging
    node scripts/package-local-windows.mts prepare
    dotnet publish src/DevDuo -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true --artifacts-path temp/build/local-release/dotnet -o temp/build/local-release/win-x64/DevDuo
    node scripts/package-local-windows.mts bundle

[macos]
sign-release-macos:
    node scripts/package-release.mts sign osx-arm64
    node scripts/package-release.mts archive osx-arm64
