param(
    [switch]$Hold,
    [switch]$HostApi,
    [string]$RuntimeDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Evidence.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pin = Get-Content -LiteralPath (Join-Path $repository 'native/webkit/windows/runtime.lock.json') -Raw | ConvertFrom-Json
if (-not $RuntimeDirectory) { $RuntimeDirectory = Join-Path $repository $pin.runtimeDirectory }
$runtime = [IO.Path]::GetFullPath($RuntimeDirectory)
$binary = Assert-ProbeBuild $repository
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'WebKit2.dll'))) { throw 'WebKit runtime missing' }
if ($HostApi) {
    $manifestPath = Join-Path (Split-Path -Parent $runtime) 'runtime-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Complete just build-windows-webkit-runtime before host API acceptance' }
    $runtimeManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($runtimeManifest.schemaVersion -ne 1 -or $runtimeManifest.hostViewportApiVersion -ne 2) {
        throw 'Unsupported source-built runtime manifest'
    }
}
$probe = Join-Path $runtime 'DevDuo.WebKitProbe.exe'
Copy-Item -LiteralPath $binary -Destination $probe -ErrorAction Stop
$session = Join-Path $repository ('captures/desktop-validation/windows-webkit/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-ordinary')
[IO.Directory]::CreateDirectory((Join-Path $session 'sources/pages')) | Out-Null
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $binary) 'build-inputs.json') -Destination (Join-Path $session 'probe-build-inputs.json')
if ($HostApi) {
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $session 'runtime-manifest.json')
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $runtime) 'build-inputs.json') -Destination (Join-Path $session 'runtime-build-inputs.json')
    foreach ($buildInput in $runtimeManifest.inputFiles) {
        $frozenRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $runtime) 'inputs'))
        $path = [IO.Path]::GetFullPath((Join-Path $frozenRoot $buildInput.path))
        if (-not $path.StartsWith($frozenRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Frozen native build input path escapes its directory'
        }
        $destination = Join-Path (Join-Path $session 'sources/runtime-inputs') $buildInput.path
        [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
        Copy-Item -LiteralPath $path -Destination $destination
    }
}
$files = @('Program.cpp', 'WebKitApi.h', 'ContinuationSurface.h', 'AcceptanceProbe.h', 'acceptance-observer.js',
    'DevDuo.WebKitProbe.vcxproj', 'RunAcceptance.ps1', 'Evidence.ps1',
    'pages/standards.html', 'pages/overflow.html', 'pages/quirks.html', 'pages/static-gradient.html',
    'pages/transparent.html', 'pages/static-quirks.html', 'pages/dynamic-height.html', 'pages/live-glass.html')
$records = foreach ($file in $files) {
    $source = Join-Path $PSScriptRoot $file
    Copy-Item -LiteralPath $source -Destination (Join-Path $session ('sources/' + $file))
    [ordered]@{ path = ('tests/DevDuo.WebKitProbe/' + $file); sha256 = Get-WebKitFileHash $source }
}
$observer = Join-Path $repository 'src/DevDuo/WebAssets/Scripts/browser-observer.js'
Copy-Item -LiteralPath $observer -Destination (Join-Path $session 'sources/browser-observer.js')
$records += [ordered]@{ path = 'src/DevDuo/WebAssets/Scripts/browser-observer.js'; sha256 = Get-WebKitFileHash $observer }
foreach ($relative in @('native/webkit/windows/runtime.lock.json', 'native/webkit/windows/RuntimeFiles.ps1')) {
    $source = Join-Path $repository $relative
    $destination = Join-Path (Join-Path $session 'sources') $relative
    [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $records += [ordered]@{ path = $relative; sha256 = Get-WebKitFileHash $source }
}
[ordered]@{
    schemaVersion = 1; upstreamRuntime = $pin; sourceFiles = @($records)
    probeSha256 = Get-WebKitFileHash $probe
    webKitSha256 = Get-WebKitFileHash (Join-Path $runtime 'WebKit2.dll')
    hostApiRequired = [bool]$HostApi; testOnlyIPCEnabled = -not [bool]$HostApi
    status = 'paired-layout diagnostic; presented-window acceptance is separate'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $session 'run.json') -Encoding utf8

# The test server serves only reviewed local fixture assets on loopback. Python
# comes from the repository's selected mise/uv environment, without downloads.
$server = Start-Job -ScriptBlock {
    param($root, $pages)
    Set-Location -LiteralPath $root
    & mise exec -- uv run --locked python -m http.server 8877 --bind 127.0.0.1 --directory $pages
} -ArgumentList $repository, (Join-Path $PSScriptRoot 'pages')
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; ++$attempt) {
        if ($server.State -eq 'Failed' -or $server.State -eq 'Completed') {
            Receive-Job $server
            throw 'Local fixture server failed to start'
        }
        try {
            $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8877/standards.html' -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200 -and $response.Content.Contains('Standards layout acceptance')) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Fixture server did not become ready; ensure loopback port 8877 is free' }
    $arguments = @('--acceptance', $repository, (Join-Path $session 'native'), 'http://127.0.0.1:8877/')
    if ($HostApi) { $arguments += '--host-api' }
    if ($Hold) { $arguments += '--hold' }
    Write-Host ('Ordinary-page evidence session: ' + $session)
    & $probe @arguments
    $probeExitCode = $LASTEXITCODE
    [ordered]@{ exitCode = $probeExitCode; productionIntegrationAccepted = $false } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $session 'process-result.json') -Encoding utf8
} finally {
    Stop-Job $server
    Receive-Job $server -ErrorAction Continue 2>&1 | Out-File -LiteralPath (Join-Path $session 'server.log') -Encoding utf8
    Remove-Job $server
}
exit $probeExitCode
