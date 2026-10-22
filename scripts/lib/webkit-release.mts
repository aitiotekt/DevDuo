import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, relative } from "node:path";
import { loadMetadata } from "./metadata.mts";
import { repoRoot } from "./paths.mts";
import {
  findRelease,
  publishRelease,
  type Runner,
  repositoryName,
} from "./release-github.mts";
import { run } from "./release-process.mts";
import { parseVersion } from "./release-version.mts";

type FileRecord = { path: string };
export type RuntimeRelease = {
  schemaVersion: 1;
  version: string;
  tag: string;
  asset: string;
  upstreamRevision: string;
  sourceSha: string;
};

// The application release downloads this asset by tag; fail before signing when
// it has not been published yet.
export function preflightPublishedWebKit(
  root = repoRoot,
  execute: Runner = run,
): void {
  const metadata = loadMetadata(root),
    names = webkitNames(metadata);
  const release = findRelease(repositoryName(metadata), names.tag, execute);
  if (
    !release ||
    release.draft ||
    !release.assets.some((asset) => asset.name === names.asset)
  )
    throw new Error(
      `Publish ${names.tag} with ${names.asset} before releasing the application.`,
    );
}

export function webkitNames(metadata = loadMetadata()) {
  const config = metadata.webkit;
  const tag = `${config.tagPrefix}${config.version}`;
  const stem = `${config.assetPrefix}-${config.version}-${config.target}`;
  return { tag, asset: `${stem}.zip`, manifest: `${stem}.json`, stem };
}

export function publishWebKitOutput(
  directory: string,
  prerelease: boolean,
  execute: Runner = run,
  root = repoRoot,
): { runtime: RuntimeRelease } {
  const metadata = loadMetadata(root),
    names = webkitNames(metadata);
  const report = JSON.parse(
    readFileSync(join(directory, names.manifest), "utf8"),
  ) as { runtime: RuntimeRelease };
  publishRelease(
    {
      repository: repositoryName(metadata),
      tag: names.tag,
      sha: report.runtime.sourceSha,
      prerelease,
      latest: false,
      title: `${metadata.webkit.assetPrefix} ${metadata.webkit.version}`,
      notes: join(directory, "release-notes.md"),
      directory,
      files: [names.asset, names.manifest, "SHA256SUMS"],
    },
    execute,
  );
  return report;
}

export function webkitPlan(
  ref: string,
  sha: string,
  metadata = loadMetadata(),
) {
  if (ref !== `refs/heads/${metadata.webkit.releaseBranch}`)
    throw new Error(
      "WebKit publication runs from its configured release branch.",
    );
  return {
    ...webkitNames(metadata),
    version: metadata.webkit.version,
    repository: repositoryName(metadata),
    sourceSha: sha,
    runner_labels: JSON.stringify(metadata.webkit.runnerLabels),
    prerelease: parseVersion(metadata.webkit.version).channel !== "stable",
  };
}

export function safeRelative(path: unknown): string {
  if (
    typeof path !== "string" ||
    path.length === 0 ||
    path.includes("\\") ||
    path.includes(":") ||
    [...path].some(
      (character) =>
        character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127,
    ) ||
    path
      .split("/")
      .some(
        (part) => !part || part === "." || part === ".." || /[. ]$/.test(part),
      )
  )
    throw new Error("Unsafe runtime file path.");
  return path;
}

export function regularFiles(directory: string, prefix = ""): string[] {
  return readdirSync(directory)
    .flatMap((name) => {
      const relative = safeRelative(prefix + name),
        path = join(directory, name),
        stat = lstatSync(path);
      if (stat.isDirectory()) return regularFiles(path, `${relative}/`);
      if (!stat.isFile())
        throw new Error(
          `Runtime files cannot be links or special files: ${relative}`,
        );
      return [relative];
    })
    .sort();
}

function licenseFiles(directory: string): string[] {
  return readdirSync(directory, { recursive: true, withFileTypes: true })
    .filter(
      (entry) =>
        entry.isFile() && /^(?:LICENSE|COPYING)(?:[.-].*)?$/i.test(entry.name),
    )
    .map((entry) =>
      relative(directory, join(entry.parentPath, entry.name)).replaceAll(
        "\\",
        "/",
      ),
    );
}

function readJson(path: string) {
  return JSON.parse(readFileSync(path, "utf8").replace(/^\uFEFF/, ""));
}

export function stageRuntime(
  build: string,
  stage: string,
  sha: string,
  root = repoRoot,
): void {
  const metadata = loadMetadata(root);
  const manifest = readJson(join(build, "runtime-manifest.json"));
  const inputs = readJson(join(build, "build-inputs.json"));
  // Build output can include PDBs and MiniBrowser; only the explicit runtime inventory is shipped.
  for (const file of manifest.inputFiles as FileRecord[]) {
    const path = safeRelative(file.path);
    const snapshot = join(build, "inputs", path);
    const destination = join(stage, "source-inputs", path);
    mkdirSync(dirname(destination), { recursive: true });
    copyFileSync(
      existsSync(snapshot) ? snapshot : join(root, path),
      destination,
    );
  }
  for (const file of manifest.files as FileRecord[]) {
    const path = safeRelative(file.path),
      destination = join(stage, "bin", path);
    mkdirSync(dirname(destination), { recursive: true });
    copyFileSync(join(build, "bin", path), destination);
  }
  copyFileSync(
    join(build, "runtime-manifest.json"),
    join(stage, "runtime-manifest.json"),
  );
  writeFileSync(
    join(stage, "build-provenance.json"),
    `${JSON.stringify(
      {
        schemaVersion: 1,
        upstreamRevision: inputs.upstreamRevision,
        clangSha256: inputs.clangSha256,
        systemInputs: inputs.systemInputs,
        buildTreeIdentity: inputs.buildTreeIdentity,
        tools: (inputs.tools ?? []).map(
          (tool: { name: string; sha256: string }) => ({
            name: tool.name,
            sha256: tool.sha256,
          }),
        ),
        inputFiles: inputs.inputFiles,
      },
      null,
      2,
    )}\n`,
  );
  const source = join(
    root,
    "temp/cache/windows-webkit",
    `source-${metadata.webkit.upstreamRevision.slice(0, 8)}`,
  );
  let licenses = 0;
  for (const area of [
    "Source/WebCore",
    "Source/JavaScriptCore",
    "Source/WTF",
    "Source/ThirdParty",
  ]) {
    const directory = join(source, area);
    if (!existsSync(directory)) continue;
    for (const path of licenseFiles(directory)) {
      const destination = join(stage, "licenses", "WebKit", area, path);
      mkdirSync(dirname(destination), { recursive: true });
      copyFileSync(join(directory, path), destination);
      licenses++;
    }
  }
  const dependencies = join(build, "vcpkg_installed/x64-windows-devduo/share");
  if (!existsSync(dependencies))
    throw new Error("Missing vcpkg runtime dependency notices.");
  for (const path of regularFiles(dependencies).filter((path) =>
    path.endsWith("/copyright"),
  )) {
    const destination = join(stage, "licenses/vcpkg", path);
    mkdirSync(dirname(destination), { recursive: true });
    copyFileSync(join(dependencies, path), destination);
    licenses++;
  }
  if (!licenses) throw new Error("Missing upstream runtime notices.");
  for (const path of ["LICENSE", "THIRD_PARTY_NOTICES.md"])
    copyFileSync(join(root, path), join(stage, path));
  const release: RuntimeRelease = {
    schemaVersion: 1,
    version: metadata.webkit.version,
    tag: webkitNames(metadata).tag,
    asset: webkitNames(metadata).asset,
    upstreamRevision: metadata.webkit.upstreamRevision,
    sourceSha: sha,
  };
  writeFileSync(
    join(stage, "runtime-release.json"),
    `${JSON.stringify(release, null, 2)}\n`,
  );
  writeFileSync(
    join(stage, "SOURCE.md"),
    `# Windows WebKit runtime sources\n\nUpstream: https://github.com/WebKit/WebKit/tree/${release.upstreamRevision}\n\nDevDuo build inputs and patches: source-inputs/. The runtime inventory retains its explicit native acceptance status; compilation does not establish desktop acceptance. Retained license texts do not by themselves establish a complete redistribution review.\n`,
  );
}
