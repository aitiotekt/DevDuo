# Install each ecosystem's locked dependencies, then restore the native target.
setup: doctor setup-python setup-docs
    dotnet restore src/DevDuo --locked-mode
    dotnet restore tests/DevDuo.Core.Tests --locked-mode
    dotnet restore tests/DevDuo.Localization.Tests --locked-mode
    dotnet restore tests/DevDuo.Debugging.Tests --locked-mode

# Hosted portable checks do not restore or compile an application backend.
setup-ci: doctor setup-python setup-docs
    dotnet restore tests/DevDuo.Core.Tests --locked-mode
    dotnet restore tests/DevDuo.Localization.Tests --locked-mode
    dotnet restore tests/DevDuo.Debugging.Tests --locked-mode

# Native compilation needs the offline debugging assets, not Python or docs.
setup-native: setup-release-tools
    node scripts/docs.mts doctor
    dotnet restore src/DevDuo --locked-mode

# Release planning/publication does not need the docs, Python or native SDK.
setup-release-tools:
    pnpm install --frozen-lockfile

# The docs workspace can be set up without Python or the native SDK.
setup-docs: setup-release-tools
    pnpm --dir docsite install --frozen-lockfile
    node scripts/docs.mts sync

setup-python:
    uv sync --locked --all-groups

doctor:
    node --version
    pnpm --version
    just --version
    prek --version
    python --version
    uv --version
    dotnet --version
    node scripts/docs.mts doctor
