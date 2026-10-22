param(
    [Parameter(Mandatory)][ValidateSet('Pack', 'Unpack')][string]$Mode,
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Archive
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$directoryPath = [IO.Path]::GetFullPath($Directory)
$archivePath = [IO.Path]::GetFullPath($Archive)
if ($Mode -eq 'Pack') {
    if (Test-Path -LiteralPath $archivePath) { throw 'Refusing to overwrite an existing runtime archive' }
    $zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $directoryPath -File -Recurse -Force) {
            $name = $file.FullName.Substring($directoryPath.TrimEnd('\').Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
    exit 0
}
if (Test-Path -LiteralPath $directoryPath) { throw 'Extraction requires a new directory' }
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $total = 0L
    foreach ($entry in $zip.Entries) {
        # Windows PowerShell's legacy ZipFile writer used backslash separators.
        $name = $entry.FullName.Replace('\', '/').TrimEnd('/')
        if (-not $name -or $name -match '[\x00-\x1f\x7f:]') { throw 'Unsafe ZIP entry' }
        foreach ($part in $name.Split('/')) {
            if (-not $part -or $part -in @('.', '..') -or $part -match '[. ]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:[.]|$)') { throw 'Unsafe Windows ZIP path' }
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
        $name = $entry.FullName.Replace('\', '/')
        $target = Join-Path $directoryPath $name
        if ($name.EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
    }
} finally { $zip.Dispose() }
