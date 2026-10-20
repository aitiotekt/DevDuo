import { chmodSync, existsSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { macosBundlePlist } from "./macos-bundle.mts";
import { loadMetadata } from "./metadata.mts";
import { repoRoot } from "./paths.mts";
import { checkVersion } from "./version.mts";

export function packageMacOS(): void {
  if (process.platform !== "darwin")
    throw new Error("macOS packaging requires macOS and Xcode.");
  const metadata = loadMetadata();
  const output = join(
    repoRoot,
    `temp/${metadata.project.binaryName}.app/Contents/MacOS`,
  );
  if (!existsSync(join(output, metadata.project.binaryName)))
    throw new Error(
      "Publish the macOS application with just package-macos first.",
    );
  checkVersion();
  writeFileSync(
    join(output, "../Info.plist"),
    macosBundlePlist(metadata, "launch"),
  );
  writeFileSync(
    join(output, "launch"),
    `#!/bin/zsh
set -euo pipefail
if [[ -z "\${DOTNET_ROOT:-}" ]]; then
  for candidate in "$HOME/.local/share/mise/dotnet-root" /opt/homebrew/opt/dotnet/libexec /usr/local/share/dotnet /usr/local/share/dotnet/x64; do
    if [[ -x "$candidate/dotnet" ]]; then export DOTNET_ROOT="$candidate"; break; fi
  done
fi
exec "$(dirname "$0")/${metadata.project.binaryName}" "$@"
`,
  );
  chmodSync(join(output, "launch"), 0o755);
  console.log("Created unsigned temp/DevDuo.app (requires .NET 10 runtime).");
}
