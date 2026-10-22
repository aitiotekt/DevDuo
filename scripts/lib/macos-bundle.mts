import {
  closeSync,
  existsSync,
  mkdirSync,
  openSync,
  readdirSync,
  readSync,
  renameSync,
  symlinkSync,
} from "node:fs";
import { join } from "node:path";
import type { DevDuoMetadata } from "./metadata.mts";
import { run } from "./release-process.mts";
import { parseVersion } from "./release-version.mts";

function isMachO(path: string): boolean {
  const descriptor = openSync(path, "r"),
    header = Buffer.alloc(4);
  try {
    readSync(descriptor, header, 0, 4, 0);
  } finally {
    closeSync(descriptor);
  }
  return [
    "cffaedfe",
    "cefaedfe",
    "feedfacf",
    "feedface",
    "cafebabe",
    "bebafeca",
    "cafebabf",
    "bfbafeca",
  ].includes(header.toString("hex"));
}

export function relocateMacOSResources(app: string): void {
  const contents = join(app, "Contents"),
    resources = join(contents, "Resources");
  mkdirSync(resources, { recursive: true });
  for (const entry of readdirSync(join(contents, "MacOS"), {
    withFileTypes: true,
  })) {
    const source = join(contents, "MacOS", entry.name);
    if (entry.isSymbolicLink()) continue;
    if (entry.isFile() && isMachO(source)) {
      // .NET resolves its managed entry assembly in Resources, so its local
      // hostfxr and P/Invoke search must still reach the native runtime.
      const alias = join(resources, entry.name);
      if (entry.name.endsWith(".dylib") && !existsSync(alias))
        symlinkSync(`../MacOS/${entry.name}`, alias, "file");
      continue;
    }
    renameSync(source, join(resources, entry.name));
    // Keep AppContext.BaseDirectory lookups working without treating the data
    // directory as nested executable code during signing.
    symlinkSync(
      `../Resources/${entry.name}`,
      source,
      entry.isDirectory() ? "dir" : "file",
    );
  }
}

export function signMacOSBundle(
  app: string,
  executable: string,
  identity: string,
  entitlements: string,
  execute: typeof run = run,
): void {
  const main = join(app, "Contents/MacOS", executable);
  const sign = (path: string) =>
    execute("codesign", [
      "--force",
      "--options",
      "runtime",
      identity === "-" ? "--timestamp=none" : "--timestamp",
      "--entitlements",
      entitlements,
      "--sign",
      identity,
      path,
    ]);
  const signNested = (directory: string): void => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) signNested(path);
      else if (entry.isFile() && path !== main && isMachO(path)) sign(path);
    }
  };
  signNested(app);
  // Signing the bundle also signs its main executable; do this after dylibs.
  sign(app);
}

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
