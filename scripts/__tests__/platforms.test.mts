import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { loadMetadata, metadataFile, parseMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";
import { checksMatrix } from "../lib/release-policy.mts";

const source = readFileSync(join(repoRoot, metadataFile), "utf8");

test("Linux checks can be enabled independently while unsupported releases fail closed", () => {
  const metadata = parseMetadata(
    source.replace("check = false", "check = true"),
  );
  const baseline = checksMatrix(loadMetadata()).include;
  const expanded = checksMatrix(metadata).include;
  assert.equal(expanded.length, baseline.length + 1);
  for (const entry of baseline)
    assert.ok(expanded.some((candidate) => candidate.os === entry.os));
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
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
