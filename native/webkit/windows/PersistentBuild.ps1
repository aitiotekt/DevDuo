# Persistent engine build state. Imported after RuntimeFiles.ps1 supplies file hashing.
function Invoke-CheckedGit([string]$directory, [string[]]$arguments) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -C $directory @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Pinned source operation failed; retain the cache and retry after resolving the Git error above' }
    } finally { $ErrorActionPreference = $previousPreference }
}

function Initialize-PinnedSource([string]$directory, [string]$url, [string]$revision, [string[]]$paths) {
    if ($revision -notmatch '^[a-f0-9]{40}$' -or -not $url -or -not $paths.Count) { throw 'Invalid pinned source inputs' }
    $gitDirectory = Join-Path $directory '.git'
    if (-not (Test-Path -LiteralPath $gitDirectory -PathType Container)) {
        if ((Test-Path -LiteralPath $directory) -and @(Get-ChildItem -LiteralPath $directory -Force).Count) {
            throw 'Source cache is not a standalone Git repository; preserve it before replacing'
        }
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Invoke-CheckedGit $directory @('init', '--quiet')
    }
    foreach ($name in @('index.lock', 'HEAD.lock')) {
        if (Test-Path -LiteralPath (Join-Path $gitDirectory $name)) { throw 'Source cache is locked; resolve the active or interrupted Git operation before retrying' }
    }
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $actual = & git -C $directory rev-parse --verify --quiet 'HEAD^{commit}'
        $hasHead = $LASTEXITCODE -eq 0
        $origin = & git -C $directory config --get remote.origin.url
        $hasOrigin = $LASTEXITCODE -eq 0
    } finally { $ErrorActionPreference = $previousPreference }
    if ($hasOrigin -and $origin -cne $url) { throw 'Source cache has a different origin; preserve it before replacing' }
    if ($hasHead) {
        if ($actual -ne $revision) { throw 'Source cache has a different revision; preserve it before replacing' }
        if (-not $hasOrigin) { throw 'Source cache has no origin; preserve it before replacing' }
        Invoke-CheckedGit $directory @('config', 'core.longpaths', 'true')
        Invoke-CheckedGit $directory @('config', 'core.symlinks', 'true')
        return
    }

    # Only an unborn repository can be resumed. Never reset an established tree,
    # staged changes, corrupt refs or locks to make a cache look complete.
    $branch = & git -C $directory symbolic-ref --quiet HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source cache HEAD is invalid; preserve it before replacing' }
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -C $directory show-ref --verify --quiet $branch
        $branchStatus = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    if ($branchStatus -ne 1) { throw 'Source cache HEAD is unreadable; preserve it before replacing' }
    $staged = @(& git -C $directory ls-files)
    if ($LASTEXITCODE -ne 0 -or $staged.Count) { throw 'Incomplete source cache has indexed files; preserve them before retrying' }
    $entries = @(Get-ChildItem -LiteralPath $directory -Force | Where-Object { $_.Name -ne '.git' })
    if (-not $hasOrigin) {
        if ($entries.Count) { throw 'Incomplete source cache has no origin; preserve it before replacing' }
        Invoke-CheckedGit $directory @('remote', 'add', 'origin', $url)
    }
    # Nested caches must not depend on which Windows account launched the build.
    Invoke-CheckedGit $directory @('config', 'core.longpaths', 'true')
    Invoke-CheckedGit $directory @('config', 'core.symlinks', 'true')
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -C $directory cat-file -e ($revision + '^{commit}') 2>$null
        $hasRevision = $LASTEXITCODE -eq 0
    } finally { $ErrorActionPreference = $previousPreference }
    if (-not $hasRevision) {
        Invoke-CheckedGit $directory @('fetch', '--filter=blob:none', '--depth', '1', 'origin', $revision)
    }
    if ($entries.Count) {
        # Failed checkout can leave files without HEAD or an index. Keep every
        # byte (including unknown edits), reuse downloaded Git objects in place,
        # then let an ordinary checkout finish without force/reset/clean.
        $archive = $directory + '.incomplete-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
        [IO.Directory]::CreateDirectory($archive) | Out-Null
        foreach ($entry in $entries) { Move-Item -LiteralPath $entry.FullName -Destination $archive }
        Write-Warning "Preserved incomplete checkout files at $archive; reusing downloaded Git objects"
    }
    Invoke-CheckedGit $directory (@('sparse-checkout', 'set', '--cone', '--') + $paths)
    Invoke-CheckedGit $directory @('checkout', '--detach', $revision)
    $actual = & git -C $directory rev-parse --verify 'HEAD^{commit}'
    if ($LASTEXITCODE -ne 0 -or $actual -ne $revision) { throw 'Pinned source checkout did not establish the required HEAD' }
}

function Test-WebKitPatchApplied([string]$directory, [string[]]$patches) {
    # Windows PowerShell promotes native stderr to an error even when a failed
    # reverse check is the expected cold-cache result. Decide from exit status.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & git -C $directory apply --reverse --check @patches 2>$null
        return $LASTEXITCODE -eq 0
    } finally { $ErrorActionPreference = $previousPreference }
}

function Use-WebKitBuildTree([string]$repository, [string]$build, [string]$identityHash) {
    if ($identityHash -notmatch '^[a-f0-9]{64}$') { throw 'Invalid build tree identity' }
    $buildArchiveRoot = Join-Path $repository 'temp/build/windows-webkit-trees'
    $currentInputs = Join-Path $build 'build-inputs.json'
    $previousInputRoot = $build
    if (Test-Path -LiteralPath $currentInputs) {
        $previous = Get-Content -LiteralPath $currentInputs -Raw | ConvertFrom-Json
        if ($previous.buildTreeIdentity -ne $identityHash) {
            $previousIdentity = $previous.buildTreeIdentity
            if (-not $previousIdentity) { $previousIdentity = Get-WebKitFileHash $currentInputs }
            if ($previousIdentity -notmatch '^[a-f0-9]{64}$') { throw 'Invalid persistent build identity; preserve the build directory before retrying' }
            [IO.Directory]::CreateDirectory($buildArchiveRoot) | Out-Null
            $destination = [IO.Path]::GetFullPath((Join-Path $buildArchiveRoot $previousIdentity))
            $allowed = [IO.Path]::GetFullPath((Join-Path $repository 'temp/build')) + [IO.Path]::DirectorySeparatorChar
            if (-not $destination.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or
                -not ([IO.Path]::GetFullPath($build)).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build archive path escapes temp/build' }
            if (Test-Path -LiteralPath $destination) { throw 'A previous build archive already exists; preserve both trees before retrying' }
            Move-Item -LiteralPath $build -Destination $destination
            $previousInputRoot = $destination
        }
    }
    if (-not (Test-Path -LiteralPath $currentInputs) -and (Test-Path -LiteralPath (Join-Path $build 'CMakeCache.txt'))) {
        throw 'Unrecorded CMake build tree; preserve it before rebuilding'
    }
    $restore = [IO.Path]::GetFullPath((Join-Path $buildArchiveRoot $identityHash))
    if (-not (Test-Path -LiteralPath $build) -and (Test-Path -LiteralPath $restore)) {
        $allowed = [IO.Path]::GetFullPath((Join-Path $repository 'temp/build')) + [IO.Path]::DirectorySeparatorChar
        if (-not $restore.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or
            -not ([IO.Path]::GetFullPath($build)).StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build restore path escapes temp/build' }
        Move-Item -LiteralPath $restore -Destination $build
    }
    return $previousInputRoot
}

# Build the expected diff in a disposable index. Patch order can overlap files, so
# concatenating patch bytes is not a valid comparison with Git's canonical diff.
function Get-WebKitPatchedDiff([string]$source, [string[]]$patches) {
    $temporaryIndex = Join-Path ([IO.Path]::GetTempPath()) ('devduo-patch-index-' + [Guid]::NewGuid().ToString('N'))
    $previousIndex = $env:GIT_INDEX_FILE
    try {
        $env:GIT_INDEX_FILE = $temporaryIndex
        Invoke-CheckedGit $source @('read-tree', 'HEAD')
        foreach ($item in $patches) { Invoke-CheckedGit $source @('apply', '--cached', $item) }
        $lines = & git -C $source diff --cached --binary --no-ext-diff
        if ($LASTEXITCODE -ne 0) { throw 'Cannot compute expected native source diff' }
        return ($lines -join "`n")
    } finally {
        $env:GIT_INDEX_FILE = $previousIndex
        if ([IO.File]::Exists($temporaryIndex)) { [IO.File]::Delete($temporaryIndex) }
    }
}

# Added native headers are part of the maintained patch set. Compare through a
# disposable index so neither the source cache index nor contributor index moves.
function Get-WebKitWorkingDiff([string]$source, [string[]]$addedPaths) {
    $temporaryIndex = Join-Path ([IO.Path]::GetTempPath()) ('devduo-source-index-' + [Guid]::NewGuid().ToString('N'))
    $previousIndex = $env:GIT_INDEX_FILE
    try {
        $env:GIT_INDEX_FILE = $temporaryIndex
        Invoke-CheckedGit $source @('read-tree', 'HEAD')
        Invoke-CheckedGit $source @('add', '--update', '--', '.')
        foreach ($relative in $addedPaths) {
            if ([IO.File]::Exists((Join-Path $source $relative))) {
                Invoke-CheckedGit $source @('add', '--', $relative)
            }
        }
        $lines = & git -C $source diff --cached --binary --no-ext-diff
        if ($LASTEXITCODE -ne 0) { throw 'Cannot compare maintained native source additions' }
        return ($lines -join "`n")
    } finally {
        $env:GIT_INDEX_FILE = $previousIndex
        if ([IO.File]::Exists($temporaryIndex)) { [IO.File]::Delete($temporaryIndex) }
    }
}

function Sync-WebKitPatches([string]$source, [string]$cache, [string]$revision, [string]$build,
    [string]$previousInputRoot, [string[]]$patches) {
    & git -C $source diff --cached --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Native source has staged changes; preserve them before rebuilding' }
    [IO.Directory]::CreateDirectory($build) | Out-Null
    $recordedPatchRoot = Join-Path $cache ('applied-patches/' + $revision)
    $oldPatches = @()
    foreach ($item in $patches) {
        $name = [IO.Path]::GetFileName($item)
        $old = Join-Path $recordedPatchRoot $name
        if (-not (Test-Path -LiteralPath $old)) {
            $old = Join-Path $previousInputRoot ('inputs/native/webkit/windows/patches/' + $name)
        }
        if (-not (Test-Path -LiteralPath $old)) {
            # Older successful builds retain their original input paths and hashes.
            $old = Join-Path $previousInputRoot ('inputs/tests/DevDuo.WebKitProbe/patches/' + $name)
        }
        if (Test-Path -LiteralPath $old) { $oldPatches += $old }
    }
    $expected = Get-WebKitPatchedDiff $source $patches
    $previousExpected = if ($oldPatches.Count) { Get-WebKitPatchedDiff $source $oldPatches } else { '' }
    $addedPaths = @([Regex]::Matches($expected + "`n" + $previousExpected,
        '(?m)^diff --git a/(.+) b/\1\r?\nnew file mode ') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    $untracked = @(& git -C $source ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0 -or @($untracked | Where-Object { $_ -notin $addedPaths }).Count) {
        throw 'Native source has unrecorded files; preserve them before rebuilding'
    }
    $actual = Get-WebKitWorkingDiff $source $addedPaths
    $unchangedCandidates = @()
    if ($actual -ne $expected) {
        if ($actual) {
            if (-not $oldPatches.Count -or $actual -ne $previousExpected) {
                throw 'Persistent source has unrecorded changes; preserve them before rebuilding'
            }
            # Reapplying an overlapping patch set rewrites headers whose final bytes
            # may be unchanged. Retain their timestamps only after exact provenance
            # validation, so Ninja does not rebuild the entire engine unnecessarily.
            $sourcePrefix = [IO.Path]::GetFullPath($source).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            $changedPaths = & git -C $source -c core.quotepath=false diff --name-only --no-renames
            if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect patched source paths' }
            foreach ($relative in $changedPaths) {
                $path = [IO.Path]::GetFullPath((Join-Path $source $relative))
                if (-not $path.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Patched path escapes source directory' }
                if ([IO.File]::Exists($path)) {
                    $unchangedCandidates += [pscustomobject]@{
                        Path = $path; Hash = Get-WebKitFileHash $path; LastWriteTimeUtc = [IO.File]::GetLastWriteTimeUtc($path)
                    }
                }
            }
            for ($i = $oldPatches.Count - 1; $i -ge 0; $i--) {
                Invoke-CheckedGit $source @('apply', '--reverse', '--check', $oldPatches[$i])
                Invoke-CheckedGit $source @('apply', '--reverse', $oldPatches[$i])
            }
        }
        foreach ($item in $patches) {
            Invoke-CheckedGit $source @('apply', '--check', $item)
            Invoke-CheckedGit $source @('apply', $item)
        }
    }
    if ((Get-WebKitWorkingDiff $source $addedPaths) -ne $expected) {
        throw 'Source cache differs from the pinned revision plus the maintained patches'
    }
    foreach ($candidate in $unchangedCandidates) {
        if ([IO.File]::Exists($candidate.Path) -and (Get-WebKitFileHash $candidate.Path) -eq $candidate.Hash) {
            [IO.File]::SetLastWriteTimeUtc($candidate.Path, $candidate.LastWriteTimeUtc)
        }
    }
    [IO.File]::WriteAllText((Join-Path $build 'upstream-working.patch'), $expected + "`n", [Text.UTF8Encoding]::new($false))
    [IO.Directory]::CreateDirectory($recordedPatchRoot) | Out-Null
    foreach ($item in $patches) {
        Copy-Item -LiteralPath $item -Destination (Join-Path $recordedPatchRoot ([IO.Path]::GetFileName($item))) -Force
    }
}
