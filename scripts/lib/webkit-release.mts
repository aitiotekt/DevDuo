import { createHash } from "node:crypto";
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
import { parse as parseToml } from "smol-toml";
import { parse as parseYaml } from "yaml";
import { loadMetadata } from "./metadata.mts";
import { repoRoot } from "./paths.mts";
import { digest } from "./release-artifacts.mts";
import {
  findRelease,
  type Runner,
  repositoryName,
  tagCommit,
} from "./release-github.mts";
import { run, temporary } from "./release-process.mts";
import { parseVersion } from "./release-version.mts";

export const engineInputs = [
  "mise.windows-webkit.toml",
  "mise.windows-webkit.lock",
  "native/webkit/windows/runtime.lock.json",
  "native/webkit/windows/BuildRuntime.ps1",
  "native/webkit/windows/RuntimeFiles.ps1",
  "native/webkit/windows/PersistentBuild.ps1",
  "native/webkit/windows/MiseTools.cmake",
  "native/webkit/windows/triplets/x64-windows-devduo.cmake",
  "native/webkit/windows/triplets/x64-windows-devduo-host.cmake",
  "native/webkit/windows/patches/0001-windows-host-viewport-api.patch",
  "native/webkit/windows/patches/0002-windows-content-painting.patch",
  "native/webkit/windows/patches/0003-windows-developer-api.patch",
];
const capabilities = {
  hostViewportApiVersion: 2,
  contentPaintingVersion: 2,
  contentColorApiVersion: 1,
  hostAppearanceApiVersion: 1,
  hostDeveloperApiVersion: 1,
  hostFixedContainerColorApiVersion: 1,
  hostScrollIndicatorApiVersion: 1,
};
type FileRecord = { path: string; sha256: string };
export type RuntimeRelease = {
  schemaVersion: 1;
  version: string;
  tag: string;
  asset: string;
  upstreamRevision: string;
  sourceFingerprint: string;
  sourceSha: string;
};

type WebKitReport = {
  schemaVersion: number;
  version: string;
  asset: string;
  archiveSha256: string;
  runtimeManifestSha256: string;
  sourceFingerprint: string;
  runtime: RuntimeRelease;
};

export function verifyWebKitReport(
  value: unknown,
  root = repoRoot,
  sourceSha?: string,
): asserts value is WebKitReport {
  if (!value || typeof value !== "object")
    throw new Error("Missing WebKit release report.");
  const report = value as WebKitReport;
  const metadata = loadMetadata(root),
    names = webkitNames(metadata);
  const runtime = report.runtime;
  if (
    report.schemaVersion !== 1 ||
    report.asset !== names.asset ||
    report.version !== metadata.webkit.version ||
    report.sourceFingerprint !== sourceFingerprint(root, metadata) ||
    !/^[a-f0-9]{64}$/.test(report.archiveSha256) ||
    !/^[a-f0-9]{64}$/.test(report.runtimeManifestSha256) ||
    runtime?.schemaVersion !== 1 ||
    runtime.version !== metadata.webkit.version ||
    runtime.tag !== names.tag ||
    runtime.asset !== names.asset ||
    runtime.upstreamRevision !== metadata.webkit.upstreamRevision ||
    runtime.sourceFingerprint !== sourceFingerprint(root, metadata) ||
    !/^[a-f0-9]{40}$/.test(runtime.sourceSha) ||
    (sourceSha !== undefined && runtime.sourceSha !== sourceSha)
  )
    throw new Error(
      "WebKit release output identity or archive integrity mismatch.",
    );
  if (
    metadata.webkit.archiveSha256 &&
    report.archiveSha256 !== metadata.webkit.archiveSha256
  )
    throw new Error(
      "Built runtime differs from the already pinned archive; use a new WebKit version.",
    );
}

export function preflightPublishedWebKit(
  root = repoRoot,
  execute: Runner = run,
): void {
  const metadata = loadMetadata(root),
    expected = requirePinnedArchive(metadata),
    names = webkitNames(metadata);
  const repository = repositoryName(metadata),
    release = findRelease(repository, names.tag, execute);
  if (
    !release ||
    release.draft ||
    release.prerelease !==
      (parseVersion(metadata.webkit.version).channel !== "stable")
  )
    throw new Error(
      "The metadata WebKit release is missing, still a draft or has the wrong channel.",
    );
  const required = [names.asset, names.manifest, "SHA256SUMS"];
  if (
    release.assets.length !== required.length ||
    new Set(release.assets.map((asset) => asset.name)).size !==
      required.length ||
    release.assets.some(
      (asset) =>
        !required.includes(asset.name) ||
        asset.state !== "uploaded" ||
        !Number.isSafeInteger(asset.size) ||
        asset.size <= 0,
    )
  )
    throw new Error(
      "The published WebKit asset set is incomplete or unexpected.",
    );
  temporary((directory) => {
    for (const name of [names.manifest, "SHA256SUMS"])
      execute("gh", [
        "release",
        "download",
        names.tag,
        "--repo",
        repository,
        "--pattern",
        name,
        "--dir",
        directory,
      ]);
    const report = JSON.parse(
      readFileSync(join(directory, names.manifest), "utf8"),
    );
    verifyWebKitReport(report, root);
    if (tagCommit(names.tag, execute) !== report.runtime.sourceSha)
      throw new Error(
        "Published WebKit source tag does not match its release report.",
      );
    const sums = `${expected}  ${names.asset}\n${digest(join(directory, names.manifest))}  ${names.manifest}\n`;
    if (readFileSync(join(directory, "SHA256SUMS"), "utf8") !== sums)
      throw new Error(
        "Published WebKit checksum list does not match metadata and report.",
      );
  });
}

export function webkitNames(metadata = loadMetadata()) {
  const config = metadata.webkit;
  const tag = `${config.tagPrefix}${config.version}`;
  const stem = `${config.assetPrefix}-${config.version}-${config.target}`;
  return { tag, asset: `${stem}.zip`, manifest: `${stem}.json`, stem };
}

export function requirePinnedArchive(metadata = loadMetadata()): string {
  if (!metadata.webkit.archiveSha256)
    throw new Error(
      "No WebKit archive is pinned. Manually publish the metadata version, then set webkit.archive_sha256 from its release report.",
    );
  return metadata.webkit.archiveSha256;
}

export function verifyWebKitOutput(
  directory: string,
  sha: string,
  root = repoRoot,
): {
  archiveSha256: string;
  runtime: RuntimeRelease;
  runtimeManifestSha256: string;
} {
  const metadata = loadMetadata(root),
    names = webkitNames(metadata);
  const report = JSON.parse(
    readFileSync(join(directory, names.manifest), "utf8"),
  );
  verifyWebKitReport(report, root, sha);
  if (digest(join(directory, names.asset)) !== report.archiveSha256)
    throw new Error(
      "WebKit release output identity or archive integrity mismatch.",
    );
  const checksums = `${report.archiveSha256}  ${names.asset}\n${digest(join(directory, names.manifest))}  ${names.manifest}\n`;
  if (readFileSync(join(directory, "SHA256SUMS"), "utf8") !== checksums)
    throw new Error("WebKit release output checksum list mismatch.");
  return report;
}

export function sourceFingerprint(
  root = repoRoot,
  metadata = loadMetadata(root),
): string {
  const tools = parseToml(readFileSync(join(root, "mise.toml"), "utf8"))
    .tools as Record<string, unknown>;
  const lock = parseToml(readFileSync(join(root, "mise.lock"), "utf8"))
    .tools as Record<string, unknown>;
  // Application versions, download digests and Node/pnpm updates do not change the engine.
  return createHash("sha256")
    .update(
      JSON.stringify({
        revision: metadata.webkit.upstreamRevision,
        target: metadata.webkit.target,
        python: { selection: tools.python, lock: lock.python },
        inputs: engineInputs.map((path) => ({
          path,
          sha256: digest(join(root, path)),
        })),
      }),
    )
    .digest("hex");
}

export function checkWebKitConfiguration(root = repoRoot): void {
  const metadata = loadMetadata(root);
  const pin = JSON.parse(
    readFileSync(join(root, "native/webkit/windows/runtime.lock.json"), "utf8"),
  );
  if (
    pin.revision !== metadata.webkit.upstreamRevision ||
    pin.architecture !== "x86_64"
  )
    throw new Error(
      "The official probe pin must match metadata's WebKit revision and architecture.",
    );
  const workflow = parseYaml(
    readFileSync(join(root, ".github/workflows/webkit-release.yml"), "utf8"),
  );
  if (Object.keys(workflow.on ?? {}).join() !== "workflow_dispatch")
    throw new Error(
      "WebKit release must have workflow_dispatch as its only trigger.",
    );
  const checkout = workflow.jobs.build.steps.find((step: { uses?: string }) =>
    step.uses?.startsWith("actions/checkout@"),
  );
  if (
    checkout?.with?.clean !== false ||
    workflow.jobs.build["runs-on"] !==
      `\${{ fromJSON(needs.plan.outputs.runner_labels) }}`
  )
    throw new Error(
      "WebKit builder must preserve caches and use metadata runner labels.",
    );
  sourceFingerprint(root, metadata);
}

export function webkitPlan(
  event: string,
  ref: string,
  sha: string,
  metadata = loadMetadata(),
) {
  if (
    event !== "workflow_dispatch" ||
    ref !== `refs/heads/${metadata.webkit.releaseBranch}` ||
    !/^[a-f0-9]{40}$/.test(sha)
  )
    throw new Error(
      "WebKit publication requires manual dispatch on its configured release branch and an exact commit.",
    );
  const repository = new URL(metadata.project.repositoryUrl);
  if (
    repository.hostname !== "github.com" ||
    !/^\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository.pathname)
  )
    throw new Error("WebKit release requires a GitHub project repository URL.");
  return {
    ...webkitNames(metadata),
    version: metadata.webkit.version,
    repository: repository.pathname.slice(1),
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

export function validateRuntime(
  directory: string,
  metadata = loadMetadata(),
  released = true,
  root = repoRoot,
): void {
  const manifest = JSON.parse(
    readFileSync(join(directory, "runtime-manifest.json"), "utf8").replace(
      /^\uFEFF/,
      "",
    ),
  );
  if (
    manifest.schemaVersion !== 1 ||
    manifest.upstreamRevision !== metadata.webkit.upstreamRevision ||
    manifest.backForwardListSwift !== false ||
    Object.entries(capabilities).some(
      ([name, value]) => manifest[name] !== value,
    )
  )
    throw new Error(
      "Runtime revision, ABI capabilities or history implementation does not match.",
    );
  const patchPaths = engineInputs.filter((path) => path.endsWith(".patch"));
  if (
    manifest.patchSha256 !== digest(join(root, patchPaths[0])) ||
    manifest.paintingPatchSha256 !== digest(join(root, patchPaths[1])) ||
    manifest.developerPatchSha256 !== digest(join(root, patchPaths[2]))
  )
    throw new Error(
      "Runtime patch hashes do not match the application's source.",
    );
  if (!Array.isArray(manifest.files) || !manifest.files.length)
    throw new Error("Runtime file manifest is missing.");
  const seen = new Set<string>();
  for (const file of manifest.files as FileRecord[]) {
    const path = safeRelative(file.path);
    if (seen.has(path.toLowerCase()) || !/^[a-f0-9]{64}$/.test(file.sha256))
      throw new Error("Duplicate runtime file or invalid digest.");
    seen.add(path.toLowerCase());
    const absolute = join(directory, "bin", path);
    if (!lstatSync(absolute).isFile() || digest(absolute) !== file.sha256)
      throw new Error(`Runtime file integrity mismatch: ${path}`);
  }
  for (const name of [
    metadata.webkit.dllName,
    "WebCore.dll",
    "JavaScriptCore.dll",
    "WebKitWebProcess.exe",
    "WebKitNetworkProcess.exe",
    "WebKitGPUProcess.exe",
  ])
    if (!seen.has(name.toLowerCase()))
      throw new Error(`Missing runtime component: ${name}`);
  const actual = regularFiles(join(directory, "bin"));
  if (
    actual.length !== seen.size ||
    actual.some((path) => !seen.has(path.toLowerCase()))
  )
    throw new Error("Runtime contains unlisted binaries or resources.");
  if (released) {
    const release = JSON.parse(
      readFileSync(join(directory, "runtime-release.json"), "utf8"),
    ) as RuntimeRelease;
    if (
      release.schemaVersion !== 1 ||
      release.version !== metadata.webkit.version ||
      release.tag !== webkitNames(metadata).tag ||
      release.asset !== webkitNames(metadata).asset ||
      release.upstreamRevision !== metadata.webkit.upstreamRevision ||
      release.sourceFingerprint !== sourceFingerprint(root, metadata) ||
      !/^[a-f0-9]{40}$/.test(release.sourceSha)
    )
      throw new Error(
        "Released runtime identity or source fingerprint does not match metadata.",
      );
    regularFiles(directory);
  }
}

export function stageRuntime(
  build: string,
  stage: string,
  sha: string,
  root = repoRoot,
): void {
  const metadata = loadMetadata(root);
  if (!/^[a-f0-9]{40}$/.test(sha))
    throw new Error("A full runtime source commit is required.");
  const manifest = JSON.parse(
    readFileSync(join(build, "runtime-manifest.json"), "utf8").replace(
      /^\uFEFF/,
      "",
    ),
  );
  const inputsPath = join(build, "build-inputs.json");
  if (manifest.buildInputsSha256 !== digest(inputsPath))
    throw new Error(
      "Native build provenance hash does not match the runtime manifest.",
    );
  const inputs = JSON.parse(
    readFileSync(inputsPath, "utf8").replace(/^\uFEFF/, ""),
  );
  // Build output can include PDBs and MiniBrowser; only the explicit runtime inventory is shipped.
  for (const file of [
    ...manifest.inputFiles,
    ...engineInputs.map((path) => ({ path, sha256: digest(join(root, path)) })),
  ] as FileRecord[]) {
    const path = safeRelative(file.path);
    if (digest(join(root, path)) !== file.sha256)
      throw new Error(`Rebuild WebKit after changing ${path}.`);
    const destination = join(stage, "source-inputs", path);
    mkdirSync(dirname(destination), { recursive: true });
    copyFileSync(join(root, path), destination);
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
    sourceFingerprint: sourceFingerprint(root, metadata),
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
  validateRuntime(stage, metadata, true, root);
}
