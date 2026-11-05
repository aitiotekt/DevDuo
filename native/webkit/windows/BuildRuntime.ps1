param([switch]$ConfigureOnly, [int]$Jobs = 8, [string]$RuntimeRoot = '', [switch]$KeepRuntimeSelection)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RuntimeFiles.ps1')
. (Join-Path $PSScriptRoot 'PersistentBuild.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'runtime.lock.json') -Raw | ConvertFrom-Json
$source = Join-Path $repository ('temp/cache/windows-webkit/source-' + $pin.revision.Substring(0, 8))
$build = Join-Path $repository 'temp/build/windows-webkit-host'
$runtimeRoot = if ($RuntimeRoot) { [IO.Path]::GetFullPath($RuntimeRoot) } else { $build }
$runtime = Join-Path $runtimeRoot 'bin'
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
$edgeColorPatch = Join-Path $PSScriptRoot 'patches/0005-windows-edge-color-v2.patch'
$framePipelinePatch = Join-Path $PSScriptRoot 'patches/0006-windows-frame-pipeline.patch'
$atomicContinuationPatch = Join-Path $PSScriptRoot 'patches/0007-windows-atomic-continuation.patch'
$frameSurfacePatch = Join-Path $PSScriptRoot 'patches/0008-windows-frame-surfaces.patch'
$frameClockPatch = Join-Path $PSScriptRoot 'patches/0009-windows-frame-clock.patch'
$scrollingLayersPatch = Join-Path $PSScriptRoot 'patches/0010-windows-scrolling-layers.patch'
$directFrameRelayPatch = Join-Path $PSScriptRoot 'patches/0011-windows-direct-frame-relay.patch'
$asyncPresentationPatch = Join-Path $PSScriptRoot 'patches/0012-windows-async-presentation.patch'
$nativeTouchPatch = Join-Path $PSScriptRoot 'patches/0013-windows-native-touch.patch'
$rasterCoveragePatch = Join-Path $PSScriptRoot 'patches/0014-windows-wc-raster-coverage.patch'
$asyncViewportPatch = Join-Path $PSScriptRoot 'patches/0015-windows-async-viewport-publication.patch'
$directPanPatch = Join-Path $PSScriptRoot 'patches/0016-windows-direct-pan-work.patch'
$preciseTouchPatch = Join-Path $PSScriptRoot 'patches/0017-windows-precise-touch-coordinates.patch'
$viewportFixedPatch = Join-Path $PSScriptRoot 'patches/0018-windows-viewport-fixed-transition.patch'
$dimmingSheetPatch = Join-Path $PSScriptRoot 'patches/0019-windows-fixed-dimming-sheet.patch'
$fixedPortalPatch = Join-Path $PSScriptRoot 'patches/0020-windows-fixed-portal-clip.patch'
$dimmingOverlayPatch = Join-Path $PSScriptRoot 'patches/0021-windows-dimming-overlay-paint.patch'
Sync-WebKitPatches $source $cache $pin.revision $build $previousInputRoot @($patch, $paintingPatch, $developerPatch, $desktopPatch, $edgeColorPatch, $framePipelinePatch, $atomicContinuationPatch, $frameSurfacePatch, $frameClockPatch, $scrollingLayersPatch, $directFrameRelayPatch, $asyncPresentationPatch, $nativeTouchPatch, $rasterCoveragePatch, $asyncViewportPatch, $directPanPatch, $preciseTouchPatch, $viewportFixedPatch, $dimmingSheetPatch, $fixedPortalPatch, $dimmingOverlayPatch)
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
$arguments += '-DDEVDUO_HOST_RUNTIME_OUTPUT_DIRECTORY=' + $runtime.Replace('\', '/')
$arguments += '-DENABLE_ASYNC_SCROLLING=ON'
$arguments += '-DENABLE_TOUCH_EVENTS=ON'
if ($runtimeRoot -ne $build -and -not (Test-Path -LiteralPath $runtime)) {
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    Copy-Item -LiteralPath (Join-Path $build 'bin') -Destination $runtime -Recurse
}
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
    'native/webkit/windows/patches/0005-windows-edge-color-v2.patch',
    'native/webkit/windows/patches/0006-windows-frame-pipeline.patch',
    'native/webkit/windows/patches/0007-windows-atomic-continuation.patch',
    'native/webkit/windows/patches/0008-windows-frame-surfaces.patch',
    'native/webkit/windows/patches/0009-windows-frame-clock.patch',
    'native/webkit/windows/patches/0010-windows-scrolling-layers.patch',
    'native/webkit/windows/patches/0011-windows-direct-frame-relay.patch',
    'native/webkit/windows/patches/0012-windows-async-presentation.patch',
    'native/webkit/windows/patches/0013-windows-native-touch.patch',
    'native/webkit/windows/patches/0014-windows-wc-raster-coverage.patch',
    'native/webkit/windows/patches/0015-windows-async-viewport-publication.patch',
    'native/webkit/windows/patches/0016-windows-direct-pan-work.patch',
    'native/webkit/windows/patches/0017-windows-precise-touch-coordinates.patch',
    'native/webkit/windows/patches/0018-windows-viewport-fixed-transition.patch',
    'native/webkit/windows/patches/0019-windows-fixed-dimming-sheet.patch',
    'native/webkit/windows/patches/0020-windows-fixed-portal-clip.patch',
    'native/webkit/windows/patches/0021-windows-dimming-overlay-paint.patch',
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
    edgeColorPatchSha256 = Get-WebKitFileHash $edgeColorPatch
    framePipelinePatchSha256 = Get-WebKitFileHash $framePipelinePatch
    atomicContinuationPatchSha256 = Get-WebKitFileHash $atomicContinuationPatch
    frameSurfacePatchSha256 = Get-WebKitFileHash $frameSurfacePatch
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
foreach ($name in @('WebKit2.dll', 'WebCore.dll', 'JavaScriptCore.dll', 'WebKitWebProcess.exe', 'WebKitNetworkProcess.exe', 'WebKitGPUProcess.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $runtime $name))) { throw "Native build did not produce $name" }
}
$exports = & (Get-Command dumpbin).Source /exports (Join-Path $runtime 'WebKit2.dll')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect host API exports' }
foreach ($name in @('WKViewGetHostScrollFrameAPIVersion', 'WKViewCopyHostScrollFrames', 'WKViewCreateWithHostFrameSurface', 'WKViewRequestHostScrollingState')) {
    if (-not ($exports -match ('\b' + [Regex]::Escape($name) + '\b'))) { throw "Missing built host API export: $name" }
}
foreach ($name in @('WKViewGetHostTouchAPIVersion', 'WKViewSetHostTouchEnabled', 'WKViewCopyHostTouchDiagnostics')) {
    if (-not ($exports -match ('\b' + [Regex]::Escape($name) + '\b'))) { throw "Missing built host API export: $name" }
}
foreach ($name in @('WKViewGetHostFrameSurfaceAPIVersion', 'WKViewSetHostFrameSurfaceEnabled', 'WKViewAcquireHostFrameSurface', 'WKViewAcknowledgeHostFrameSurface', 'WKViewReplayHostFrameSurface', 'WKViewGetHostGDIUploadCount')) {
    if (-not ($exports -match ('\b' + [Regex]::Escape($name) + '\b'))) { throw "Missing built host API export: $name" }
}
foreach ($name in @('WKViewGetHostAccessibilityAPIVersion', 'WKViewRequestHostAccessibilityTree', 'WKViewGetHostFrameTimingAPIVersion', 'WKViewCopyHostFrameTimings', 'WKViewGetHostFrameClock', 'WKViewGetHostFramePresentationAPIVersion', 'WKViewCopyHostFrameEdges')) {
    if (-not ($exports -match ('\b' + [Regex]::Escape($name) + '\b'))) { throw "Missing built host API export: $name" }
}
foreach ($name in @('WKViewGetHostTextInputState', 'WKViewCopyHostTextInputDiagnostics', 'WKViewResetHostTextInput', 'WKViewCopyHostAccessibilityTree', 'WKViewPerformHostAccessibilityAction', 'WKViewGetHostDesktopAPIVersion', 'WKViewSetHostDesktopGeometry', 'WKViewGetViewportHostAPIVersion', 'WKViewSetObscuredContentInsets', 'WKViewSetUnobscuredSizeRange', 'WKViewSetHostPresentationGeometry', 'WKViewGetContentColorAPIVersion', 'WKViewRequestContentEdgeColor', 'WKViewGetHostAppearanceAPIVersion', 'WKViewSetHostDarkAppearance', 'WKViewSetUsesOffscreenRendering', 'WKViewGetHostRenderingStatistics', 'WKViewGetHostDeveloperAPIVersion', 'WKInspectorStartHostRemoteServer', 'WKInspectorStopHostRemoteServer', 'WKInspectorGetHostRemoteServerPort', 'WKUserContentControllerRemoveHostUserScript', 'WKViewGetFixedContainerColorAPIVersion', 'WKViewRequestFixedContainerColors', 'WKViewRequestFixedContainerColorsV2', 'WKViewGetHostScrollIndicatorAPIVersion', 'WKViewSetHostScrollIndicatorEnabled', 'WKViewRequestHostScrollIndicatorState')) {
    if (-not ($exports -match ('\b' + [Regex]::Escape($name) + '\b'))) { throw "Missing built host API export: $name" }
}
$runtimeFiles = Get-ChildItem -LiteralPath $runtime -File -Recurse |
    Where-Object { $_.Extension -notin @('.pdb', '.ilk') -and $_.Name -notlike 'DevDuo.WebKitProbe*' } |
    ForEach-Object {
        [ordered]@{ path = $_.FullName.Substring($runtime.Length + 1).Replace('\', '/') }
    }
[ordered]@{
    schemaVersion = 1; upstreamRevision = $pin.revision
    hostFrameTimingApiVersion = 2; hostAccessibilityApiVersion = 2
    hostFramePresentationApiVersion = 1
    hostFrameSurfaceApiVersion = 1
    hostScrollFrameApiVersion = 1
    hostTouchApiVersion = 1
    asyncScrollingEnabled = $true
    inputFiles = @($inputRecords)
    hostDesktopApiVersion = 1; hostViewportApiVersion = 2; contentPaintingVersion = 2; contentColorApiVersion = 1; hostAppearanceApiVersion = 1; hostDeveloperApiVersion = 1; hostFixedContainerColorApiVersion = 2; hostScrollIndicatorApiVersion = 1; backForwardListSwift = $false; files = @($runtimeFiles)
    runtimeAcceptancePassed = $false
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runtimeRoot 'runtime-manifest.json') -Encoding utf8
if ($runtimeRoot -ne $build) {
    Copy-Item -LiteralPath (Join-Path $build 'build-inputs.json') -Destination (Join-Path $runtimeRoot 'build-inputs.json')
    [IO.Directory]::CreateDirectory((Join-Path $runtimeRoot 'inputs')) | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath (Join-Path $build 'inputs') -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $runtimeRoot 'inputs') -Recurse -Force
    }
}
# Select only a fully built runtime with its manifest and frozen inputs in place.
# Packaged apps and explicit DEVDUO_WEBKIT_RUNTIME overrides have precedence.
if ($KeepRuntimeSelection) { exit 0 }
$selectionPath = Join-Path $repository 'temp/build/windows-webkit-runtime.json'
$selectionTemporary = $selectionPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    $selectedDirectory = if ($runtime.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        $runtime.Substring($repository.Length + 1)
    } else { $runtime }
    [ordered]@{ schemaVersion = 1; binDirectory = $selectedDirectory.Replace('\', '/') } |
        ConvertTo-Json | Set-Content -LiteralPath $selectionTemporary -Encoding utf8
    Move-Item -LiteralPath $selectionTemporary -Destination $selectionPath -Force
} finally {
    if (Test-Path -LiteralPath $selectionTemporary) { Remove-Item -LiteralPath $selectionTemporary }
}
exit 0
