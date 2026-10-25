import assert from "node:assert/strict";
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
import { assemble, digest, windowsRuntime } from "../lib/release-artifacts.mts";
import {
  artifactName,
  currentVersion,
  parseVersion,
  releasePlan,
  targets,
} from "../lib/release-policy.mts";

const sha = "a".repeat(40);
const metadata = loadMetadata();

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
    for (const target of targets) {
      const directory = join(input, `release-${target}`);
      mkdirSync(directory, { recursive: true });
      writeFileSync(
        join(directory, artifactName(plan.version, target)),
        `fake zip for ${target}`,
      );
    }
    assemble(input, output, plan, metadata);
    const names = targets.map((target) => artifactName(plan.version, target));
    assert.deepEqual(
      readdirSync(output).sort(),
      [...names, "SHA256SUMS", "release-manifest.json"].sort(),
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
    assert.throws(() => assemble(input, join(root, "missing"), plan, metadata));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
