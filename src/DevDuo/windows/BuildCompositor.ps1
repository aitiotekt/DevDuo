$ErrorActionPreference = 'Stop'
$installation = & "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe" -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ tools and Windows SDK are required for native composition' }
& (Join-Path $installation 'MSBuild/Current/Bin/MSBuild.exe') @((Join-Path $PSScriptRoot 'DevDuo.Windows.Compositor.vcxproj'), '/p:Configuration=Release', '/p:Platform=x64', '/nologo', '/verbosity:minimal')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path $repository 'temp/build/windows-compositor'
function Get-SourceHash([string]$path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
$records = foreach ($relative in @('src/DevDuo/windows/HostCompositor.cpp', 'src/DevDuo/windows/HostCompositor.h', 'src/DevDuo/windows/DevDuo.Windows.Compositor.vcxproj', 'src/DevDuo/windows/BuildCompositor.ps1')) {
    [ordered]@{ path = $relative; sha256 = Get-SourceHash (Join-Path $repository $relative) }
}
[ordered]@{ schemaVersion = 1; apiVersion = 2; presentationApiVersion = 1; sources = @($records); binarySha256 = Get-SourceHash (Join-Path $output 'DevDuo.Windows.Compositor.dll'); nativeAcceptancePassed = $false } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
exit 0
