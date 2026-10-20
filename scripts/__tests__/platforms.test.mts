import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { parse as parseYaml } from "yaml";
import { loadMetadata, metadataFile, parseMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";
import { checksMatrix, targets } from "../lib/release-policy.mts";

const source = readFileSync(join(repoRoot, metadataFile), "utf8");

test("Linux is excluded from the default check matrix and release artifacts", () => {
  const metadata = loadMetadata();
  assert.deepEqual(metadata.platforms.linux, { check: false, release: false });
  assert.deepEqual(
    checksMatrix(metadata).include.map((entry) => entry.os),
    ["windows-2025", "xcode-27"],
  );
  assert.deepEqual([...targets].sort(), ["osx-arm64", "win-x64"]);
});

test("Linux checks can be enabled independently while unsupported releases fail closed", () => {
  const metadata = parseMetadata(
    source.replace("check = false", "check = true"),
  );
  assert.deepEqual(
    checksMatrix(metadata).include.map((entry) => entry.os),
    ["ubuntu-24.04", "windows-2025", "xcode-27"],
  );
  assert.equal(metadata.platforms.linux.release, false);
  assert.throws(
    () => parseMetadata(source.replace("release = false", "release = true")),
    /Linux release is not implemented/,
  );
  assert.throws(
    () => parseMetadata(source.replace("check = false", 'check = "false"')),
    /must be a boolean/,
  );
  assert.throws(
    () => parseMetadata(source.replace("release = false", 'release = "false"')),
    /must be a boolean/,
  );
});

test("the matrix CLI exports the enabled platforms to GitHub Actions", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo-platforms-"));
  try {
    const output = join(root, "github-output");
    const result = spawnSync(
      process.execPath,
      [join(repoRoot, "scripts/release.mts"), "checks-matrix"],
      {
        cwd: repoRoot,
        encoding: "utf8",
        env: { ...process.env, GITHUB_OUTPUT: output },
      },
    );
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stderr);
    const expected = checksMatrix();
    assert.deepEqual(JSON.parse(result.stdout), expected);
    assert.equal(
      readFileSync(output, "utf8"),
      `matrix=${JSON.stringify(expected)}\n`,
    );
    const workflow = parseYaml(
      readFileSync(join(repoRoot, ".github/workflows/checks.yml"), "utf8"),
    );
    assert.equal(workflow.jobs.portable.needs, "plan");
    assert.equal(
      workflow.jobs.portable.strategy.matrix,
      `\${{ fromJSON(needs.plan.outputs.matrix) }}`,
    );
    assert.match(
      workflow.jobs.plan.steps.find(
        (step: { id?: string }) => step.id === "plan",
      ).run,
      /scripts\/release\.mts checks-matrix/,
    );
    assert.deepEqual(workflow.jobs.report.needs, ["plan", "portable"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("Checks report fails when either matrix planning or platform validation fails", {
  skip: process.platform === "win32",
}, () => {
  const workflow = parseYaml(
    readFileSync(join(repoRoot, ".github/workflows/checks.yml"), "utf8"),
  );
  const script = workflow.jobs.report.steps[0].run;
  for (const [plan, platform] of [
    ["success", "success"],
    ["failure", "success"],
    ["success", "failure"],
    ["failure", "skipped"],
  ]) {
    const result = spawnSync("bash", ["-e", "-c", script], {
      encoding: "utf8",
      env: { ...process.env, PLAN_RESULT: plan, RESULT: platform },
    });
    assert.ifError(result.error);
    assert.equal(
      result.status === 0,
      plan === "success" && platform === "success",
      result.stderr,
    );
  }
});
