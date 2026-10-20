# Update all managed versions; edit the bilingual changelogs separately.
set-version version:
    node scripts/release.mts set-version {{quote(version)}}

# Increment major/minor/patch or select alpha/beta/prerelease/stable.
bump-version level:
    node scripts/release.mts bump-version {{quote(level)}}

sync-version:
    node scripts/release.mts sync-version

check-version:
    node scripts/release.mts check-version
