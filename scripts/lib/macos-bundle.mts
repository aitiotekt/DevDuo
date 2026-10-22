import type { DevDuoMetadata } from "./metadata.mts";
import { parseVersion } from "./release-version.mts";

function xml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&apos;");
}

export function macosBundlePlist(
  metadata: DevDuoMetadata,
  executable: string,
): string {
  const core = parseVersion(metadata.project.version).core;
  return `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>${xml(metadata.project.identifier)}</string>
<key>CFBundleName</key><string>${xml(metadata.project.displayName)}</string>
<key>CFBundleDisplayName</key><string>${xml(metadata.project.displayName)}</string>
<key>CFBundleExecutable</key><string>${xml(executable)}</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconName</key><string>AppIcon</string>
<key>CFBundleIconFile</key><string>AppIcon.icns</string>
<key>CFBundleShortVersionString</key><string>${core}</string>
<key>CFBundleVersion</key><string>${core}</string>
<key>LSMinimumSystemVersion</key><string>${metadata.macos.minimumSystemVersion}</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSAppTransportSecurity</key><dict><key>NSAllowsLocalNetworking</key><true/></dict>
</dict></plist>\n`;
}
