import { readFileSync } from "node:fs";
import { join } from "node:path";
import { parse } from "smol-toml";
import { repoRoot } from "./paths.mts";
import { parseVersion } from "./release-version.mts";

export const metadataFile = "devduo-metadata.toml";
export type Target = "osx-arm64" | "win-x64";
export type ReleaseArtifact = {
  platform: "macos" | "windows";
  target: Target;
  extension: "zip";
};
export type WebKitMetadata = {
  version: string;
  tagPrefix: string;
  assetPrefix: string;
  target: "win-x64";
  dllName: "WebKit2.dll";
  upstreamRevision: string;
  releaseBranch: string;
  runnerLabels: string[];
};
export type DevDuoMetadata = {
  schemaVersion: 1;
  project: {
    version: string;
    displayName: string;
    binaryName: string;
    identifier: string;
    repositoryUrl: string;
    docsUrl: string;
  };
  macos: { minimumSystemVersion: string };
  platforms: { linux: { check: boolean; release: false } };
  release: {
    branch: string;
    tagPrefix: string;
    signing: { macos: "signed" };
    artifacts: ReleaseArtifact[];
  };
  webkit: WebKitMetadata;
};

function table(value: unknown, location: string): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value))
    throw new Error(`${location} must be a table in ${metadataFile}.`);
  return value as Record<string, unknown>;
}
function text(
  object: Record<string, unknown>,
  key: string,
  location: string,
): string {
  const value = object[key];
  if (
    typeof value !== "string" ||
    value.trim() !== value ||
    !value ||
    [...value].some(
      (character) =>
        character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127,
    )
  )
    throw new Error(
      `Missing or invalid ${location}.${key} in ${metadataFile}.`,
    );
  return value;
}
function httpsUrl(value: string): string {
  const url = new URL(value);
  if (
    url.protocol !== "https:" ||
    url.username ||
    url.password ||
    url.search ||
    url.hash
  )
    throw new Error(
      "Project URLs must use HTTPS without credentials, queries or fragments.",
    );
  return value;
}

function boolean(
  object: Record<string, unknown>,
  key: string,
  location: string,
): boolean {
  const value = object[key];
  if (typeof value !== "boolean")
    throw new Error(`${location}.${key} must be a boolean in ${metadataFile}.`);
  return value;
}

export function parseMetadata(source: string): DevDuoMetadata {
  const raw = parse(source);
  if (raw.schema_version !== 1)
    throw new Error(`${metadataFile} schema_version must be 1.`);
  const project = table(raw.project, "project"),
    release = table(raw.release, "release"),
    macos = table(raw.macos, "macos");
  const platforms = table(raw.platforms, "platforms");
  const linux = table(platforms.linux, "platforms.linux");
  const linuxCheck = boolean(linux, "check", "platforms.linux");
  const linuxRelease = boolean(linux, "release", "platforms.linux");
  if (linuxRelease)
    throw new Error(
      "Linux release is not implemented; keep platforms.linux.release = false.",
    );
  const signing = table(release.signing, "release.signing");
  if (
    signing.macos !== "signed" ||
    Object.keys(signing).some((key) => key !== "macos")
  )
    throw new Error(
      "macOS releases require signed/notarized artifacts; other signing policies are unsupported.",
    );
  const version = text(project, "version", "project");
  parseVersion(version);
  const displayName = text(project, "display_name", "project"),
    binaryName = text(project, "binary_name", "project"),
    identifier = text(project, "identifier", "project");
  if (
    !/^[A-Za-z][A-Za-z0-9_-]*$/.test(binaryName) ||
    !/^[A-Za-z0-9]+(?:\.[A-Za-z0-9-]+)+$/.test(identifier)
  )
    throw new Error("Invalid project binary name or bundle identifier.");
  const branch = text(release, "branch", "release"),
    tagPrefix = text(release, "tag_prefix", "release");
  if (
    !/^[A-Za-z0-9][A-Za-z0-9_/-]*$/.test(branch) ||
    branch.includes("//") ||
    branch.endsWith("/") ||
    !/^[A-Za-z][A-Za-z0-9_-]*$/.test(tagPrefix)
  )
    throw new Error("Invalid release branch or tag prefix.");
  const minimumSystemVersion = text(macos, "minimum_system_version", "macos");
  if (!/^\d+\.\d+(?:\.\d+)?$/.test(minimumSystemVersion))
    throw new Error("Invalid minimum macOS version.");
  if (!Array.isArray(release.artifact) || release.artifact.length !== 2)
    throw new Error(
      "Release metadata must define both supported desktop artifacts.",
    );
  const identities = new Set<string>();
  const artifacts = release.artifact.map((entry, index): ReleaseArtifact => {
    const artifact = table(entry, `release.artifact[${index}]`);
    const target = text(artifact, "target", "release.artifact"),
      platform = text(artifact, "platform", "release.artifact");
    if (
      !(
        (target === "osx-arm64" && platform === "macos") ||
        (target === "win-x64" && platform === "windows")
      ) ||
      artifact.extension !== "zip" ||
      identities.has(target)
    )
      throw new Error("Unsupported or duplicate release artifact.");
    identities.add(target);
    return { platform, target, extension: "zip" };
  });
  const webkit = table(raw.webkit, "webkit");
  const engineVersion = text(webkit, "version", "webkit");
  if (parseVersion(engineVersion).channel === "dev")
    throw new Error("WebKit requires a numbered release version.");
  const engineTagPrefix = text(webkit, "tag_prefix", "webkit");
  const assetPrefix = text(webkit, "asset_prefix", "webkit");
  const upstreamRevision = text(webkit, "upstream_revision", "webkit");
  const releaseBranch = text(webkit, "release_branch", "webkit");
  const runnerLabels = webkit.runner_labels;
  if (
    !/^[A-Za-z][A-Za-z0-9_-]*$/.test(engineTagPrefix) ||
    engineTagPrefix.startsWith(tagPrefix) ||
    !/^[A-Za-z][A-Za-z0-9_-]*$/.test(assetPrefix) ||
    !/^[A-Za-z0-9][A-Za-z0-9_/-]*$/.test(releaseBranch) ||
    releaseBranch.includes("//") ||
    releaseBranch.endsWith("/") ||
    webkit.target !== "win-x64" ||
    webkit.dll_name !== "WebKit2.dll" ||
    !Array.isArray(runnerLabels) ||
    runnerLabels.length < 3 ||
    !["self-hosted", "Windows", "X64"].every((label) =>
      runnerLabels.includes(label),
    ) ||
    !runnerLabels.every(
      (label) => typeof label === "string" && /^[A-Za-z0-9_-]+$/.test(label),
    ) ||
    new Set(runnerLabels).size !== runnerLabels.length
  )
    throw new Error("Invalid WebKit release metadata or runner labels.");
  return {
    schemaVersion: 1,
    project: {
      version,
      displayName,
      binaryName,
      identifier,
      repositoryUrl: httpsUrl(text(project, "repository_url", "project")),
      docsUrl: httpsUrl(text(project, "docs_url", "project")),
    },
    macos: { minimumSystemVersion },
    platforms: { linux: { check: linuxCheck, release: false } },
    release: { branch, tagPrefix, signing: { macos: "signed" }, artifacts },
    webkit: {
      version: engineVersion,
      tagPrefix: engineTagPrefix,
      assetPrefix,
      target: "win-x64",
      dllName: "WebKit2.dll",
      upstreamRevision,
      releaseBranch,
      runnerLabels: runnerLabels as string[],
    },
  };
}

export function loadMetadata(root = repoRoot): DevDuoMetadata {
  return parseMetadata(readFileSync(join(root, metadataFile), "utf8"));
}
