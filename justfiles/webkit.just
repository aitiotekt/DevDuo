# Engine publication is independent from the application release workflow.
test-webkit-release-tools:
    node --test scripts/__tests__/webkit-release.test.mts scripts/__tests__/webkit-source.test.mts

plan-webkit-release:
    node scripts/webkit-release.mts plan

# Read-only lookup of the metadata version's published release asset.
preflight-webkit-release:
    node scripts/webkit-release.mts preflight

release-preflight: release-check preflight-webkit-release

[windows]
package-webkit-release:
    node scripts/webkit-release.mts package

publish-webkit-release:
    node scripts/webkit-release.mts publish

[windows]
fetch-windows-webkit-runtime:
    node scripts/webkit-release.mts fetch

[windows]
bundle-windows-webkit-runtime:
    node scripts/webkit-release.mts bundle
