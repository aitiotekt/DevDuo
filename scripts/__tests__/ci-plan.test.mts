import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { changeRange, documentationOnly, planChecks } from "../lib/ci-plan.mts";

test("range selection covers every push commit and the full PR comparison", () => {
  const base = "a".repeat(40),
    head = "b".repeat(40),
    merge = "c".repeat(40);
  assert.equal(changeRange("push", { before: base }, head), `${base}..${head}`);
  assert.equal(
    changeRange(
      "pull_request",
      { pull_request: { base: { sha: base }, head: { sha: head } } },
      merge,
    ),
    `${base}...${head}`,
  );
  assert.equal(changeRange("push", { before: "0".repeat(40) }, head), null);
  assert.equal(changeRange("push", { before: "--output=unsafe" }, head), null);
  assert.equal(changeRange("workflow_dispatch", {}, head), null);
});

test("only a complete, nonempty documentation-only list skips native compilation", () => {
  assert.equal(
    documentationOnly(["README.md", "docs/zh/013-RELEASE.md"]),
    true,
  );
  for (const paths of [
    [],
    ["docs/en/013-RELEASE.md", "src/DevDuo/MainWindow.cs"],
    ["package.json"],
    [".github/workflows/checks.yml"],
    ["unknown-input"],
  ])
    assert.equal(documentationOnly(paths), false);
});

test("a later docs commit cannot conceal an earlier native change in the same push", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo-ci-plan-"));
  const git = (...args: string[]) => {
    const result = spawnSync("git", args, {
      cwd: root,
      encoding: "utf8",
      windowsHide: true,
      env: {
        ...process.env,
        GIT_AUTHOR_NAME: "Fixture",
        GIT_AUTHOR_EMAIL: "fixture@example.invalid",
        GIT_COMMITTER_NAME: "Fixture",
        GIT_COMMITTER_EMAIL: "fixture@example.invalid",
      },
    });
    assert.ifError(result.error);
    assert.equal(result.status, 0, result.stderr);
    return result.stdout.trim();
  };
  try {
    git("init");
    writeFileSync(join(root, "README.md"), "base\n");
    git("add", ".");
    git("commit", "-m", "base");
    const base = git("rev-parse", "HEAD");
    writeFileSync(join(root, "native.cs"), "native change\n");
    git("add", ".");
    git("commit", "-m", "native");
    const native = git("rev-parse", "HEAD");
    writeFileSync(join(root, "README.md"), "docs\n");
    git("add", ".");
    git("commit", "-m", "docs");
    const head = git("rev-parse", "HEAD");
    assert.equal(
      planChecks("push", { before: base }, head, false, root).native,
      true,
    );
    assert.equal(
      planChecks("push", { before: native }, head, false, root).native,
      true,
    );
    assert.equal(
      planChecks(
        "pull_request",
        { pull_request: { base: { sha: native }, head: { sha: head } } },
        head,
        false,
        root,
      ).native,
      false,
    );
    assert.equal(
      planChecks("push", { before: native }, head, true, root).native,
      true,
    );
    const unavailable = planChecks(
      "push",
      { before: "e".repeat(40) },
      head,
      false,
      root,
    );
    assert.equal(unavailable.native, true);
    assert.match(unavailable.reason, /unavailable/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
