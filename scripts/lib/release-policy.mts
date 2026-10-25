import { type DevDuoMetadata, loadMetadata, type Target } from "./metadata.mts";
import { parseVersion } from "./release-version.mts";

export type { Target } from "./metadata.mts";
export { parseVersion } from "./release-version.mts";
export { checkVersion as currentVersion, setVersion } from "./version.mts";

export function releasePlan(
  version: string,
  ref: string,
  sha: string,
  metadata = loadMetadata(),
) {
  const parsed = parseVersion(version);
  const tag = `${metadata.release.tagPrefix}${version}`;
  const branch = `refs/heads/${metadata.release.branch}`;
  if (ref !== branch && ref !== `refs/tags/${tag}`)
    throw new Error(
      "Release requires the configured release branch or the exact version tag.",
    );
  if (parsed.channel === "dev" && ref !== branch)
    throw new Error("Development versions cannot be tagged for release.");
  return {
    version,
    tag,
    displayName: metadata.project.displayName,
    sourceRef: ref,
    sourceSha: sha,
    channel: parsed.channel,
    publish: parsed.channel !== "dev",
    prerelease: parsed.channel !== "stable",
  };
}
export type ReleasePlan = ReturnType<typeof releasePlan>;
export const targets = loadMetadata().release.artifacts.map(
  (artifact) => artifact.target,
);

export function checksMatrix(metadata = loadMetadata()) {
  const include = [{ os: "windows-2025" }, { os: "xcode-27" }];
  if (metadata.platforms.linux.check) include.unshift({ os: "ubuntu-24.04" });
  return { include };
}

export function artifactName(
  version: string,
  target: Target,
  metadata: DevDuoMetadata = loadMetadata(),
): string {
  parseVersion(version);
  const artifact = metadata.release.artifacts.find(
    (artifact) => artifact.target === target,
  );
  if (!artifact) throw new Error(`Missing release artifact: ${target}`);
  return `${metadata.project.binaryName}-${metadata.release.tagPrefix}${version}-${target}.${artifact.extension}`;
}
