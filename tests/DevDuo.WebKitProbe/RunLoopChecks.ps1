param([string]$RuntimeDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../native/webkit/windows/RuntimeFiles.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build = Join-Path $repository 'temp/build/windows-webkit-host'
$pin = Get-Content -LiteralPath (Join-Path $repository 'native/webkit/windows/runtime.lock.json') -Raw | ConvertFrom-Json
$source = Join-Path $repository ('temp/cache/windows-webkit/source-' + $pin.revision.Substring(0, 8))
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $build 'bin' }
$RuntimeDirectory = [IO.Path]::GetFullPath($RuntimeDirectory)
$output = Join-Path $repository 'temp/build/windows-run-loop-checks'
$evidence = Join-Path $repository ('captures/desktop-validation/windows-run-loop/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
foreach ($path in @((Join-Path $build 'lib/JavaScriptCore.lib'), (Join-Path $RuntimeDirectory 'JavaScriptCore.dll'),
    (Join-Path $source 'Source/WTF/wtf/RunLoop.h'))) {
    if (-not [IO.File]::Exists($path)) { throw 'Build the pinned runtime with just build-windows-webkit-runtime before testing its run loop.' }
}
[IO.Directory]::CreateDirectory($output) | Out-Null
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$installation = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ and Windows SDK are required.' }
Import-Module (Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
$clang = Join-Path $installation 'VC/Tools/Llvm/x64/bin/clang-cl.exe'
$harness = Join-Path $PSScriptRoot 'RunLoopChecks.cpp'
$guardHeader = Join-Path $source 'Source/WebKit/UIProcess/win/HostProcessCapabilityTransfer.h'
$binary = Join-Path $output 'RunLoopChecks.exe'
# The import library resolves real WTF implementations. Suppress dllimport on
# inline template members that the DLL does not export, so the harness emits them.
$arguments = @('/nologo', '/clang:-std=c++23', '/MD', '/O2', '/EHs-c-', '/GR-', '/DNDEBUG', '/DNOMINMAX',
    '/DWTF_EXPORT_PRIVATE=', '/DBUILDING_WITH_CMAKE', '/DHAVE_CONFIG_H', '/D_HAS_EXCEPTIONS=0',
    '/DWIN32', '/D_WINDOWS', '/D_WIN32_WINNT=0x0A00',
    ('/I' + $build), ('/I' + (Join-Path $build 'WTF/DerivedSources')), ('/I' + (Join-Path $source 'Source/WTF')),
    ('/I' + (Join-Path $source 'Source/WTF/wtf')), ('/I' + (Join-Path $source 'Source/WebKit/UIProcess')), ('/I' + (Join-Path $build 'bmalloc/Headers')),
    ('/I' + (Join-Path $build 'bmalloc/PrivateHeaders')), ('/I' + (Join-Path $build 'vcpkg_installed/x64-windows-devduo/include')),
    $harness, ('/Fo' + (Join-Path $output 'RunLoopChecks.obj')), ('/Fe' + $binary),
    '/link', (Join-Path $build 'lib/JavaScriptCore.lib'), 'user32.lib')
& $clang @arguments
if ($LASTEXITCODE -ne 0) { throw 'Run-loop native primitive compilation failed.' }
$env:PATH = $RuntimeDirectory + [IO.Path]::PathSeparator + $env:PATH
$start = [Diagnostics.ProcessStartInfo]::new($binary)
$start.CreateNoWindow = $true
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    if (-not $process.Start()) { throw 'Cannot start the native run-loop harness.' }
    $standardOutput = $process.StandardOutput.ReadToEndAsync()
    $standardError = $process.StandardError.ReadToEndAsync()
    if ($process.WaitForExit(15000)) {
        $result = $process.ExitCode
    } else {
        # Terminate only the exact harness process created above.
        $process.Kill()
        $process.WaitForExit()
        $result = 124
    }
    $text = $standardOutput.GetAwaiter().GetResult() + $standardError.GetAwaiter().GetResult()
    if ($result -eq 124) { $text += "TIMEOUT native run-loop harness exceeded 15 seconds.`n" }
    [IO.File]::WriteAllText((Join-Path $evidence 'result.txt'), $text, [Text.UTF8Encoding]::new($false))
    Write-Output $text.TrimEnd()
} finally {
    $process.Dispose()
}
Copy-Item -LiteralPath $harness -Destination $evidence
Copy-Item -LiteralPath $guardHeader -Destination $evidence
Copy-Item -LiteralPath (Join-Path $build 'runtime-manifest.json') -Destination $evidence
Copy-Item -LiteralPath $PSCommandPath -Destination $evidence
[ordered]@{
    schemaVersion = 1; upstreamRevision = $pin.revision; exitCode = $result
    sourceSha256 = Get-WebKitFileHash $harness; scriptSha256 = Get-WebKitFileHash $PSCommandPath
    pushGuardSha256 = Get-WebKitFileHash $guardHeader
    runtimeManifestSha256 = Get-WebKitFileHash (Join-Path $build 'runtime-manifest.json')
    binarySha256 = Get-WebKitFileHash $binary; compilerSha256 = Get-WebKitFileHash $clang
    javaScriptCoreSha256 = Get-WebKitFileHash (Join-Path $RuntimeDirectory 'JavaScriptCore.dll')
    windowsSDK = $env:WindowsSDKVersion; vcTools = $env:VCToolsVersion
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
if ($result -ne 0) { throw "Native run-loop acceptance failed; evidence: $evidence" }
Write-Output "PASS actual WTF timers under external DispatchMessage, own run loop and cycle; evidence: $evidence"
