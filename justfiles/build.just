build:
    dotnet build src/DevDuo -p:RestoreLockedMode=true

build-windows:
    dotnet build src/DevDuo -p:RestoreLockedMode=true -p:DevDuoWindows=true -p:EnableWindowsTargeting=true

build-docs: docs-check
    pnpm --dir docsite build

[macos]
package-macos:
    dotnet publish src/DevDuo/DevDuo.csproj -c Release --self-contained false -p:RestoreLockedMode=true -o temp/DevDuo.app/Contents/MacOS
    node scripts/package-macos.mts

# Release packages include .NET and use separate RID-specific dependency locks.
[macos]
package-release-macos:
    node scripts/package-release.mts prepare osx-arm64
    dotnet publish src/DevDuo -c Release -r osx-arm64 --self-contained true -p:RestoreLockedMode=true -o temp/build/release/osx-arm64/DevDuo.app/Contents/MacOS
    node scripts/package-release.mts bundle osx-arm64

[windows]
package-release-windows:
    node scripts/package-release.mts prepare win-x64
    dotnet publish src/DevDuo -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o temp/build/release/win-x64/DevDuo
    node scripts/package-release.mts bundle win-x64
    node scripts/package-release.mts archive win-x64

[macos]
sign-release-macos:
    node scripts/package-release.mts sign osx-arm64
    node scripts/package-release.mts archive osx-arm64
