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
