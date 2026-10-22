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
import { dryRun, repositoryName } from "./lib/release-github.mts";
import { run, temporary } from "./lib/release-process.mts";
import {
  preflightPublishedWebKit,
  publishWebKitOutput,
  stageRuntime,
  webkitNames,
  webkitPlan,
} from "./lib/webkit-release.mts";

const metadata = loadMetadata(),
  names = webkitNames(metadata);
const output = join(repoRoot, "temp/release/webkit", names.stem),
  archive = join(output, names.asset);
const sourceSha = () =>
  process.env.GITHUB_SHA ?? run("git", ["rev-parse", "HEAD"], true);
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
function packageRuntime(): void {
  mkdirSync(output, { recursive: true });
  if (existsSync(archive)) {
    console.log(`Reusing ${names.asset} without rebuilding or repacking.`);
    return;
  }
  temporary((stage) => {
    stageRuntime(
      join(repoRoot, "temp/build/windows-webkit-host"),
      stage,
      sourceSha(),
    );
    zip("Pack", stage, archive);
    writeJson(join(output, names.manifest), {
      schemaVersion: 1,
      version: metadata.webkit.version,
      asset: names.asset,
      runtime: json(join(stage, "runtime-release.json")),
    });
    // Published for downloaders; no pipeline step compares against it.
    writeFileSync(
      join(output, "SHA256SUMS"),
      `${digest(archive)}  ${names.asset}\n${digest(join(output, names.manifest))}  ${names.manifest}\n`,
    );
    writeFileSync(
      join(output, "release-notes.md"),
      `# ${metadata.webkit.assetPrefix} ${metadata.webkit.version}\n\nUpstream: ${metadata.webkit.upstreamRevision}\n\nRuntime files and patches are bundled with upstream notices. Automated builds do not establish full desktop acceptance.\n`,
    );
  });
}
function fetchRuntime(): string {
  const cache = join(
    repoRoot,
    "temp/cache/windows-webkit/releases",
    metadata.webkit.version,
  );
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
        repositoryName(metadata),
        "--pattern",
        names.asset,
        "--dir",
        path,
      ]);
      cpSync(join(path, names.asset), downloaded);
    });
  if (!existsSync(unpacked))
    temporary((path) => {
      const extracted = join(path, "runtime");
      zip("Unpack", extracted, downloaded);
      // Stage then rename so an interrupted copy never looks like a complete cache.
      const staged = join(cache, "runtime.partial");
      rmSync(staged, { recursive: true, force: true });
      try {
        cpSync(extracted, staged, { recursive: true });
        renameSync(staged, unpacked);
      } finally {
        rmSync(staged, { recursive: true, force: true });
      }
    });
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
}
function publish(): void {
  const plan = webkitPlan(process.env.GITHUB_REF ?? "", sourceSha());
  if (dryRun(process.env.RELEASE_DRY_RUN ?? ""))
    throw new Error("Dry-run mode cannot publish a WebKit release.");
  publishWebKitOutput(output, plan.prerelease);
  if (process.env.GITHUB_STEP_SUMMARY)
    appendFileSync(
      process.env.GITHUB_STEP_SUMMARY,
      `Published ${names.tag}.\n`,
    );
}

try {
  const command = process.argv[2];
  if (command === "plan") {
    const plan = webkitPlan(process.env.GITHUB_REF ?? "", sourceSha());
    const mode = dryRun(process.env.RELEASE_DRY_RUN ?? "");
    if (process.env.GITHUB_OUTPUT)
      for (const [key, value] of Object.entries({ ...plan, dry_run: mode }))
        appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
    console.log(JSON.stringify({ ...plan, dry_run: mode }, null, 2));
  } else if (command === "preflight") {
    preflightPublishedWebKit();
    console.log(`${names.tag} is published with ${names.asset}.`);
  } else if (command === "package") packageRuntime();
  else if (command === "fetch") console.log(fetchRuntime());
  else if (command === "bundle") bundleRuntime();
  else if (command === "publish") publish();
  else
    throw new Error("Use plan, preflight, package, fetch, bundle or publish.");
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "WebKit release failed.",
  );
  process.exitCode = 1;
}
