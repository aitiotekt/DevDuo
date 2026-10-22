$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot '../../native/webkit/windows/RuntimeFiles.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$pin = Get-Content -LiteralPath (Join-Path $repository 'native/webkit/windows/runtime.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repository $pin.cacheDirectory
$runtime = Join-Path $repository $pin.runtimeDirectory
$packageRoot = Split-Path $runtime
$archivePath = Join-Path $cache 'archive.zip'
[IO.Directory]::CreateDirectory($cache) | Out-Null

if (-not (Test-Path -LiteralPath $archivePath)) {
    $partial = $archivePath + '.partial'
    Write-Host ('Downloading official WebKit ' + $pin.identifier)
    & curl.exe @('--fail', '--location', '--silent', '--show-error', '--retry', '2',
        '--connect-timeout', '15', '--max-time', '900', '--output', $partial, $pin.archiveUrl)
    if ($LASTEXITCODE -ne 0) {
        throw 'Official archive download failed. CI archives expire; retain the cache or deliberately update the pin.'
    }
    Move-Item -LiteralPath $partial -Destination $archivePath
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $resolvedRoot = [IO.Path]::GetFullPath($packageRoot) + [IO.Path]::DirectorySeparatorChar
    foreach ($entry in $zip.Entries) {
        if (-not $entry.Name -or $entry.FullName -match '\.(pdb|ilk)$') { continue }
        $destination = [IO.Path]::GetFullPath((Join-Path $packageRoot $entry.FullName))
        if (-not $destination.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Archive entry escapes the isolated runtime directory'
        }
        [IO.Directory]::CreateDirectory((Split-Path $destination)) | Out-Null
        if (Test-Path -LiteralPath $destination) { continue }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
    }
} finally { $zip.Dispose() }
Write-Host ('Official WebKit runtime ready: ' + $pin.identifier)
