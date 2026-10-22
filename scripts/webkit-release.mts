import {
  appendFileSync,
  cpSync,
  existsSync,
  mkdirSync,
  readFileSync,
  renameSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { loadMetadata } from "./lib/metadata.mts";
import { repoRoot } from "./lib/paths.mts";
import { digest } from "./lib/release-artifacts.mts";
import {
  dryRun,
  publishRelease,
  repositoryName,
} from "./lib/release-github.mts";
import { run, temporary } from "./lib/release-process.mts";
import {
  checkWebKitConfiguration,
  preflightPublishedWebKit,
  requirePinnedArchive,
  sourceFingerprint,
  stageRuntime,
  validateRuntime,
  verifyWebKitOutput,
  webkitNames,
  webkitPlan,
} from "./lib/webkit-release.mts";

const metadata = loadMetadata(),
  names = webkitNames(metadata);
const output = join(repoRoot, "temp/release/webkit", names.stem),
  archive = join(output, names.asset);
const sourceSha = () =>
  process.env.GITHUB_SHA ?? run("git", ["rev-parse", "HEAD"], true);
const repository = repositoryName(metadata);
const json = (path: string) => JSON.parse(readFileSync(path, "utf8"));
const writeJson = (path: string, value: unknown) =>
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
function zip(mode: "Pack" | "Unpack", directory: string, file: string): void {
  if (process.platform !== "win32")
    throw new Error("Runtime ZIP staging requires Windows.");
  run("powershell.exe", [
    "-NoLogo",
    "-NoProfile",
    "-File",
    join(repoRoot, "native/webkit/windows/RuntimeArchive.ps1"),
    "-Mode",
    mode,
    "-Directory",
    directory,
    "-Archive",
    file,
  ]);
}
function verifyOutput() {
  return verifyWebKitOutput(output, sourceSha());
}
function packageRuntime(): void {
  mkdirSync(output, { recursive: true });
  if (existsSync(archive)) {
    const report = verifyOutput();
    if (
      report.runtimeManifestSha256 !==
      digest(
        join(repoRoot, "temp/build/windows-webkit-host/runtime-manifest.json"),
      )
    )
      throw new Error(
        "The preserved release archive belongs to another native build; use a new WebKit version.",
      );
    return;
  }
  temporary((stage) => {
    stageRuntime(
      join(repoRoot, "temp/build/windows-webkit-host"),
      stage,
      sourceSha(),
    );
    zip("Pack", stage, archive);
    const report = {
      schemaVersion: 1,
      version: metadata.webkit.version,
      asset: names.asset,
      archiveSha256: digest(archive),
      sourceFingerprint: sourceFingerprint(),
      runtime: json(join(stage, "runtime-release.json")),
      runtimeManifestSha256: digest(join(stage, "runtime-manifest.json")),
    };
    writeJson(join(output, names.manifest), report);
    writeFileSync(
      join(output, "SHA256SUMS"),
      `${report.archiveSha256}  ${names.asset}\n${digest(join(output, names.manifest))}  ${names.manifest}\n`,
    );
    writeFileSync(
      join(output, "release-notes.md"),
      `# ${metadata.webkit.assetPrefix} ${metadata.webkit.version}\n\nUpstream: ${metadata.webkit.upstreamRevision}\n\nSource fingerprint: ${report.sourceFingerprint}\n\nRuntime files and patches are bundled with upstream notices. Automated build/integrity checks do not establish full desktop acceptance.\n\nPin in devduo-metadata.toml:\n\n\`archive_sha256 = "${report.archiveSha256}"\`\n`,
    );
  });
  verifyOutput();
}
function fetchRuntime(): string {
  const expected = requirePinnedArchive(metadata);
  const cache = join(repoRoot, "temp/cache/windows-webkit/releases", expected);
  mkdirSync(cache, { recursive: true });
  const downloaded = join(cache, names.asset),
    unpacked = join(cache, "runtime");
  if (!existsSync(downloaded))
    temporary((path) => {
      run("gh", [
        "release",
        "download",
        names.tag,
        "--repo",
        repository,
        "--pattern",
        names.asset,
        "--dir",
        path,
      ]);
      const file = join(path, names.asset);
      if (digest(file) !== expected)
        throw new Error(
          "Downloaded WebKit archive SHA-256 does not match metadata.",
        );
      cpSync(file, downloaded, { errorOnExist: true, force: false });
    });
  if (digest(downloaded) !== expected)
    throw new Error(
      "Downloaded WebKit archive SHA-256 does not match metadata.",
    );
  if (!existsSync(unpacked))
    temporary((path) => {
      const extracted = join(path, "runtime");
      zip("Unpack", extracted, downloaded);
      validateRuntime(extracted);
      const staged = join(cache, "runtime.partial");
      if (existsSync(staged))
        throw new Error(
          "Incomplete runtime cache stage exists; inspect and remove runtime.partial before retrying.",
        );
      try {
        cpSync(extracted, staged, {
          recursive: true,
          errorOnExist: true,
          force: false,
        });
        validateRuntime(staged);
        renameSync(staged, unpacked);
      } finally {
        rmSync(staged, { recursive: true, force: true });
      }
    });
  validateRuntime(unpacked);
  return unpacked;
}
function bundleRuntime(): void {
  const runtime = fetchRuntime();
  const destination = join(
    repoRoot,
    "temp/build/release/win-x64",
    metadata.project.binaryName,
    "webkit",
  );
  if (existsSync(destination))
    throw new Error(
      "Runtime bundle destination already exists; prepare the application release stage first.",
    );
  cpSync(runtime, destination, {
    recursive: true,
    errorOnExist: true,
    force: false,
  });
  validateRuntime(destination);
}
function publish(): void {
  const plan = webkitPlan(
    process.env.GITHUB_EVENT_NAME ?? "",
    process.env.GITHUB_REF ?? "",
    sourceSha(),
  );
  if (
    dryRun(
      process.env.GITHUB_EVENT_NAME ?? "",
      process.env.RELEASE_DRY_RUN ?? "",
    )
  )
    throw new Error("Dry-run mode cannot publish a WebKit release.");
  const report = verifyOutput();
  const files = [names.asset, names.manifest, "SHA256SUMS"];
  publishRelease({
    repository,
    tag: names.tag,
    sha: sourceSha(),
    prerelease: plan.prerelease,
    mayCreateTag: true,
    latest: false,
    title: `${metadata.webkit.assetPrefix} ${metadata.webkit.version}`,
    notes: join(output, "release-notes.md"),
    directory: output,
    files,
  });
  if (process.env.GITHUB_STEP_SUMMARY)
    appendFileSync(
      process.env.GITHUB_STEP_SUMMARY,
      `Published ${names.tag}. Set \`[webkit].archive_sha256 = "${report.archiveSha256}"\` before an application release.\n`,
    );
}

try {
  const command = process.argv[2];
  if (command === "check") {
    checkWebKitConfiguration();
    console.log("WebKit manual release configuration is consistent.");
  } else if (command === "plan") {
    checkWebKitConfiguration();
    const plan = webkitPlan(
      process.env.GITHUB_EVENT_NAME ?? "",
      process.env.GITHUB_REF ?? "",
      sourceSha(),
    );
    const mode = dryRun(
      process.env.GITHUB_EVENT_NAME ?? "",
      process.env.RELEASE_DRY_RUN ?? "",
    );
    if (process.env.GITHUB_OUTPUT)
      for (const [key, value] of Object.entries({ ...plan, dry_run: mode }))
        appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
    console.log(JSON.stringify({ ...plan, dry_run: mode }, null, 2));
  } else if (command === "preflight") {
    checkWebKitConfiguration();
    preflightPublishedWebKit();
    console.log(
      "The metadata-pinned WebKit release is published and its report/checksums match.",
    );
  } else if (command === "package") packageRuntime();
  else if (command === "fetch") console.log(fetchRuntime());
  else if (command === "bundle") bundleRuntime();
  else if (command === "verify") verifyOutput();
  else if (command === "publish") publish();
  else
    throw new Error(
      "Use check, plan, preflight, package, fetch, bundle, verify or publish.",
    );
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "WebKit release failed.",
  );
  process.exitCode = 1;
}
