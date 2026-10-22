# Update all managed versions; edit the bilingual changelogs separately.
set-version version:
    node scripts/release.mts set-version {{ quote(version) }}

# Increment major/minor/patch or select alpha/beta/prerelease/stable.
bump-version level:
    node scripts/release.mts bump-version {{ quote(level) }}

sync-version:
    node scripts/release.mts sync-version

check-version:
    node scripts/release.mts check-version

# Raster exports use the locked image toolchain; Apple compilation requires Xcode.
export-icons:
    uv run --locked devduo-tools icons export

[macos]
sync-icons: export-icons
    uv run --locked devduo-tools icons compile

check-icons:
    uv run --locked devduo-tools icons check
