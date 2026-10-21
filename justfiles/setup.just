# Install each ecosystem's locked dependencies, then restore the native target.
setup: doctor setup-python setup-docs
    dotnet restore src/DevDuo --locked-mode
    dotnet restore tests/DevDuo.Localization.Tests --locked-mode

# The docs workspace can be set up without Python or the native SDK.
setup-docs:
    pnpm install --frozen-lockfile
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
