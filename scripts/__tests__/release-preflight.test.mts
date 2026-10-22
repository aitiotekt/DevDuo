import assert from "node:assert/strict";
import test, { after, before } from "node:test";
import { loadMetadata } from "../lib/metadata.mts";
import type { Runner } from "../lib/release-github.mts";
import { releasePlan } from "../lib/release-policy.mts";
import { preflightApplicationRelease } from "../lib/release-preflight.mts";

const sha = "a".repeat(40);
const plan = releasePlan("0.1.0-alpha.1", "refs/heads/release", sha);
const originalRepository = process.env.GITHUB_REPOSITORY;

before(() => {
  // Unit fixtures describe the canonical release repository, including on forks.
  process.env.GITHUB_REPOSITORY = new URL(
    loadMetadata().project.repositoryUrl,
  ).pathname.slice(1);
});
after(() => {
  if (originalRepository === undefined) delete process.env.GITHUB_REPOSITORY;
  else process.env.GITHUB_REPOSITORY = originalRepository;
});

test("development plans perform no remote lookup or runtime preflight", () => {
  const development = releasePlan("0.1.0-dev", "refs/heads/release", sha);
  const unexpected = () => {
    assert.fail("Development skips cannot access remote or runtime gates");
  };
  preflightApplicationRelease(development, unexpected, unexpected);
});

function remoteTag(existing: string | undefined) {
  const calls: string[][] = [];
  const execute: Runner = (command, args, capture) => {
    assert.equal(command, "git");
    assert.equal(capture, true);
    assert.deepEqual(args, [
      "ls-remote",
      "origin",
      `refs/tags/${plan.tag}`,
      `refs/tags/${plan.tag}^{}`,
    ]);
    calls.push(args);
    return existing ? `${existing}\trefs/tags/${plan.tag}\n` : "";
  };
  return { execute, calls };
}

test("a version tagged at another commit fails before the native runtime gate", () => {
  const { execute, calls } = remoteTag("b".repeat(40));
  let runtimeChecks = 0;
  assert.throws(
    () =>
      preflightApplicationRelease(plan, execute, () => {
        runtimeChecks++;
      }),
    /Bump the version, or retry the original source and release-final artifact/,
  );
  assert.equal(runtimeChecks, 0);
  assert.equal(calls.length, 1);
});

test("a new application version and an exact source retry still require runtime preflight", () => {
  for (const existing of [undefined, sha]) {
    const { execute, calls } = remoteTag(existing);
    let runtimeChecks = 0;
    preflightApplicationRelease(plan, execute, () => {
      runtimeChecks++;
    });
    assert.equal(runtimeChecks, 1);
    assert.equal(calls.length, 1);
  }
});

test("source and remote-read failures cannot bypass the runtime or signing gates", () => {
  let runtimeChecks = 0;
  const runtime = () => {
    runtimeChecks++;
  };
  const execute: Runner = () => {
    throw new Error("Remote tag lookup failed");
  };
  assert.throws(
    () =>
      preflightApplicationRelease(
        { ...plan, sourceSha: "abc" },
        execute,
        runtime,
      ),
    /full source commit SHA/,
  );
  assert.throws(
    () => preflightApplicationRelease(plan, execute, runtime),
    /Remote tag lookup failed/,
  );
  assert.equal(runtimeChecks, 0);
  assert.throws(
    () =>
      preflightApplicationRelease(plan, remoteTag(undefined).execute, () => {
        throw new Error("Runtime is not pinned");
      }),
    /Runtime is not pinned/,
  );
});
