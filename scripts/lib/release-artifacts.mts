import { createHash } from "node:crypto";
import {
  closeSync,
  copyFileSync,
  lstatSync,
  mkdirSync,
  openSync,
  readSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { type DevDuoMetadata, loadMetadata } from "./metadata.mts";
import {
  artifactName,
  type ReleasePlan,
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
  return {
    version: config.version,
    tag: `${config.tagPrefix}${config.version}`,
    asset: `${config.assetPrefix}-${config.version}-${config.target}.zip`,
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

function artifactRecord(path: string, name: string, target: Target): Artifact {
  return {
    name,
    target,
    sha256: digest(path),
    size: lstatSync(path).size,
    signed: target === "osx-arm64",
    notarized: target === "osx-arm64",
  };
}

// The manifest and SHA256SUMS are published for downloaders; the pipeline does
// not compare them against anything.
export function assemble(
  input: string,
  output: string,
  plan: ReleasePlan,
  metadata: DevDuoMetadata = loadMetadata(),
): void {
  mkdirSync(output, { recursive: true });
  const artifacts = targets.map((target) => {
    const name = artifactName(plan.version, target, metadata),
      destination = join(output, name);
    copyFileSync(join(input, `release-${target}`, name), destination);
    return artifactRecord(destination, name, target);
  });
  const manifest: Manifest = {
    schemaVersion: 1,
    version: plan.version,
    sourceSha: plan.sourceSha,
    sourceRef: plan.sourceRef,
    artifacts,
    windowsWebKit: windowsRuntime(metadata),
  };
  const manifestPath = join(output, "release-manifest.json");
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
  writeFileSync(
    join(output, "SHA256SUMS"),
    `${[
      ...artifacts.map((artifact) => `${artifact.sha256}  ${artifact.name}`),
      `${digest(manifestPath)}  release-manifest.json`,
    ].join("\n")}\n`,
  );
}
