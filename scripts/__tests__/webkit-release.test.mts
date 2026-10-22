import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  statSync,
  utimesSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import test from "node:test";
import { loadMetadata, parseMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";
import { digest } from "../lib/release-artifacts.mts";
import type { GitHubRelease, Runner } from "../lib/release-github.mts";
import {
  checkWebKitConfiguration,
  engineInputs,
  preflightPublishedWebKit,
  requirePinnedArchive,
  safeRelative,
  sourceFingerprint,
  stageRuntime,
  validateRuntime,
  verifyWebKitOutput,
  webkitNames,
  webkitPlan,
} from "../lib/webkit-release.mts";

const source = readFileSync(
  join(repoRoot, "devduo-metadata.toml"),
  "utf8",
).replace(/^archive_sha256 = .*$/m, 'archive_sha256 = ""');
const sha = "a".repeat(40);
function fixture() {
  const root = mkdtempSync(join(tmpdir(), "devduo-webkit-tests-"));
  for (const file of [
    ...engineInputs,
    "devduo-metadata.toml",
    "mise.toml",
    "mise.lock",
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    ".github/workflows/webkit-release.yml",
  ]) {
    mkdirSync(dirname(join(root, file)), { recursive: true });
    cpSync(join(repoRoot, file), join(root, file));
  }
  writeFileSync(join(root, "devduo-metadata.toml"), source);
  return root;
}
function cleanup(root: string) {
  assert.equal(dirname(resolve(root)), resolve(tmpdir()));
  assert.ok(root.startsWith(join(tmpdir(), "devduo-webkit-tests-")));
  rmSync(root, { recursive: true });
}
function writeJson(path: string, value: unknown) {
  writeFileSync(path, JSON.stringify(value));
}
function runtimeFixture(root: string) {
  const config = loadMetadata(root),
    build = join(root, "build"),
    bin = join(build, "bin");
  mkdirSync(bin, { recursive: true });
  const files = [
    "WebKit2.dll",
    "WebCore.dll",
    "JavaScriptCore.dll",
    "WebKitWebProcess.exe",
    "WebKitNetworkProcess.exe",
    "WebKitGPUProcess.exe",
  ].map((path) => {
    writeFileSync(join(bin, path), `unit fixture ${path}`);
    return { path, sha256: digest(join(bin, path)) };
  });
  const patches = engineInputs.filter((path) => path.endsWith(".patch"));
  writeJson(join(build, "build-inputs.json"), {
    upstreamRevision: config.webkit.upstreamRevision,
    clangSha256: "c".repeat(64),
    tools: [
      {
        name: "compiler",
        sha256: "d".repeat(64),
        path: "private install directory",
      },
    ],
  });
  const manifest = {
    buildInputsSha256: digest(join(build, "build-inputs.json")),
    schemaVersion: 1,
    upstreamRevision: config.webkit.upstreamRevision,
    backForwardListSwift: false,
    hostViewportApiVersion: 2,
    contentPaintingVersion: 2,
    contentColorApiVersion: 1,
    hostFixedContainerColorApiVersion: 1,
    hostScrollIndicatorApiVersion: 1,
    hostAppearanceApiVersion: 1,
    hostDeveloperApiVersion: 1,
    patchSha256: digest(join(root, patches[0])),
    paintingPatchSha256: digest(join(root, patches[1])),
    developerPatchSha256: digest(join(root, patches[2])),
    inputFiles: engineInputs.map((path) => ({
      path,
      sha256: digest(join(root, path)),
    })),
    files,
    runtimeAcceptancePassed: false,
  };
  writeJson(join(build, "runtime-manifest.json"), manifest);
  const engineSource = join(
    root,
    "temp/cache/windows-webkit",
    `source-${config.webkit.upstreamRevision.slice(0, 8)}`,
    "Source/WebCore",
  );
  mkdirSync(engineSource, { recursive: true });
  writeFileSync(join(engineSource, "LICENSE-APPLE"), "unit license fixture");
  const dependency = join(
    build,
    "vcpkg_installed/x64-windows-devduo/share/fixture",
  );
  mkdirSync(dependency, { recursive: true });
  writeFileSync(join(dependency, "copyright"), "unit dependency fixture");
  return { build, manifest };
}

test("WebKit release identities are independent and unsafe metadata fails closed", () => {
  const metadata = loadMetadata();
  assert.equal(webkitNames().tag, `webkit-v${metadata.webkit.version}`);
  assert.equal(
    webkitNames().asset,
    `DevDuo-WebKit-${metadata.webkit.version}-win-x64.zip`,
  );
  metadata.project.version = "9.0.0";
  assert.equal(webkitNames(metadata).asset, webkitNames().asset);
  assert.throws(
    () => requirePinnedArchive(parseMetadata(source)),
    /No WebKit archive/,
  );
  for (const [before, after] of [
    ['asset_prefix = "DevDuo-WebKit"', 'asset_prefix = "../bad"'],
    ['tag_prefix = "webkit-v"', 'tag_prefix = "v"'],
    ['dll_name = "WebKit2.dll"', 'dll_name = "other.dll"'],
    ['archive_sha256 = ""', 'archive_sha256 = "latest"'],
    ['"Windows", "X64"', '"Windows", "X86"'],
  ])
    assert.throws(() => parseMetadata(source.replace(before, after)));
  for (const path of [
    "../outside",
    "/root",
    "bin\\outside",
    "bin/../outside",
    "C:/outside",
    "bin/one.",
    `bin/${String.fromCharCode(0)}`,
  ])
    assert.throws(() => safeRelative(path));
});

test("application preflight checks the published WebKit tag, channel, source report, assets and pinned checksums", () => {
  const root = fixture();
  try {
    const expected = "d".repeat(64);
    writeFileSync(
      join(root, "devduo-metadata.toml"),
      source.replace('archive_sha256 = ""', `archive_sha256 = "${expected}"`),
    );
    const metadata = loadMetadata(root),
      names = webkitNames(metadata),
      fingerprint = sourceFingerprint(root);
    const report = {
      schemaVersion: 1,
      version: metadata.webkit.version,
      asset: names.asset,
      archiveSha256: expected,
      runtimeManifestSha256: "c".repeat(64),
      sourceFingerprint: fingerprint,
      runtime: {
        schemaVersion: 1,
        version: metadata.webkit.version,
        tag: names.tag,
        asset: names.asset,
        upstreamRevision: metadata.webkit.upstreamRevision,
        sourceFingerprint: fingerprint,
        sourceSha: sha,
      },
    };
    const reportText = JSON.stringify(report),
      reportPath = join(root, "report.json");
    writeFileSync(reportPath, reportText);
    const sums = `${expected}  ${names.asset}\n${digest(reportPath)}  ${names.manifest}\n`;
    const baseline: GitHubRelease = {
      tag_name: names.tag,
      draft: false,
      prerelease: true,
      assets: [names.asset, names.manifest, "SHA256SUMS"].map((name) => ({
        name,
        size: 100,
        state: "uploaded",
      })),
    };
    for (const scenario of [
      "valid",
      "draft",
      "missing",
      "channel",
      "assets",
      "tag",
      "hash",
      "sums",
      "network",
    ]) {
      const release = structuredClone(baseline);
      if (scenario === "draft") release.draft = true;
      if (scenario === "channel") release.prerelease = false;
      if (scenario === "assets") release.assets.pop();
      const execute: Runner = (command, args) => {
        if (command === "git")
          return `${scenario === "tag" ? "b".repeat(40) : sha}\trefs/tags/${names.tag}`;
        assert.equal(command, "gh");
        if (args[0] === "api") {
          if (scenario === "network") throw new Error("network failure");
          return JSON.stringify([scenario === "missing" ? [] : [release]]);
        }
        assert.deepEqual(args.slice(0, 2), ["release", "download"]);
        const name = args[args.indexOf("--pattern") + 1],
          destination = args[args.indexOf("--dir") + 1];
        writeFileSync(
          join(destination, name),
          name === names.manifest
            ? scenario === "hash"
              ? JSON.stringify({ ...report, archiveSha256: "b".repeat(64) })
              : reportText
            : scenario === "sums"
              ? "invalid"
              : sums,
        );
        return "";
      };
      if (scenario === "valid") preflightPublishedWebKit(root, execute);
      else
        assert.throws(() => preflightPublishedWebKit(root, execute), scenario);
    }
  } finally {
    cleanup(root);
  }
});

test("runner routing accepts repository-scoped defaults and optional capability labels", () => {
  const defaults = '["self-hosted", "Windows", "X64"]';
  const metadata = source.replace(
    /runner_labels = \[[^\r\n]+\]/,
    `runner_labels = ${defaults}`,
  );
  assert.deepEqual(parseMetadata(metadata).webkit.runnerLabels, [
    "self-hosted",
    "Windows",
    "X64",
  ]);
  assert.deepEqual(
    parseMetadata(
      metadata.replace(
        defaults,
        '["self-hosted", "Windows", "X64", "webkit-builder"]',
      ),
    ).webkit.runnerLabels,
    ["self-hosted", "Windows", "X64", "webkit-builder"],
  );
  for (const labels of [
    '["Windows", "X64"]',
    '["self-hosted", "Linux", "X64"]',
    '["self-hosted", "Windows", "ARM64"]',
    '["self-hosted", "Windows", "X64", "X64"]',
    '["self-hosted", "Windows", "X64", "unsafe label"]',
  ])
    assert.throws(() => parseMetadata(metadata.replace(defaults, labels)));
});

test("manual release planning rejects automatic triggers, wrong branches and abbreviated commits", () => {
  const plan = webkitPlan("workflow_dispatch", "refs/heads/main", sha);
  assert.deepEqual(
    JSON.parse(plan.runner_labels),
    loadMetadata().webkit.runnerLabels,
  );
  assert.equal(
    plan.repository,
    new URL(loadMetadata().project.repositoryUrl).pathname.slice(1),
  );
  for (const event of [
    "push",
    "pull_request",
    "workflow_call",
    "workflow_run",
    "schedule",
  ])
    assert.throws(() => webkitPlan(event, "refs/heads/main", sha));
  assert.throws(() => webkitPlan("workflow_dispatch", "refs/heads/other", sha));
  assert.throws(() =>
    webkitPlan("workflow_dispatch", "refs/heads/main", "aaaaaaa"),
  );
});

test("source fingerprint ignores app/tooling/pin changes but detects engine and Python changes", () => {
  const root = fixture();
  try {
    const before = sourceFingerprint(root);
    writeFileSync(
      join(root, "devduo-metadata.toml"),
      source
        .replace('version = "0.1.0-alpha.1"', 'version = "9.0.0"')
        .replace('archive_sha256 = ""', `archive_sha256 = "${"b".repeat(64)}"`),
    );
    const mise = readFileSync(join(root, "mise.toml"), "utf8");
    writeFileSync(
      join(root, "mise.toml"),
      mise.replace('node = "26"', 'node = "27"'),
    );
    assert.equal(sourceFingerprint(root), before);
    writeFileSync(
      join(root, "mise.toml"),
      mise.replace('python = "3.14"', 'python = "3.15"'),
    );
    assert.notEqual(sourceFingerprint(root), before);
    writeFileSync(join(root, "mise.toml"), mise);
    writeFileSync(join(root, engineInputs[2]), "changed engine build recipe");
    assert.notEqual(sourceFingerprint(root), before);
  } finally {
    cleanup(root);
  }
});

test("workflow policy preserves the workspace and permits only manual dispatch", () => {
  const root = fixture();
  try {
    checkWebKitConfiguration(root);
    const path = join(root, ".github/workflows/webkit-release.yml"),
      workflow = readFileSync(path, "utf8");
    writeFileSync(path, workflow.replace("clean: false", "clean: true"));
    assert.throws(() => checkWebKitConfiguration(root), /preserve caches/);
    writeFileSync(
      path,
      workflow.replace("  workflow_dispatch:", "  workflow_dispatch:\n  push:"),
    );
    assert.throws(() => checkWebKitConfiguration(root), /only trigger/);
  } finally {
    cleanup(root);
  }
});

test("runtime staging validates all components, rejects stale inputs and preserves acceptance qualifications", () => {
  const root = fixture();
  try {
    const { build, manifest } = runtimeFixture(root),
      stage = join(root, "stage");
    stageRuntime(build, stage, sha, root);
    validateRuntime(stage, loadMetadata(root), true, root);
    assert.equal(
      JSON.parse(readFileSync(join(stage, "runtime-manifest.json"), "utf8"))
        .runtimeAcceptancePassed,
      false,
    );
    assert.ok(existsSync(join(stage, "licenses/vcpkg/fixture/copyright")));
    assert.doesNotMatch(
      readFileSync(join(stage, "build-provenance.json"), "utf8"),
      /private install directory/,
    );
    writeFileSync(join(stage, "bin/WebCore.dll"), "corrupt");
    assert.throws(
      () => validateRuntime(stage, loadMetadata(root), true, root),
      /integrity mismatch/,
    );
    const duplicate = structuredClone(manifest);
    duplicate.files.push({
      path: "WEBKIT2.DLL",
      sha256: manifest.files[0].sha256,
    });
    writeJson(join(build, "runtime-manifest.json"), duplicate);
    assert.throws(
      () => validateRuntime(build, loadMetadata(root), false, root),
      /Duplicate/,
    );
    writeJson(join(build, "runtime-manifest.json"), manifest);
    writeFileSync(join(root, engineInputs[2]), "unbuilt recipe");
    assert.throws(
      () => stageRuntime(build, join(root, "stale"), sha, root),
      /Rebuild WebKit/,
    );
  } finally {
    cleanup(root);
  }
});

test("PowerShell BOM provenance is parsed without changing the original manifest bytes", () => {
  const root = fixture();
  try {
    const { build, manifest } = runtimeFixture(root);
    const inputsPath = join(build, "build-inputs.json");
    writeFileSync(inputsPath, `\uFEFF${readFileSync(inputsPath, "utf8")}`);
    manifest.buildInputsSha256 = digest(inputsPath);
    const manifestPath = join(build, "runtime-manifest.json");
    const original = `\uFEFF${JSON.stringify(manifest)}`;
    writeFileSync(manifestPath, original);
    const stage = join(root, "bom-stage");
    stageRuntime(build, stage, sha, root);
    validateRuntime(stage, loadMetadata(root), true, root);
    assert.equal(
      readFileSync(join(stage, "runtime-manifest.json"), "utf8"),
      original,
    );
    assert.equal(
      digest(join(stage, "runtime-manifest.json")),
      digest(manifestPath),
    );
    assert.equal(digest(inputsPath), manifest.buildInputsSha256);
  } finally {
    cleanup(root);
  }
});

test("published output validation refuses changed bytes, wrong commits and mismatched archive pins", () => {
  const root = fixture();
  try {
    const output = join(root, "output"),
      metadata = loadMetadata(root),
      names = webkitNames(metadata);
    mkdirSync(output);
    const archive = join(output, names.asset);
    writeFileSync(archive, "unit archive fixture");
    const fingerprint = sourceFingerprint(root),
      hash = digest(archive);
    writeJson(join(output, names.manifest), {
      schemaVersion: 1,
      asset: names.asset,
      version: metadata.webkit.version,
      archiveSha256: hash,
      runtimeManifestSha256: "c".repeat(64),
      sourceFingerprint: fingerprint,
      runtime: {
        schemaVersion: 1,
        version: metadata.webkit.version,
        tag: names.tag,
        asset: names.asset,
        upstreamRevision: metadata.webkit.upstreamRevision,
        sourceFingerprint: fingerprint,
        sourceSha: sha,
      },
    });
    writeFileSync(
      join(output, "SHA256SUMS"),
      `${hash}  ${names.asset}\n${digest(join(output, names.manifest))}  ${names.manifest}\n`,
    );
    verifyWebKitOutput(output, sha, root);
    assert.throws(
      () => verifyWebKitOutput(output, "b".repeat(40), root),
      /identity/,
    );
    writeFileSync(
      join(root, "devduo-metadata.toml"),
      source.replace(
        'archive_sha256 = ""',
        `archive_sha256 = "${"b".repeat(64)}"`,
      ),
    );
    assert.throws(
      () => verifyWebKitOutput(output, sha, root),
      /already pinned/,
    );
    writeFileSync(join(root, "devduo-metadata.toml"), source);
    writeFileSync(archive, "modified transport");
    assert.throws(() => verifyWebKitOutput(output, sha, root), /integrity/);
  } finally {
    cleanup(root);
  }
});

test("native ZIP handling round-trips bytes and rejects traversal before extraction", {
  skip: process.platform !== "win32",
}, () => {
  const root = fixture();
  try {
    const archiveScript = join(
      repoRoot,
      "native/webkit/windows/RuntimeArchive.ps1",
    );
    const input = join(root, "input"),
      archive = join(root, "runtime.zip"),
      output = join(root, "unpacked");
    mkdirSync(input);
    writeFileSync(join(input, "sample.txt"), "runtime bytes");
    const invoke = (mode: string, directory: string, zip: string) =>
      spawnSync(
        "powershell.exe",
        [
          "-NoProfile",
          "-File",
          archiveScript,
          "-Mode",
          mode,
          "-Directory",
          directory,
          "-Archive",
          zip,
        ],
        { encoding: "utf8" },
      );
    assert.equal(invoke("Pack", input, archive).status, 0);
    assert.equal(invoke("Unpack", output, archive).status, 0);
    assert.equal(
      readFileSync(join(output, "sample.txt"), "utf8"),
      "runtime bytes",
    );
    const invalid = join(root, "invalid.zip");
    const create = spawnSync(
      "powershell.exe",
      [
        "-NoProfile",
        "-Command",
        "Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip=[IO.Compression.ZipFile]::Open($env:ARCHIVE, 'Create'); $entry=$zip.CreateEntry('../escape.txt'); $zip.Dispose()",
      ],
      { env: { ...process.env, ARCHIVE: invalid }, encoding: "utf8" },
    );
    assert.equal(create.status, 0, create.stderr);
    assert.notEqual(
      invoke("Unpack", join(root, "rejected"), invalid).status,
      0,
    );
    assert.equal(existsSync(join(root, "rejected")), false);
    assert.equal(existsSync(join(root, "escape.txt")), false);
  } finally {
    cleanup(root);
  }
});

test("persistent Ninja trees survive toolchain switches and patch refresh refuses unrecorded edits", {
  skip: process.platform !== "win32",
}, () => {
  const root = fixture();
  try {
    const stateRoot = join(root, "native-state"),
      build = join(stateRoot, "temp/build/windows-webkit-host");
    mkdirSync(build, { recursive: true });
    const evidence = join(repoRoot, "native/webkit/windows/RuntimeFiles.ps1");
    const helper = join(repoRoot, "native/webkit/windows/PersistentBuild.ps1");
    const invoke = (code: string, environment: Record<string, string> = {}) =>
      spawnSync(
        "powershell.exe",
        [
          "-NoProfile",
          "-Command",
          `$ErrorActionPreference='Stop'; . $env:EVIDENCE; . $env:HELPER; ${code}`,
        ],
        {
          encoding: "utf8",
          env: {
            ...process.env,
            EVIDENCE: evidence,
            HELPER: helper,
            ROOT: stateRoot,
            BUILD: build,
            ...environment,
          },
        },
      );
    const identityA = "a".repeat(64),
      identityB = "b".repeat(64);
    writeJson(join(build, "build-inputs.json"), {
      buildTreeIdentity: identityA,
    });
    writeFileSync(join(build, ".ninja_log"), "first build cache");
    let result = invoke(
      "Use-WebKitBuildTree $env:ROOT $env:BUILD $env:IDENTITY",
      { IDENTITY: identityB },
    );
    assert.equal(result.status, 0, result.stderr);
    assert.equal(existsSync(build), false);
    assert.equal(
      readFileSync(
        join(
          stateRoot,
          "temp/build/windows-webkit-trees",
          identityA,
          ".ninja_log",
        ),
        "utf8",
      ),
      "first build cache",
    );
    mkdirSync(build);
    writeJson(join(build, "build-inputs.json"), {
      buildTreeIdentity: identityB,
    });
    result = invoke("Use-WebKitBuildTree $env:ROOT $env:BUILD $env:IDENTITY", {
      IDENTITY: identityA,
    });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(
      readFileSync(join(build, ".ninja_log"), "utf8"),
      "first build cache",
    );
    const native = join(root, "native-source");
    mkdirSync(native);
    const git = (args: string[]) => {
      const result = spawnSync("git", ["-C", native, ...args], {
        encoding: "utf8",
      });
      assert.equal(result.status, 0, result.stderr);
      return result.stdout;
    };
    git(["init", "--quiet"]);
    git(["config", "core.autocrlf", "false"]);
    for (const name of ["a-paint.txt", "b-host.txt", "c-extra.txt"])
      writeFileSync(join(native, name), "base\n");
    git(["add", "."]);
    git([
      "-c",
      "user.name=Fixture",
      "-c",
      "user.email=fixture@example.invalid",
      "commit",
      "--quiet",
      "-m",
      "fixture",
    ]);
    const patches: string[][] = [];
    for (const version of [1, 2]) {
      const directory = join(root, `patch-${version}`);
      mkdirSync(directory);
      writeFileSync(join(native, "b-host.txt"), `host ${version}\n`);
      writeFileSync(join(native, "a-paint.txt"), `paint ${version}\n`);
      const host = join(directory, "0001-windows-host-viewport-api.patch"),
        paint = join(directory, "0002-windows-content-painting.patch"),
        developer = join(directory, "0003-windows-developer-api.patch");
      writeFileSync(host, git(["diff", "--", "b-host.txt"]));
      writeFileSync(paint, git(["diff", "--", "a-paint.txt"]));
      // The developer patch deliberately overlaps the host patch's file.
      git(["add", "b-host.txt"]);
      writeFileSync(
        join(native, "b-host.txt"),
        `host ${version}\ndeveloper ${version}\n`,
      );
      writeFileSync(developer, git(["diff", "--", "b-host.txt"]));
      git(["reset", "--quiet", "HEAD", "--", "b-host.txt"]);
      patches.push([host, paint, developer]);
      git(["restore", "--", "a-paint.txt", "b-host.txt"]);
    }
    const sync = (version: number) =>
      invoke(
        "Sync-WebKitPatches $env:SOURCE $env:CACHE $env:REVISION $env:BUILD $env:BUILD @($env:PATCH, $env:PAINT, $env:DEVELOPER)",
        {
          SOURCE: native,
          CACHE: join(root, "cache"),
          REVISION: "c".repeat(40),
          PATCH: patches[version][0],
          PAINT: patches[version][1],
          DEVELOPER: patches[version][2],
        },
      );
    for (const version of [0, 0, 1]) {
      const result = sync(version);
      assert.equal(result.status, 0, result.stderr);
      assert.equal(
        readFileSync(join(native, "a-paint.txt"), "utf8"),
        `paint ${version + 1}\n`,
      );
      assert.equal(
        readFileSync(join(native, "b-host.txt"), "utf8"),
        `host ${version + 1}\ndeveloper ${version + 1}\n`,
      );
    }
    // Successful historical builds retain their original frozen patch paths.
    // Refresh must work from either layout without rewriting that evidence.
    for (const layout of [
      "tests/DevDuo.WebKitProbe/patches",
      "native/webkit/windows/patches",
    ]) {
      const frozen = join(build, "inputs", layout);
      mkdirSync(frozen, { recursive: true });
      for (const path of patches[1]) cpSync(path, join(frozen, basename(path)));
      const hashes = patches[1].map((path) => digest(path));
      rmSync(join(root, "cache/applied-patches"), { recursive: true });
      result = sync(0);
      assert.equal(result.status, 0, result.stderr);
      assert.equal(
        readFileSync(join(native, "a-paint.txt"), "utf8"),
        "paint 1\n",
      );
      for (const [index, path] of patches[1].entries())
        assert.equal(digest(join(frozen, basename(path))), hashes[index]);
      result = sync(1);
      assert.equal(result.status, 0, result.stderr);
      rmSync(frozen, { recursive: true });
    }
    const refreshDirectory = join(root, "patch-paint-only-refresh");
    mkdirSync(refreshDirectory);
    const unchangedHost = join(
        refreshDirectory,
        "0001-windows-host-viewport-api.patch",
      ),
      changedPaint = join(
        refreshDirectory,
        "0002-windows-content-painting.patch",
      ),
      unchangedDeveloper = join(
        refreshDirectory,
        "0003-windows-developer-api.patch",
      );
    cpSync(patches[1][0], unchangedHost);
    cpSync(patches[1][2], unchangedDeveloper);
    writeFileSync(join(native, "a-paint.txt"), "paint 3\n");
    writeFileSync(changedPaint, git(["diff", "--", "a-paint.txt"]));
    writeFileSync(join(native, "a-paint.txt"), "paint 2\n");
    patches.push([unchangedHost, changedPaint, unchangedDeveloper]);
    const oldTimestamp = new Date("2000-01-01T00:00:00Z");
    utimesSync(join(native, "a-paint.txt"), oldTimestamp, oldTimestamp);
    utimesSync(join(native, "b-host.txt"), oldTimestamp, oldTimestamp);
    result = sync(2);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(
      readFileSync(join(native, "a-paint.txt"), "utf8"),
      "paint 3\n",
    );
    assert.equal(
      statSync(join(native, "b-host.txt")).mtimeMs,
      oldTimestamp.getTime(),
    );
    assert.notEqual(
      statSync(join(native, "a-paint.txt")).mtimeMs,
      oldTimestamp.getTime(),
    );
    writeFileSync(join(native, "c-extra.txt"), "preserve this local edit\n");
    assert.notEqual(sync(0).status, 0);
    assert.equal(
      readFileSync(join(native, "c-extra.txt"), "utf8"),
      "preserve this local edit\n",
    );
    assert.equal(
      readFileSync(join(native, "b-host.txt"), "utf8"),
      "host 2\ndeveloper 2\n",
    );
    git(["add", "c-extra.txt"]);
    const staged = git(["diff", "--cached"]);
    assert.notEqual(sync(0).status, 0);
    assert.equal(git(["diff", "--cached"]), staged);
    git(["restore", "--staged", "--worktree", "--", "c-extra.txt"]);
    writeFileSync(
      join(native, "unrecorded.txt"),
      "preserve this untracked source\n",
    );
    assert.notEqual(sync(0).status, 0);
    assert.equal(
      readFileSync(join(native, "unrecorded.txt"), "utf8"),
      "preserve this untracked source\n",
    );
    assert.equal(
      readFileSync(join(native, "b-host.txt"), "utf8"),
      "host 2\ndeveloper 2\n",
    );
  } finally {
    cleanup(root);
  }
});
