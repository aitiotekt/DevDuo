# Windows WebKit runtime ownership

This directory owns the source-built production engine: the upstream pin,
Windows host API and painting patches, mise-aware vcpkg triplets, build-tree
management, file integrity helper and runtime archive transport. Application
startup and release tooling consume these inputs independently of test code.

`BuildRuntime.ps1` is invoked explicitly through
`mise --env windows-webkit exec -- just build-windows-webkit-runtime`. It can be
expensive. Ordinary verification and hosted application jobs must not build the
engine. The manual engine release workflow uses a persistent self-hosted runner;
application releases consume a separately published, metadata-pinned archive.
Build output remains in `temp/build/windows-webkit-host/`, source/dependency
caches in `temp/cache/windows-webkit/`, and retained toolchain build trees in
`temp/build/windows-webkit-trees/`.

The matching official archive fields in `runtime.lock.json` support the separate
comparison probe. Its downloader, Win32 executable, acceptance launchers and HTML
fixtures remain in `tests/DevDuo.WebKitProbe/`. Passing a build or archive check
does not establish native presentation acceptance. See the
[Windows WebKit guide](../../../docs/en/014-WINDOWS-WEBKIT.md) and
[Windows handoff](../../../docs/en/009-WINDOWS-HANDOFF.md).

Historical manifests and frozen inputs retain their original paths and bytes.
Patch refresh recognizes both the current frozen input layout and the historical
`inputs/tests/DevDuo.WebKitProbe/patches/` layout. Local preview packaging validates
the original engine provenance without claiming that relocated build scripts
rebuilt it. A formal new runtime release still requires current source inputs and
their fingerprint; relocation does not grant a provenance exception.
