import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { loadMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";
import { assemble, digest, windowsRuntime } from "../lib/release-artifacts.mts";
import { changelogEntry, releaseNotes } from "../lib/release-notes.mts";
import {
  artifactName,
  currentVersion,
  parseVersion,
  releasePlan,
  targets,
} from "../lib/release-policy.mts";

const sha = "a".repeat(40);
const metadata = loadMetadata();

test("application planning leaves engine availability to the dependency workflow", () => {
  const result = spawnSync(
    process.execPath,
    [join(repoRoot, "scripts/release.mts"), "plan"],
    {
      cwd: repoRoot,
      env: {
        ...process.env,
        GITHUB_REF: `refs/tags/${metadata.release.tagPrefix}${metadata.project.version}`,
        GITHUB_SHA: sha,
        GH_TOKEN: "invalid-test-token",
        GITHUB_OUTPUT: "",
      },
      encoding: "utf8",
    },
  );
  assert.equal(result.status, 0, result.stderr);
  assert.equal(JSON.parse(result.stdout).publish, true);
});

test("release policy accepts release branches and matching tags for prerelease and stable versions", () => {
  for (const channel of ["alpha", "beta"]) {
    const version = `1.2.3-${channel}.1`;
    assert.equal(releasePlan(version, "refs/heads/release", sha).publish, true);
    assert.equal(
      releasePlan(version, `refs/tags/v${version}`, sha).prerelease,
      true,
    );
  }
  for (const ref of ["refs/heads/release", "refs/tags/v1.2.3"]) {
    const stable = releasePlan("1.2.3", ref, sha);
    assert.equal(stable.publish, true);
    assert.equal(stable.prerelease, false);
    assert.equal(stable.tag, "v1.2.3");
    assert.equal(stable.sourceRef, ref);
    assert.equal(stable.sourceSha, sha);
  }
  assert.equal(
    releasePlan("1.2.3-dev", "refs/heads/release", sha).publish,
    false,
  );
  assert.equal(
    releasePlan("1.2.3", "refs/tags/v1.2.3", "abc1234").sourceSha,
    "abc1234",
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
    "refs/heads/feature",
    "refs/tags/v1.2.4",
    "v1.2.3",
  ])
    assert.throws(() => releasePlan("1.2.3", ref, sha));
  assert.throws(() => releasePlan("1.2.3-dev", "refs/tags/v1.2.3-dev", sha));
  assert.ok(currentVersion());
});

test("assembly copies platform archives and writes the published manifest and checksums", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo-release-"));
  try {
    const input = join(root, "input"),
      output = join(root, "final");
    const plan = releasePlan("1.2.3-beta.1", "refs/heads/release", sha);
    mkdirSync(join(root, "docs/zh"), { recursive: true });
    mkdirSync(join(root, ".github"));
    writeFileSync(
      join(root, "CHANGELOG.md"),
      `## ${plan.version}\n\n- Current fix.\n\n## 1.2.2\n\n- Old fix.`,
    );
    writeFileSync(
      join(root, "docs/zh/CHANGELOG.md"),
      `## ${plan.version}\n\n- 当前修复。`,
    );
    writeFileSync(
      join(root, ".github/release-notes.md"),
      "Installation instructions.",
    );
    for (const target of targets) {
      const directory = join(input, `release-${target}`);
      mkdirSync(directory, { recursive: true });
      writeFileSync(
        join(directory, artifactName(plan.version, target)),
        `fake zip for ${target}`,
      );
    }
    assemble(input, output, plan, metadata, root);
    const notes = readFileSync(join(output, "release-notes.md"), "utf8");
    assert.equal(notes, releaseNotes(plan.version, root));
    assert.match(notes, /Current fix/);
    assert.match(notes, /当前修复/);
    assert.match(notes, /Installation instructions/);
    assert.doesNotMatch(notes, /Old fix/);
    const names = targets.map((target) => artifactName(plan.version, target));
    assert.deepEqual(
      readdirSync(output).sort(),
      [
        ...names,
        "SHA256SUMS",
        "release-manifest.json",
        "release-notes.md",
      ].sort(),
    );
    const manifest = JSON.parse(
      readFileSync(join(output, "release-manifest.json"), "utf8"),
    );
    assert.equal(manifest.sourceSha, sha);
    assert.deepEqual(manifest.windowsWebKit, windowsRuntime(metadata));
    assert.equal(manifest.windowsWebKit.archiveSha256, undefined);
    assert.equal(
      readFileSync(join(output, "SHA256SUMS"), "utf8"),
      `${[
        ...names.map((name) => `${digest(join(output, name))}  ${name}`),
        `${digest(join(output, "release-manifest.json"))}  release-manifest.json`,
      ].join("\n")}\n`,
    );
    rmSync(join(input, "release-osx-arm64"), { recursive: true });
    assert.throws(() =>
      assemble(input, join(root, "missing"), plan, metadata, root),
    );
    writeFileSync(
      join(root, "docs/zh/CHANGELOG.md"),
      "## 0.0.1\n\nOther version.",
    );
    assert.throws(
      () => releaseNotes(plan.version, root),
      /docs\/zh\/CHANGELOG.md/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("changelog extraction selects exact versions and preserves nested markdown", () => {
  const source =
    "# Changes\r\n\r\n## Unreleased\r\nFuture\r\n## 1.2.3-beta.1\r\n\r\n### Fixed\r\n\r\n- **Detail**\r\n\r\n```md\r\n## 9.9.9\r\n```\r\n\r\n## 1.2.3\r\nStable\r\n## 1.2.2\r\nOld";
  assert.equal(
    changelogEntry(source, "1.2.3-beta.1"),
    "### Fixed\n\n- **Detail**\n\n```md\n## 9.9.9\n```",
  );
  assert.equal(changelogEntry(source, "1.2.3"), "Stable");
  for (const text of [
    "## 1.2.30\nWrong",
    "## 1.2.3\n\n",
    "## 1.2.3\n<!-- pending -->",
    "## 1.2.3\nOne\n## 1.2.3\nTwo",
  ])
    assert.throws(() => changelogEntry(text, "1.2.3"), /exactly one nonempty/);
});
