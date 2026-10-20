import assert from "node:assert/strict";
import {
  cpSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { repoRoot } from "../lib/paths.mts";
import {
  artifactRecord,
  assemble,
  verifyArtifacts,
  verifyPublished,
} from "../lib/release-artifacts.mts";
import {
  artifactName,
  currentVersion,
  parseVersion,
  releasePlan,
  targets,
} from "../lib/release-policy.mts";

const sha = "a".repeat(40);

test("release policy accepts numbered prereleases and gates stable versions on exact tags", () => {
  for (const channel of ["alpha", "beta"]) {
    const version = `1.2.3-${channel}.1`;
    assert.equal(releasePlan(version, "refs/heads/release", sha).publish, true);
    assert.equal(
      releasePlan(version, `refs/tags/v${version}`, sha).prerelease,
      true,
    );
  }
  assert.equal(releasePlan("1.2.3", "refs/tags/v1.2.3", sha).prerelease, false);
  assert.equal(
    releasePlan("1.2.3-dev", "refs/heads/release", sha).publish,
    false,
  );
  for (const version of [
    "v1.2.3",
    "1.2.3-rc.1",
    "1.2.3-alpha.01",
    "01.2.3",
    "1.2.3+build",
    "1.2.3\ninjected=true",
  ])
    assert.throws(() => parseVersion(version));
  for (const ref of [
    "refs/heads/main",
    "refs/heads/release",
    "refs/tags/v1.2.4",
    "v1.2.3",
  ])
    assert.throws(() => releasePlan("1.2.3", ref, sha));
  assert.throws(() => releasePlan("1.2.3-dev", "refs/tags/v1.2.3-dev", sha));
  assert.throws(() => releasePlan("1.2.3", "refs/tags/v1.2.3", "aaaaaaa"));
  assert.ok(currentVersion());
});

test("assembly requires both exact platform reports and verifies every published byte", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo-release-"));
  try {
    const input = join(root, "input"),
      output = join(root, "final"),
      existing = join(root, "existing");
    const plan = releasePlan("1.2.3-beta.1", "refs/heads/release", sha);
    for (const target of targets) {
      const directory = join(input, `release-${target}`);
      mkdirSync(directory, { recursive: true });
      writeFileSync(
        join(directory, artifactName(plan.version, target)),
        `fake zip for ${target}`,
      );
      writeFileSync(
        join(directory, "report.json"),
        JSON.stringify({
          sourceSha: sha,
          artifact: artifactRecord(directory, plan.version, target),
        }),
      );
    }
    assemble(input, output, plan);
    verifyArtifacts(output, plan);
    cpSync(output, existing, { recursive: true });
    verifyPublished(output, existing, plan);
    assert.throws(() =>
      verifyArtifacts(output, { ...plan, sourceSha: "b".repeat(40) }),
    );
    const archive = join(existing, artifactName(plan.version, "win-x64"));
    writeFileSync(archive, "tampered");
    assert.throws(() => verifyPublished(output, existing, plan), /integrity/);
    writeFileSync(join(output, "unexpected.txt"), "unexpected");
    assert.throws(() => verifyArtifacts(output, plan), /Unexpected/);
    rmSync(join(output, "unexpected.txt"));
    const manifestPath = join(output, "release-manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest.artifacts[0].signed = false;
    writeFileSync(manifestPath, JSON.stringify(manifest));
    assert.throws(() => verifyArtifacts(output, plan), /signing metadata/);
    const reportPath = join(input, "release-osx-arm64/report.json");
    const report = JSON.parse(readFileSync(reportPath, "utf8"));
    report.sourceSha = "b".repeat(40);
    writeFileSync(reportPath, JSON.stringify(report));
    assert.throws(
      () => assemble(input, output, plan),
      /Invalid platform report/,
    );
    rmSync(join(input, "release-osx-arm64"), { recursive: true });
    assert.throws(() => assemble(input, output, plan));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("checksums, platform identity and duplicate entries cannot be substituted", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo-release-"));
  const plan = releasePlan("1.2.3", "refs/tags/v1.2.3", sha);
  try {
    for (const target of targets) {
      const dir = join(root, "inputs", `release-${target}`);
      mkdirSync(dir, { recursive: true });
      writeFileSync(join(dir, artifactName(plan.version, target)), target);
      writeFileSync(
        join(dir, "report.json"),
        JSON.stringify({
          sourceSha: sha,
          artifact: artifactRecord(dir, plan.version, target),
        }),
      );
    }
    const output = join(root, "output");
    assemble(join(root, "inputs"), output, plan);
    const checksums = readFileSync(join(output, "SHA256SUMS"), "utf8");
    writeFileSync(
      join(output, "SHA256SUMS"),
      checksums.replace(/[a-f0-9]/, "z"),
    );
    assert.throws(() => verifyArtifacts(output, plan), /Checksum/);
    writeFileSync(join(output, "SHA256SUMS"), checksums);
    const path = join(output, "release-manifest.json");
    const manifest = JSON.parse(readFileSync(path, "utf8"));
    manifest.artifacts[1] = manifest.artifacts[0];
    writeFileSync(path, JSON.stringify(manifest));
    assert.throws(() => verifyArtifacts(output, plan), /integrity/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("workflow gates credentials and publication on tested source and approval", () => {
  const workflow = readFileSync(
    join(repoRoot, ".github/workflows/release.yml"),
    "utf8",
  );
  assert.match(workflow, /uses: \.\/\.github\/workflows\/checks.yml/);
  assert.match(workflow, /environment: stable-release/);
  assert.match(workflow, /environment: release-signing/);
  assert.match(workflow, /needs\.stable-approval\.result == 'success'/);
  assert.match(workflow, /needs: \[macos, windows\]/);
  assert.match(workflow, /node scripts\/release.mts verify-published/);
  assert.doesNotMatch(
    workflow,
    /--clobber|pull_request_target|secrets: inherit/,
  );
  const docs = readFileSync(
    join(repoRoot, ".github/workflows/docs.yml"),
    "utf8",
  );
  assert.match(
    docs,
    /github.ref == 'refs\/heads\/main' && github.event_name != 'pull_request'/,
  );
});

test("publication never exposes failed uploads or mismatched existing assets", {
  skip: process.platform === "win32",
}, async () => {
  const { spawnSync } = await import("node:child_process");
  const workflow = readFileSync(
    join(repoRoot, ".github/workflows/release.yml"),
    "utf8",
  );
  const start = workflow.indexOf(
    "          set -euo pipefail",
    workflow.indexOf("      - name: Publish without"),
  );
  const source = workflow
    .slice(start, workflow.indexOf("\n  release-report:", start))
    .replace(/^ {10}/gm, "");
  const root = mkdtempSync(join(tmpdir(), "devduo-publish-"));
  try {
    const bin = join(root, "bin");
    mkdirSync(bin);
    writeFileSync(
      join(bin, "gh"),
      `#!/bin/bash
set -eu
printf '%s\\n' "$*" >> "$CALLS"
case "$1 $2" in
  'api --paginate')
    [[ "$SCENARIO" != list-failed ]] || exit 1
    if [[ "$SCENARIO" == mismatch || "$SCENARIO" == published ]]; then echo 42; fi ;;
  'release create') [[ "$SCENARIO" != upload-failed ]] || exit 1 ;;
  'release view')
    if [[ "$SCENARIO" == published ]]; then echo false; else echo true; fi ;;
esac
`,
      { mode: 0o755 },
    );
    writeFileSync(
      join(bin, "mise"),
      `#!/bin/bash
set -eu
printf '%s\\n' "$*" >> "$CALLS"
[[ "$SCENARIO" != mismatch && "$SCENARIO" != corrupt-upload ]]
`,
      { mode: 0o755 },
    );
    for (const scenario of [
      "list-failed",
      "mismatch",
      "upload-failed",
      "corrupt-upload",
      "success",
      "published",
    ]) {
      const directory = join(root, scenario),
        calls = join(root, `${scenario}.log`);
      mkdirSync(directory);
      const result = spawnSync("bash", ["-c", source], {
        cwd: directory,
        encoding: "utf8",
        env: {
          ...process.env,
          PATH: `${bin}:${process.env.PATH}`,
          CALLS: calls,
          SCENARIO: scenario,
          TAG: "v1.2.3",
          DISPLAY_NAME: "DevDuo",
          PRERELEASE: "false",
          GITHUB_REPOSITORY: "example/repo",
        },
      });
      assert.ifError(result.error);
      const log = readFileSync(calls, "utf8");
      assert.equal(
        result.status === 0,
        ["success", "published"].includes(scenario),
        result.stderr,
      );
      if (scenario === "success") {
        assert.ok(
          log.indexOf("verify-published") < log.indexOf("release edit"),
        );
        assert.match(log, /release create.*--draft/);
      } else assert.doesNotMatch(log, /release edit/);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
