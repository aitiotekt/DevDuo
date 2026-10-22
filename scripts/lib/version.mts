import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { loadMetadata, metadataFile, parseMetadata } from "./metadata.mts";
import { repoRoot } from "./paths.mts";
import { nextVersion, parseVersion } from "./release-version.mts";

const jsonManifests = ["package.json", "docsite/package.json"];
const nativeManifest = "src/DevDuo/DevDuo.csproj";
const nativeLinks = "src/DevDuo/ProjectLinks.cs";

function projectLinks(repository: string, documentation: string): string {
  return `// Generated from devduo-metadata.toml by just sync-version.\nnamespace DevDuo;\n\npublic static class ProjectLinks\n{\n    public const string Repository = ${JSON.stringify(repository)};\n    public const string Documentation = ${JSON.stringify(documentation)};\n}\n`;
}

function nativeVersion(source: string): string {
  const matches = [...source.matchAll(/<Version>\s*([^<]+?)\s*<\/Version>/g)];
  if (matches.length !== 1)
    throw new Error(
      `${nativeManifest} must contain exactly one Version element.`,
    );
  return matches[0][1];
}

export function checkVersion(root = repoRoot): string {
  const project = loadMetadata(root).project;
  const version = project.version;
  const sources = jsonManifests.map((file) => [
    file,
    JSON.parse(readFileSync(join(root, file), "utf8")).version,
  ]);
  sources.push([
    nativeManifest,
    nativeVersion(readFileSync(join(root, nativeManifest), "utf8")),
  ]);
  const mismatches = sources.filter(([, actual]) => actual !== version);
  if (mismatches.length)
    throw new Error(
      `Expected ${version} from ${metadataFile}: ${mismatches.map(([file, actual]) => `${file}=${actual}`).join(", ")}. Run just sync-version.`,
    );
  if (
    !existsSync(join(root, nativeLinks)) ||
    readFileSync(join(root, nativeLinks), "utf8").replaceAll("\r\n", "\n") !==
      projectLinks(project.repositoryUrl, project.docsUrl)
  )
    throw new Error(
      `${nativeLinks} must match project URLs in ${metadataFile}. Run just sync-version.`,
    );
  return version;
}

function replaceMetadataVersion(source: string, version: string): string {
  const lines = source.split(/\r?\n/),
    eol = source.includes("\r\n") ? "\r\n" : "\n";
  let inProject = false,
    replaced = 0;
  for (let i = 0; i < lines.length; i++) {
    const section = /^\s*\[([^\]]+)\]\s*(?:#.*)?$/.exec(lines[i]);
    if (section) inProject = section[1] === "project";
    else if (inProject && /^\s*version\s*=/.test(lines[i])) {
      lines[i] = lines[i].replace(
        /^(\s*version\s*=\s*)(?:"[^"\r\n]*"|'[^'\r\n]*')/,
        (_match, prefix) => `${prefix}${JSON.stringify(version)}`,
      );
      replaced++;
    }
  }
  if (replaced !== 1)
    throw new Error(
      `${metadataFile} must contain one editable [project].version.`,
    );
  const result = lines.join(eol);
  if (parseMetadata(result).project.version !== version)
    throw new Error("Unable to update metadata project version.");
  return result;
}

export function setVersion(version: string, root = repoRoot): string {
  parseVersion(version);
  const project = loadMetadata(root).project;
  // Validate and prepare every source before writing, so malformed manifests leave all files intact.
  const updates = new Map<string, string>();
  updates.set(
    metadataFile,
    replaceMetadataVersion(
      readFileSync(join(root, metadataFile), "utf8"),
      version,
    ),
  );
  updates.set(
    nativeLinks,
    projectLinks(project.repositoryUrl, project.docsUrl),
  );
  for (const file of jsonManifests) {
    const json = JSON.parse(readFileSync(join(root, file), "utf8"));
    if (typeof json.version !== "string")
      throw new Error(`${file} has no version.`);
    json.version = version;
    updates.set(file, `${JSON.stringify(json, null, 2)}\n`);
  }
  const native = readFileSync(join(root, nativeManifest), "utf8");
  nativeVersion(native);
  updates.set(
    nativeManifest,
    native.replace(
      /<Version>\s*[^<]+?\s*<\/Version>/,
      `<Version>${version}</Version>`,
    ),
  );
  for (const [file, source] of updates) {
    if (
      !existsSync(join(root, file)) ||
      readFileSync(join(root, file), "utf8") !== source
    )
      writeFileSync(join(root, file), source);
  }
  return version;
}

export function syncVersion(root = repoRoot): string {
  return setVersion(loadMetadata(root).project.version, root);
}
export function bumpVersion(level: string, root = repoRoot): string {
  return setVersion(nextVersion(checkVersion(root), level), root);
}
