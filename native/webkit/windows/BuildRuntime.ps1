param([switch]$ConfigureOnly, [int]$Jobs = 8)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RuntimeFiles.ps1')
. (Join-Path $PSScriptRoot 'PersistentBuild.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.lock.json') -Raw | ConvertFrom-Json
$source = Join-Path $repository ('temp/cache/windows-webkit/source-' + $pin.revision.Substring(0, 8))
$build = Join-Path $repository 'temp/build/windows-webkit-host'
$cache = Join-Path $repository 'temp/cache/windows-webkit'
$patch = Join-Path $PSScriptRoot 'patches/0001-windows-host-viewport-api.patch'
foreach ($command in @('git', 'cmake', 'ninja', 'ruby', 'perl', 'gperf', 'python', 'pkgconf', 'vcpkg')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Missing $command. Run mise --env windows-webkit install, then mise --env windows-webkit exec -- just build-windows-webkit-runtime"
    }
}
$selectedTools = @{}
foreach ($command in @('cmake', 'ninja', 'ruby', 'perl', 'gperf', 'python', 'pkgconf', 'vcpkg')) {
    $selectedTools[$command] = (Get-Command $command).Source
}
$installation = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ and Windows SDK are required system dependencies' }
$clang = Join-Path $installation 'VC/Tools/Llvm/x64/bin/clang-cl.exe'
if (-not (Test-Path -LiteralPath $clang)) { throw 'Install the Visual Studio C++ Clang tools component' }
Import-Module (Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
$toolDirectories = @($selectedTools.Values | ForEach-Object { Split-Path -Parent $_ } | Select-Object -Unique)
$env:PATH = ($toolDirectories -join [IO.Path]::PathSeparator) + [IO.Path]::PathSeparator + $env:PATH
$systemInputs = [ordered]@{ windowsSDK = $env:WindowsSDKVersion; vcTools = $env:VCToolsVersion }
$clangHash = Get-WebKitFileHash $clang
$identityText = $pin.revision + ':' + $clangHash + ':' + ($systemInputs | ConvertTo-Json -Compress)
$identityHasher = [Security.Cryptography.SHA256]::Create()
try { $identityHash = [BitConverter]::ToString($identityHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($identityText))).Replace('-', '').ToLowerInvariant() }
finally { $identityHasher.Dispose() }
$previousInputRoot = Use-WebKitBuildTree $repository $build $identityHash

Initialize-PinnedSource $source 'https://github.com/WebKit/WebKit.git' $pin.revision @('Source', 'Tools', 'WebKitLibraries')
$paintingPatch = Join-Path $PSScriptRoot 'patches/0002-windows-content-painting.patch'
$developerPatch = Join-Path $PSScriptRoot 'patches/0003-windows-developer-api.patch'
$desktopPatch = Join-Path $PSScriptRoot 'patches/0004-windows-desktop-integration.patch'
Sync-WebKitPatches $source $cache $pin.revision $build $previousInputRoot @($patch, $paintingPatch, $developerPatch, $desktopPatch)
$registry = Get-Content -LiteralPath (Join-Path $source 'vcpkg-configuration.json') -Raw | ConvertFrom-Json
$vcpkgRoot = Join-Path $cache ('vcpkg-' + $registry.'default-registry'.baseline.Substring(0, 8))
Initialize-PinnedSource $vcpkgRoot 'https://github.com/microsoft/vcpkg.git' $registry.'default-registry'.baseline @('scripts', 'triplets')
Copy-Item -LiteralPath $selectedTools['vcpkg'] -Destination (Join-Path $vcpkgRoot 'vcpkg.exe') -Force
$env:VCPKG_ROOT = $vcpkgRoot
$env:DEVDUO_WEBKIT_SOURCE = $source
$env:DEVDUO_WEBKIT_PYTHON = $selectedTools['python']
$env:DEVDUO_WEBKIT_RUBY = $selectedTools['ruby']
$env:DEVDUO_WEBKIT_PERL = $selectedTools['perl']
$env:DEVDUO_WEBKIT_GPERF = $selectedTools['gperf']
$env:VCPKG_DOWNLOADS = Join-Path $cache 'downloads'
$env:VCPKG_DEFAULT_BINARY_CACHE = Join-Path $cache 'vcpkg-binaries'
$env:LC_ALL = 'C'
$env:WEBKIT_OUTPUTDIR = $build
[IO.Directory]::CreateDirectory($env:VCPKG_DEFAULT_BINARY_CACHE) | Out-Null
[IO.Directory]::CreateDirectory($build) | Out-Null
$python = (Get-Command python).Source
$arguments = @('-S', $source, '-B', $build, '-G', 'Ninja', '-DPORT=Win', '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_NINJA_FORCE_RESPONSE_FILE=ON',
    ('-DCMAKE_C_COMPILER=' + $clang), ('-DCMAKE_CXX_COMPILER=' + $clang),
    ('-DCMAKE_MAKE_PROGRAM=' + $selectedTools['ninja']),
    ('-DCMAKE_TOOLCHAIN_FILE=' + (Join-Path $vcpkgRoot 'scripts/buildsystems/vcpkg.cmake')),
    '-DVCPKG_TARGET_TRIPLET=x64-windows-devduo', '-DVCPKG_HOST_TRIPLET=x64-windows-devduo-host',
    ('-DVCPKG_OVERLAY_TRIPLETS=' + (Join-Path $PSScriptRoot 'triplets')),
    '-DVCPKG_MANIFEST_FEATURES=web;jpeg-xl;lcms;skia;woff2',
    ('-DPython_EXECUTABLE=' + $python), ('-DPERL_EXECUTABLE=' + (Get-Command perl).Source),
    ('-DRuby_EXECUTABLE=' + (Get-Command ruby).Source), ('-DGPERF_EXECUTABLE=' + (Get-Command gperf).Source),
    ('-DPKG_CONFIG_EXECUTABLE=' + $selectedTools['pkgconf']),
    '-DENABLE_API_TESTS=OFF', '-DENABLE_WEBKIT_LEGACY=OFF', '-DENABLE_BACK_FORWARD_LIST_SWIFT=OFF')
# This explicit C++ history-list flavor does not require a system Swift SDK.
# Never mix its DLLs with the official Swift-enabled runtime.
$inputFiles = @('mise.toml', 'mise.lock', 'mise.windows-webkit.toml', 'mise.windows-webkit.lock',
    'native/webkit/windows/BuildRuntime.ps1', 'native/webkit/windows/RuntimeFiles.ps1', 'native/webkit/windows/PersistentBuild.ps1', 'native/webkit/windows/MiseTools.cmake',
    'native/webkit/windows/triplets/x64-windows-devduo.cmake',
    'native/webkit/windows/triplets/x64-windows-devduo-host.cmake',
    'native/webkit/windows/patches/0001-windows-host-viewport-api.patch',
    'native/webkit/windows/patches/0002-windows-content-painting.patch',
    'native/webkit/windows/patches/0003-windows-developer-api.patch',
    'native/webkit/windows/patches/0004-windows-desktop-integration.patch',
    'native/webkit/windows/runtime.lock.json')
$inputRecords = foreach ($relative in $inputFiles) {
    $path = Join-Path $repository $relative
    if (-not (Test-Path -LiteralPath $path)) { throw 'Missing build input; install and lock the optional mise environment first' }
    $copy = Join-Path (Join-Path $build 'inputs') $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $copy)) | Out-Null
    Copy-Item -LiteralPath $path -Destination $copy -Force
    [ordered]@{ path = $relative; sha256 = Get-WebKitFileHash $path }
}
[ordered]@{
    schemaVersion = 1; upstreamRevision = $pin.revision; patchSha256 = Get-WebKitFileHash $patch
    paintingPatchSha256 = Get-WebKitFileHash $paintingPatch
    developerPatchSha256 = Get-WebKitFileHash $developerPatch
    desktopPatchSha256 = Get-WebKitFileHash $desktopPatch
    testOnlyIPCRequired = $false; backForwardListSwift = $false; cmakeArguments = $arguments
    inputFiles = @($inputRecords); clangSha256 = $clangHash
    systemInputs = $systemInputs; buildTreeIdentity = $identityHash
    tools = @('cmake', 'ninja', 'ruby', 'perl', 'gperf', 'python', 'pkgconf', 'vcpkg' | ForEach-Object {
        $path = (Get-Command $_).Source
        [ordered]@{ name = $_; path = $path; sha256 = Get-WebKitFileHash $path }
    })
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $build 'build-inputs.json') -Encoding utf8
$ErrorActionPreference = 'Continue' # Native stderr is diagnostic text; the exit code determines failure.
& cmake @arguments 2>&1 | Tee-Object -FilePath (Join-Path $build 'configure.log')
$configureExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($configureExitCode -ne 0) { exit $configureExitCode }
if ($ConfigureOnly) { exit 0 }
$ErrorActionPreference = 'Continue'
& cmake --build $build --target MiniBrowser --parallel $Jobs 2>&1 | Tee-Object -FilePath (Join-Path $build 'build.log')
$buildExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($buildExitCode -ne 0) { exit $buildExitCode }
$runtime = Join-Path $build 'bin'
foreach ($name in @('WebKit2.dll', 'WebCore.dll', 'JavaScriptCore.dll', 'WebKitWebProcess.exe', 'WebKitNetworkProcess.exe', 'WebKitGPUProcess.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $name))) { throw "Native build did not produce $name" }
}
$exports = & (Get-Command dumpbin).Source /exports (Join-Path $runtime 'WebKit2.dll')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect host API exports' }
foreach ($name in @('WKViewGetHostTextInputState', 'WKViewCopyHostTextInputDiagnostics', 'WKViewResetHostTextInput', 'WKViewCopyHostAccessibilityTree', 'WKViewPerformHostAccessibilityAction', 'WKViewGetHostDesktopAPIVersion', 'WKViewSetHostDesktopGeometry', 'WKViewGetViewportHostAPIVersion', 'WKViewSetObscuredContentInsets', 'WKViewSetUnobscuredSizeRange', 'WKViewSetHostPresentationGeometry', 'WKViewGetContentColorAPIVersion', 'WKViewRequestContentEdgeColor', 'WKViewGetHostAppearanceAPIVersion', 'WKViewSetHostDarkAppearance', 'WKViewSetUsesOffscreenRendering', 'WKViewGetHostRenderingStatistics', 'WKViewGetHostDeveloperAPIVersion', 'WKInspectorStartHostRemoteServer', 'WKInspectorStopHostRemoteServer', 'WKInspectorGetHostRemoteServerPort', 'WKUserContentControllerRemoveHostUserScript', 'WKViewGetFixedContainerColorAPIVersion', 'WKViewRequestFixedContainerColors', 'WKViewGetHostScrollIndicatorAPIVersion', 'WKViewSetHostScrollIndicatorEnabled', 'WKViewRequestHostScrollIndicatorState')) {
    if (-not ($exports -match ('\b' + [Regex]::Escape($name) + '\b'))) { throw "Missing built host API export: $name" }
}
$runtimeFiles = Get-ChildItem -LiteralPath $runtime -File -Recurse |
    Where-Object { $_.Extension -notin @('.pdb', '.ilk') -and $_.Name -notlike 'DevDuo.WebKitProbe*' } |
    ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($runtime.Length + 1).Replace('\', '/') }
    }
[ordered]@{
    schemaVersion = 1; upstreamRevision = $pin.revision
    inputFiles = @($inputRecords)
    hostDesktopApiVersion = 1; hostViewportApiVersion = 2; contentPaintingVersion = 2; contentColorApiVersion = 1; hostAppearanceApiVersion = 1; hostDeveloperApiVersion = 1; hostFixedContainerColorApiVersion = 1; hostScrollIndicatorApiVersion = 1; backForwardListSwift = $false; files = @($runtimeFiles)
    runtimeAcceptancePassed = $false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $build 'runtime-manifest.json') -Encoding utf8
exit 0
