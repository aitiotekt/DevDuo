import { chmodSync, existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { repoRoot } from "./paths.mts";

export function packageMacOS(): void {
  if (process.platform !== "darwin")
    throw new Error("macOS packaging requires macOS and Xcode.");
  const output = join(repoRoot, "temp/DevDuo.app/Contents/MacOS");
  if (!existsSync(join(output, "DevDuo")))
    throw new Error(
      "Publish the macOS application with just package-macos first.",
    );
  const version = (
    JSON.parse(readFileSync(join(repoRoot, "package.json"), "utf8")) as {
      version: string;
    }
  ).version.split("-")[0];
  writeFileSync(
    join(output, "../Info.plist"),
    `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>CFBundleIdentifier</key><string>local.DevDuo.Calibration</string><key>CFBundleName</key><string>DevDuo</string><key>CFBundleDisplayName</key><string>DevDuo</string><key>CFBundleShortVersionString</key><string>${version}</string><key>LSMinimumSystemVersion</key><string>27.0</string><key>CFBundleExecutable</key><string>launch</string><key>CFBundlePackageType</key><string>APPL</string><key>CFBundleVersion</key><string>1</string><key>NSHighResolutionCapable</key><true/><key>NSAppTransportSecurity</key><dict><key>NSAllowsLocalNetworking</key><true/></dict></dict></plist>
`,
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
exec "$(dirname "$0")/DevDuo" "$@"
`,
  );
  chmodSync(join(output, "launch"), 0o755);
  console.log("Created unsigned temp/DevDuo.app (requires .NET 10 runtime).");
}
