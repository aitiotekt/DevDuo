. (Join-Path $PSScriptRoot '../../native/webkit/windows/RuntimeFiles.ps1')

function Write-ProbeBuildManifest([string]$Repository) {
    $binary = Join-Path $Repository 'temp/build/windows-webkit-probe/Release/DevDuo.WebKitProbe.exe'
    $sources = @('tests/DevDuo.WebKitProbe/Program.cpp', 'tests/DevDuo.WebKitProbe/WebKitApi.h',
        'tests/DevDuo.WebKitProbe/ContinuationSurface.h', 'tests/DevDuo.WebKitProbe/AcceptanceProbe.h',
        'tests/DevDuo.WebKitProbe/DevDuo.WebKitProbe.vcxproj', 'src/DevDuo/app.manifest')
    $records = foreach ($relative in $sources) {
        [ordered]@{ path = $relative; sha256 = Get-WebKitFileHash (Join-Path $Repository $relative) }
    }
    [ordered]@{ schemaVersion = 1; probeSha256 = Get-WebKitFileHash $binary; sourceFiles = @($records) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $binary) 'build-inputs.json') -Encoding utf8
}

function Assert-ProbeBuild([string]$Repository) {
    $binary = Join-Path $Repository 'temp/build/windows-webkit-probe/Release/DevDuo.WebKitProbe.exe'
    $manifestPath = Join-Path (Split-Path -Parent $binary) 'build-inputs.json'
    if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $binary)) {
        throw 'Run just build-windows-webkit-probe successfully before launching'
    }
    return $binary
}
