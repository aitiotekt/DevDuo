param(
    [Parameter(Mandatory)][ValidateSet('Pack', 'Unpack')][string]$Mode,
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Archive
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$directoryPath = [IO.Path]::GetFullPath($Directory)
$archivePath = [IO.Path]::GetFullPath($Archive)
if ($Mode -eq 'Pack') {
    if (Test-Path -LiteralPath $archivePath) { throw 'Refusing to overwrite an existing runtime archive' }
    [IO.Compression.ZipFile]::CreateFromDirectory($directoryPath, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
    exit 0
}
if (Test-Path -LiteralPath $directoryPath) { throw 'Extraction requires a new directory' }
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $total = 0L
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.TrimEnd('/')
        if (-not $name -or $name.Contains('\') -or $name -match '[\x00-\x1f\x7f:]') { throw 'Unsafe ZIP entry' }
        foreach ($part in $name.Split('/')) {
            if (-not $part -or $part -in @('.', '..') -or $part -match '[. ]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw 'Unsafe Windows ZIP path' }
        }
        if (-not $names.Add($name)) { throw 'Duplicate ZIP entry' }
        if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'ZIP links are unsupported' }
        $target = [IO.Path]::GetFullPath((Join-Path $directoryPath $name))
        if (-not $target.StartsWith($directoryPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'ZIP entry escapes extraction directory' }
        $total += $entry.Length
        if ($total -gt 8GB -or $zip.Entries.Count -gt 50000) { throw 'Runtime ZIP exceeds extraction limits' }
    }
    [IO.Directory]::CreateDirectory($directoryPath) | Out-Null
    foreach ($entry in $zip.Entries) {
        $target = Join-Path $directoryPath $entry.FullName
        if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
    }
} finally { $zip.Dispose() }
