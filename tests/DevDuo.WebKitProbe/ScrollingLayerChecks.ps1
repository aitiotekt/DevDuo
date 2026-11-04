param([string]$RuntimeDirectory = '')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../native/webkit/windows/RuntimeFiles.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build = Join-Path $repository 'temp/build/windows-webkit-host'
$pin = Get-Content -LiteralPath (Join-Path $repository 'native/webkit/windows/runtime.lock.json') -Raw | ConvertFrom-Json
$source = Join-Path $repository ('temp/cache/windows-webkit/source-' + $pin.revision.Substring(0, 8))
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $build 'bin' }
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
$manifestPath = Join-Path $RuntimeDirectory '../runtime-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath) -or
    -not (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).asyncScrollingEnabled -or
    (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).hostScrollFrameApiVersion -ne 1) {
    throw 'Rebuild the current runtime before running scrolling-layer primitive checks.'
}
$output = Join-Path $repository 'temp/build/windows-scrolling-layer-checks'
$evidence = Join-Path $repository ('captures/desktop-validation/windows-scrolling-layers/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
[IO.Directory]::CreateDirectory($output) | Out-Null
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$installation = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ and Windows SDK are required.' }
Import-Module (Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
$clang = Join-Path $installation 'VC/Tools/Llvm/x64/bin/clang-cl.exe'
$harness = Join-Path $PSScriptRoot 'ScrollingLayerChecks.cpp'
$binary = Join-Path $output 'ScrollingLayerChecks.exe'
$arguments = @('/nologo', '/clang:-std=c++23', '/Zc:dllexportInlines-', '/MD', '/O2', '/EHs-c-', '/GR-', '/DNDEBUG', '/DNOMINMAX',
    '/DWTF_EXPORT_PRIVATE=', '/DBUILDING_WITH_CMAKE', '/DHAVE_CONFIG_H', '/D_HAS_EXCEPTIONS=0',
    '/DWIN32', '/D_WINDOWS', '/D_WIN32_WINNT=0x0A00',
    ('/I' + $build), ('/I' + (Join-Path $source 'Source/WebCore')),
    ('/I' + (Join-Path $source 'Source/WebCore/platform')), ('/I' + (Join-Path $build 'WTF/DerivedSources')),
    ('/I' + (Join-Path $build 'JavaScriptCore/PrivateHeaders')), ('/I' + (Join-Path $build 'PAL/Headers')),
    ('/I' + (Join-Path $build 'WebCore/PrivateHeaders')), ('/I' + (Join-Path $source 'Source/WTF')),
    ('/I' + (Join-Path $source 'Source/WTF/wtf')), ('/I' + (Join-Path $build 'bmalloc/Headers')),
    ('/I' + (Join-Path $build 'bmalloc/PrivateHeaders')), ('/I' + (Join-Path $build 'vcpkg_installed/x64-windows-devduo/include')),
    $harness, ('/Fo' + (Join-Path $output 'ScrollingLayerChecks.obj')), ('/Fe' + $binary),
    '/link', (Join-Path $build 'lib/WebCore.lib'), (Join-Path $build 'lib/JavaScriptCore.lib'), 'user32.lib')
& $clang @arguments
if ($LASTEXITCODE -ne 0) { throw 'Scrolling-layer primitive compilation failed.' }
$start = [Diagnostics.ProcessStartInfo]::new($binary)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['PATH'] = $RuntimeDirectory + [IO.Path]::PathSeparator + $env:PATH
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    if (-not $process.Start()) { throw 'Cannot start scrolling-layer primitive checks.' }
    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    if ($process.WaitForExit(15000)) { $result = $process.ExitCode }
    else {
        $process.Kill()
        $process.WaitForExit()
        $result = 124
    }
    $text = $standardOutput.GetAwaiter().GetResult() + $standardError.GetAwaiter().GetResult()
    if ($result -eq 124) { $text += "TIMEOUT scrolling-layer primitives exceeded 15 seconds.`n" }
    [IO.File]::WriteAllText((Join-Path $evidence 'result.txt'), $text, [Text.UTF8Encoding]::new($false))
    Write-Output $text.TrimEnd()
} finally { $process.Dispose() }
foreach ($path in @($harness, $PSCommandPath, $manifestPath)) { Copy-Item -LiteralPath $path -Destination $evidence }
[ordered]@{
    schemaVersion = 1; exitCode = $result; scope = 'Native scrolling-layer primitives, not input or presented scrolling acceptance'
    sourceSha256 = Get-WebKitFileHash $harness; scriptSha256 = Get-WebKitFileHash $PSCommandPath
    webCoreSha256 = Get-WebKitFileHash (Join-Path $RuntimeDirectory 'WebCore.dll')
    javaScriptCoreSha256 = Get-WebKitFileHash (Join-Path $RuntimeDirectory 'JavaScriptCore.dll')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
if ($result -ne 0) { throw "Scrolling-layer primitive checks failed; evidence: $evidence" }
Write-Output "PASS scrolling-layer primitives; evidence: $evidence"
