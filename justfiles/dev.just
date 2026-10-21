# For custom arguments, invoke dotnet or the Python CLI directly.
dev:
    dotnet run --project src/DevDuo -p:RestoreLockedMode=true

serve:
    uv run --locked devduo-tools serve

docs: docs-check
    pnpm --dir docsite dev

docs-preview: docs-check
    pnpm --dir docsite preview
