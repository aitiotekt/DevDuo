import { createHash } from "node:crypto";
import {
  closeSync,
  copyFileSync,
  lstatSync,
  mkdirSync,
  openSync,
  readdirSync,
  readFileSync,
  readSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { type DevDuoMetadata, loadMetadata } from "./metadata.mts";
import {
  artifactName,
  type ReleasePlan,
  releasePlan,
  type Target,
  targets,
} from "./release-policy.mts";

export function digest(path: string): string {
  const hash = createHash("sha256"),
    buffer = Buffer.allocUnsafe(1024 * 1024);
  const descriptor = openSync(path, "r");
  try {
    while (true) {
      const count = readSync(descriptor, buffer, 0, buffer.length, null);
      if (count === 0) break;
      hash.update(buffer.subarray(0, count));
    }
    return hash.digest("hex");
  } finally {
    closeSync(descriptor);
  }
}

export function windowsRuntime(metadata = loadMetadata()) {
  const config = metadata.webkit;
  if (!/^[a-f0-9]{64}$/.test(config.archiveSha256))
    throw new Error(
      "Publish and pin the metadata WebKit runtime before releasing the application.",
    );
  return {
    version: config.version,
    tag: `${config.tagPrefix}${config.version}`,
    asset: `${config.assetPrefix}-${config.version}-${config.target}.zip`,
    archiveSha256: config.archiveSha256,
    upstreamRevision: config.upstreamRevision,
  };
}
export type Artifact = {
  name: string;
  target: Target;
  sha256: string;
  size: number;
  signed: boolean;
  notarized: boolean;
};
export type Manifest = {
  schemaVersion: 1;
  version: string;
  sourceSha: string;
  sourceRef: string;
  artifacts: Artifact[];
  windowsWebKit: ReturnType<typeof windowsRuntime>;
};

export function artifactRecord(
  directory: string,
  version: string,
  target: Target,
): Artifact {
  const name = artifactName(version, target),
    path = join(directory, name);
  if (!lstatSync(path).isFile() || lstatSync(path).size === 0)
    throw new Error(`Missing artifact: ${name}`);
  return {
    name,
    target,
    sha256: digest(path),
    size: lstatSync(path).size,
    signed: target === "osx-arm64",
    notarized: target === "osx-arm64",
  };
}

export function assemble(
  input: string,
  output: string,
  plan: ReleasePlan,
  metadata: DevDuoMetadata = loadMetadata(),
): void {
  mkdirSync(output, { recursive: true });
  const artifacts = targets.map((target) => {
    const directory = join(input, `release-${target}`);
    const report = JSON.parse(
      readFileSync(join(directory, "report.json"), "utf8"),
    );
    const actual = artifactRecord(directory, plan.version, target);
    if (
      report.sourceSha !== plan.sourceSha ||
      (target === "win-x64" &&
        JSON.stringify(report.windowsWebKit) !==
          JSON.stringify(windowsRuntime(metadata))) ||
      JSON.stringify(report.artifact) !== JSON.stringify(actual)
    )
      throw new Error(`Invalid platform report for ${target}`);
    if (
      readdirSync(directory).sort().join() !==
      [actual.name, "report.json"].sort().join()
    )
      throw new Error(`Unexpected platform files for ${target}`);
    copyFileSync(join(directory, actual.name), join(output, actual.name));
    return actual;
  });
  const manifest: Manifest = {
    schemaVersion: 1,
    version: plan.version,
    sourceSha: plan.sourceSha,
    sourceRef: plan.sourceRef,
    artifacts,
    windowsWebKit: windowsRuntime(metadata),
  };
  writeFileSync(
    join(output, "release-manifest.json"),
    `${JSON.stringify(manifest, null, 2)}\n`,
  );
  writeFileSync(join(output, "SHA256SUMS"), checksumText(manifest, output));
  verifyArtifacts(output, plan, metadata);
}

function checksumText(manifest: Manifest, directory: string): string {
  return `${[
    ...manifest.artifacts.map((a) => `${a.sha256}  ${a.name}`),
    `${digest(join(directory, "release-manifest.json"))}  release-manifest.json`,
  ].join("\n")}\n`;
}

export function verifyArtifacts(
  directory: string,
  expected: ReleasePlan,
  metadata: DevDuoMetadata = loadMetadata(),
): Manifest {
  const manifest = JSON.parse(
    readFileSync(join(directory, "release-manifest.json"), "utf8"),
  ) as Manifest;
  releasePlan(manifest.version, manifest.sourceRef, manifest.sourceSha);
  if (
    manifest.schemaVersion !== 1 ||
    manifest.version !== expected.version ||
    manifest.sourceSha !== expected.sourceSha ||
    manifest.sourceRef !== expected.sourceRef ||
    JSON.stringify(manifest.windowsWebKit) !==
      JSON.stringify(windowsRuntime(metadata)) ||
    !Array.isArray(manifest.artifacts) ||
    manifest.artifacts.length !== targets.length
  )
    throw new Error("Release manifest does not match the approved source.");
  for (const target of targets) {
    const entries = manifest.artifacts.filter((a) => a.target === target);
    const actual = artifactRecord(directory, manifest.version, target);
    if (
      entries.length !== 1 ||
      Object.keys(actual).some(
        (key) =>
          entries[0][key as keyof Artifact] !== actual[key as keyof Artifact],
      )
    )
      throw new Error(
        `Artifact integrity or signing metadata mismatch: ${target}`,
      );
  }
  const expectedNames = [
    ...manifest.artifacts.map((a) => a.name),
    "release-manifest.json",
    "SHA256SUMS",
  ].sort();
  if (readdirSync(directory).sort().join() !== expectedNames.join())
    throw new Error("Unexpected release files.");
  if (
    readFileSync(join(directory, "SHA256SUMS"), "utf8") !==
    checksumText(manifest, directory)
  )
    throw new Error("Checksum list mismatch.");
  return manifest;
}

export function verifyPublished(
  current: string,
  existing: string,
  plan: ReleasePlan,
  metadata: DevDuoMetadata = loadMetadata(),
): void {
  verifyArtifacts(current, plan, metadata);
  verifyArtifacts(existing, plan, metadata);
  for (const file of readdirSync(current))
    if (digest(join(current, file)) !== digest(join(existing, file)))
      throw new Error(
        `Published asset differs; refusing to overwrite ${file}.`,
      );
}
