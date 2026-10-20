import { readFileSync } from "node:fs";
import { join } from "node:path";
import { parse as parseYaml } from "yaml";
import { type DevDuoMetadata, loadMetadata, type Target } from "./metadata.mts";
import { repoRoot } from "./paths.mts";
import { parseVersion } from "./release-version.mts";
import { checkVersion } from "./version.mts";

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
  if (!/^[a-f0-9]{40}$/.test(sha))
    throw new Error("A full source commit SHA is required.");
  const tag = `${metadata.release.tagPrefix}${version}`;
  const branch = `refs/heads/${metadata.release.branch}`;
  if (ref !== branch && ref !== `refs/tags/${tag}`)
    throw new Error(
      "Release requires the configured release branch or the exact version tag.",
    );
  if (parsed.channel === "dev" && ref !== branch)
    throw new Error("Development versions cannot be tagged for release.");
  if (parsed.channel === "stable" && ref !== `refs/tags/${tag}`)
    throw new Error("Stable versions require an exact version tag.");
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

export function checkReleaseConfiguration(root = repoRoot): string {
  const version = checkVersion(root),
    metadata = loadMetadata(root);
  const workflow = parseYaml(
    readFileSync(join(root, ".github/workflows/release.yml"), "utf8"),
  );
  const push = workflow?.on?.push;
  if (
    !Array.isArray(push?.branches) ||
    push.branches.length !== 1 ||
    push.branches[0] !== metadata.release.branch ||
    !Array.isArray(push?.tags) ||
    push.tags.length !== 1 ||
    push.tags[0] !== `${metadata.release.tagPrefix}*`
  )
    throw new Error(
      "Release workflow push filters do not match devduo-metadata.toml.",
    );
  return version;
}
