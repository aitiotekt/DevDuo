param(
    [switch]$Hold,
    [switch]$StartInset,
    [switch]$ForceCompositing,
    [switch]$ExtendBackground,
    [switch]$SiteIsolation,
    [switch]$Raw
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Evidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pin = Get-Content -LiteralPath (Join-Path $repository 'native/webkit/windows/runtime.lock.json') -Raw | ConvertFrom-Json
$runtime = Join-Path $repository $pin.runtimeDirectory
$builtProbe = Assert-ProbeBuild $repository
$probe = Join-Path $runtime 'DevDuo.WebKitProbe.exe'
if (-not (Test-Path -LiteralPath $builtProbe)) { throw 'Run just build-windows-webkit-probe first' }
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'WebKit2.dll'))) { throw 'Run just prepare-windows-webkit-probe first' }
Copy-Item -LiteralPath $builtProbe -Destination $probe -ErrorAction Stop

$session = Join-Path $repository ('captures/desktop-validation/windows-webkit/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
if (Test-Path -LiteralPath $session) { throw 'Probe session already exists' }
[IO.Directory]::CreateDirectory((Join-Path $session 'sources')) | Out-Null
$sources = @('Program.cpp', 'WebKitApi.h', 'ContinuationSurface.h', 'AcceptanceProbe.h', 'DevDuo.WebKitProbe.vcxproj', 'probe.html',
    'PrepareRuntime.ps1', 'RunProbe.ps1', 'Evidence.ps1')
$sourceRecords = foreach ($name in $sources) {
    $source = Join-Path $PSScriptRoot $name
    Copy-Item -LiteralPath $source -Destination (Join-Path $session ('sources/' + $name))
    [ordered]@{ path = ('tests/DevDuo.WebKitProbe/' + $name); sha256 = (Get-WebKitFileHash $source) }
}
$observer = Join-Path $repository 'src/DevDuo/WebAssets/Scripts/browser-observer.js'
Copy-Item -LiteralPath $observer -Destination (Join-Path $session 'sources/browser-observer.js')
$sourceRecords += [ordered]@{ path = 'src/DevDuo/WebAssets/Scripts/browser-observer.js'; sha256 = (Get-WebKitFileHash $observer) }
foreach ($relative in @('native/webkit/windows/runtime.lock.json', 'native/webkit/windows/RuntimeFiles.ps1')) {
    $source = Join-Path $repository $relative
    $destination = Join-Path (Join-Path $session 'sources') $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $sourceRecords += [ordered]@{ path = $relative; sha256 = Get-WebKitFileHash $source }
}
$arguments = @((Join-Path $PSScriptRoot 'probe.html'), (Join-Path $session 'native'))
if ($Hold) { $arguments += '--hold' }
if ($StartInset) { $arguments += '--start-inset' }
if ($ForceCompositing) { $arguments += '--force-compositing' }
if ($ExtendBackground) { $arguments += '--extend-background' }
if ($SiteIsolation) { $arguments += '--site-isolation' }
if ($Raw) { $arguments += '--raw' }
$metadata = [ordered]@{
    schemaVersion = 1; runtime = $pin; sourceFiles = @($sourceRecords)
    probeSha256 = (Get-WebKitFileHash $probe)
    webKitSha256 = (Get-WebKitFileHash (Join-Path $runtime 'WebKit2.dll'))
    options = @($arguments | Select-Object -Skip 2)
    status = 'diagnostic-only; requires separate presented-window acceptance'
}
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $session 'run.json') -Encoding utf8
Write-Host ('Probe evidence session: ' + $session)
& $probe @arguments
$probeExitCode = $LASTEXITCODE
[ordered]@{ exitCode = $probeExitCode; nativeContinuationAccepted = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $session 'process-result.json') -Encoding utf8
exit $probeExitCode
