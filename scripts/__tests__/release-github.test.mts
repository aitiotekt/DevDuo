import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join } from "node:path";
import test from "node:test";
import { loadMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";
import { digest } from "../lib/release-artifacts.mts";
import {
  dryRun,
  ensureTag,
  findRelease,
  type GitHubRelease,
  publishRelease,
  type Runner,
  tagCommit,
} from "../lib/release-github.mts";
import { parseVersion } from "../lib/release-version.mts";

const sha = "a".repeat(40),
  tag = "v1.2.3-beta.1";

function fixture() {
  const directory = mkdtempSync(join(tmpdir(), "devduo-publish-tests-"));
  const files = ["app.zip", "SHA256SUMS"];
  for (const name of files)
    writeFileSync(join(directory, name), `fixture bytes ${name}`);
  const state: {
    release?: GitHubRelease;
    commit?: string;
    remote: Map<string, Buffer>;
    calls: string[][];
    fail?: string;
    pendingDraft?: boolean;
  } = { remote: new Map(), calls: [] };
  const refreshAssets = () => {
    if (state.release)
      state.release.assets = [...state.remote].map(([name, value]) => ({
        name,
        size: value.length,
        state: "uploaded",
      }));
  };
  const execute: Runner = (command, args) => {
    state.calls.push([command, ...args]);
    if (command === "git")
      return state.commit ? `${state.commit}\trefs/tags/${tag}` : "";
    assert.equal(command, "gh");
    if (args[0] === "api") {
      if (args.includes("POST")) {
        if (state.fail === "tag") throw new Error("tag failure");
        state.commit = sha;
        return "{}";
      }
      if (state.fail === "list") throw new Error("listing failure");
      if (args[1] === "graphql") {
        if (state.fail === "graphql")
          throw new Error("GraphQL permission failure");
        return JSON.stringify({
          data: {
            repository: {
              release: state.release
                ? { databaseId: state.release.id, isDraft: state.release.draft }
                : null,
            },
          },
        });
      }
      if (args[1] === "repos/fixture/repo/releases/17")
        return JSON.stringify(state.release);
      return JSON.stringify([state.release ? [state.release] : []]);
    }
    const operation = args[1];
    if (operation === "create") {
      state.release = {
        id: 17,
        tag_name: state.pendingDraft ? "untagged-fixture" : tag,
        draft: true,
        prerelease: true,
        assets: [],
      };
    } else if (operation === "upload") {
      for (const path of args.slice(3, args.indexOf("--repo"))) {
        state.remote.set(basename(path), readFileSync(path));
        refreshAssets();
        if (state.fail === "upload") throw new Error("upload failure");
      }
    } else if (operation === "edit") {
      assert.ok(state.release);
      if (state.fail === "edit") throw new Error("publication failure");
      state.release.draft = false;
      state.release.tag_name = tag;
    } else throw new Error(`Unexpected fake operation: ${operation}`);
    return "";
  };
  const options = {
    repository: "fixture/repo",
    tag,
    sha,
    prerelease: true,
    latest: false,
    title: "Fixture",
    notes: join(repoRoot, ".github/release-notes.md"),
    directory,
    files,
  };
  const seed = (draft: boolean, names = files) => {
    state.commit = sha;
    state.release = {
      id: 17,
      tag_name: tag,
      draft,
      prerelease: true,
      assets: [],
    };
    for (const name of names)
      state.remote.set(name, readFileSync(join(directory, name)));
    refreshAssets();
  };
  return {
    directory,
    state,
    execute,
    options,
    seed,
    cleanup: () => rmSync(directory, { recursive: true }),
  };
}

test("publication creates a tag and draft, uploads assets, then publishes", () => {
  const f = fixture();
  try {
    assert.equal(publishRelease(f.options, f.execute), "published");
    assert.equal(f.state.release?.draft, false);
    assert.equal(f.state.commit, sha);
    for (const name of f.options.files)
      assert.deepEqual(
        f.state.remote.get(name),
        readFileSync(join(f.directory, name)),
      );
    f.state.calls.length = 0;
    assert.equal(publishRelease(f.options, f.execute), "existing");
    assert.ok(
      f.state.calls.every(
        (args) => !["upload", "create", "edit"].includes(args[2]),
      ),
    );
  } finally {
    f.cleanup();
  }
});

test("an interrupted draft upload resumes only missing assets", () => {
  const f = fixture();
  try {
    f.state.fail = "upload";
    assert.throws(() => publishRelease(f.options, f.execute), /upload failure/);
    assert.equal(f.state.release?.draft, true);
    assert.equal(f.state.remote.size, 1);
    f.state.calls.length = 0;
    delete f.state.fail;
    assert.equal(publishRelease(f.options, f.execute), "published");
    assert.equal(f.state.remote.size, f.options.files.length);
    assert.deepEqual(
      f.state.remote.get("SHA256SUMS"),
      readFileSync(join(f.directory, "SHA256SUMS")),
    );
  } finally {
    f.cleanup();
  }
});

test("pending-tag drafts are found after creation and resumed without duplicate releases", () => {
  const f = fixture();
  try {
    f.state.pendingDraft = true;
    f.state.fail = "upload";
    assert.throws(() => publishRelease(f.options, f.execute), /upload failure/);
    assert.match(f.state.release?.tag_name ?? "", /^untagged-/);
    assert.equal(f.state.remote.size, 1);
    delete f.state.fail;
    f.state.calls.length = 0;
    assert.equal(publishRelease(f.options, f.execute), "published");
    assert.ok(f.state.calls.every((args) => args[2] !== "create"));
    assert.equal(f.state.remote.size, f.options.files.length);
    assert.deepEqual(
      f.state.remote.get("SHA256SUMS"),
      readFileSync(join(f.directory, "SHA256SUMS")),
    );
    assert.equal(f.state.release?.id, 17);
    assert.equal(f.state.release?.tag_name, tag);
  } finally {
    f.cleanup();
  }
});

test("pending-tag lookup rejects permissions, malformed data and changed identities before mutation", () => {
  const f = fixture();
  try {
    f.state.fail = "graphql";
    assert.throws(() => publishRelease(f.options, f.execute), /permission/);
    assert.ok(
      f.state.calls.every(
        (args) =>
          !["POST", "create", "upload", "edit"].some((value) =>
            args.includes(value),
          ),
      ),
    );
    for (const response of [
      {},
      {
        data: { repository: { release: null } },
        errors: [{ message: "denied" }],
      },
      {
        data: { repository: { release: { databaseId: "17", isDraft: true } } },
      },
    ]) {
      assert.throws(() =>
        findRelease("fixture/repo", tag, (_command, args) =>
          args[1] === "graphql" ? JSON.stringify(response) : "[[]]",
        ),
      );
    }
    assert.throws(
      () =>
        findRelease("fixture/repo", tag, (_command, args) => {
          if (args[1] === "graphql")
            return JSON.stringify({
              data: {
                repository: { release: { databaseId: 17, isDraft: true } },
              },
            });
          if (args[1] === "repos/fixture/repo/releases/17")
            return JSON.stringify({
              id: 18,
              tag_name: "untagged-other",
              draft: true,
              prerelease: true,
              assets: [],
            });
          return "[[]]";
        }),
      /identity changed/,
    );
    f.seed(false);
    assert.equal(findRelease("fixture/repo", tag, f.execute)?.draft, false);
  } finally {
    f.cleanup();
  }
});

test("listing, tag, upload and publish failures stop before a later step", () => {
  for (const failure of ["list", "tag", "upload", "edit"]) {
    const f = fixture();
    try {
      f.state.fail = failure;
      assert.throws(() => publishRelease(f.options, f.execute), failure);
      assert.notEqual(f.state.release?.draft, false);
      if (failure !== "edit")
        assert.ok(f.state.calls.every((args) => args[2] !== "edit"));
    } finally {
      f.cleanup();
    }
  }
});

test("existing releases receive missing assets without byte, tag or channel comparisons", () => {
  const f = fixture();
  try {
    f.seed(false, ["app.zip"]);
    f.state.commit = "b".repeat(40);
    assert.ok(f.state.release);
    f.state.release.prerelease = false;
    writeFileSync(join(f.directory, "app.zip"), "different local bytes");
    assert.equal(publishRelease(f.options, f.execute), "existing");
    assert.equal(f.state.remote.size, f.options.files.length);
    assert.deepEqual(
      f.state.remote.get("SHA256SUMS"),
      readFileSync(join(f.directory, "SHA256SUMS")),
    );
    assert.ok(
      f.state.calls.every(
        (args) => !["download", "create", "edit", "POST"].includes(args[2]),
      ),
    );
  } finally {
    f.cleanup();
  }
});

test("tags are created only when missing and annotated tags resolve to their peeled commit", () => {
  const f = fixture();
  try {
    f.state.commit = "b".repeat(40);
    ensureTag("fixture/repo", tag, sha, f.execute);
    assert.equal(f.state.commit, "b".repeat(40));
    delete f.state.commit;
    ensureTag("fixture/repo", tag, sha, f.execute);
    assert.equal(f.state.commit, sha);
    const annotated: Runner = () =>
      `${"b".repeat(40)}\trefs/tags/${tag}\n${sha}\trefs/tags/${tag}^{}`;
    assert.equal(tagCommit(tag, annotated), sha);
    assert.throws(
      () => findRelease("fixture/repo", tag, () => "{}"),
      /Invalid GitHub/,
    );
  } finally {
    f.cleanup();
  }
});

test("only an explicit true value selects a dry run", () => {
  assert.equal(dryRun("true"), true);
  for (const value of ["false", "", "TRUE"]) assert.equal(dryRun(value), false);
});

test("publication CLIs refuse dry runs before reading assets or mutating GitHub", () => {
  const metadata = loadMetadata();
  const applicationRefs = [`refs/heads/${metadata.release.branch}`];
  // Development versions skip publication and cannot be tagged for release.
  if (parseVersion(metadata.project.version).channel !== "dev")
    applicationRefs.push(
      `refs/tags/${metadata.release.tagPrefix}${metadata.project.version}`,
    );
  for (const [script, command, ref] of [
    ...applicationRefs.flatMap((ref) => [
      ["release.mts", "publish", ref],
      ["release.mts", "ensure-tag", ref],
    ]),
    [
      "webkit-release.mts",
      "publish",
      `refs/heads/${metadata.webkit.releaseBranch}`,
    ],
  ]) {
    const result = spawnSync(
      process.execPath,
      [join(repoRoot, "scripts", script), command],
      {
        cwd: repoRoot,
        encoding: "utf8",
        windowsHide: true,
        env: {
          ...process.env,
          GITHUB_EVENT_NAME: "workflow_dispatch",
          GITHUB_REF: ref,
          GITHUB_SHA: sha,
          RELEASE_DRY_RUN: "true",
          GITHUB_ACTIONS: "false",
        },
      },
    );
    assert.ifError(result.error);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /[Dd]ry.run/);
  }
});

test("large artifact hashing matches SHA-256 without reading the whole archive into memory", () => {
  const f = fixture();
  try {
    const bytes = Buffer.alloc(3 * 1024 * 1024 + 17, 89),
      file = join(f.directory, "large.zip");
    writeFileSync(file, bytes);
    assert.equal(
      digest(file),
      createHash("sha256").update(bytes).digest("hex"),
    );
    assert.throws(() => digest(join(f.directory, "missing.zip")));
  } finally {
    f.cleanup();
  }
});
