import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join } from "node:path";
import test from "node:test";
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
    } else if (operation === "download") {
      const name = args[args.indexOf("--pattern") + 1],
        destination = args[args.indexOf("--dir") + 1];
      const value = state.remote.get(name);
      assert.ok(value);
      writeFileSync(
        join(destination, name),
        state.fail === "corrupt" ? Buffer.alloc(value.length) : value,
      );
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
    mayCreateTag: true,
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

test("publication creates an exact tag and draft, verifies uploaded bytes, then publishes", () => {
  const f = fixture();
  try {
    assert.equal(publishRelease(f.options, f.execute), "published");
    const calls = f.state.calls.map((args) => args.join(" "));
    const edit = calls.findIndex((line) => line.includes("release edit"));
    assert.ok(
      calls.findIndex((line) => line.includes("git/refs")) <
        calls.findIndex((line) => line.includes("release create")),
    );
    assert.ok(
      calls.findLastIndex((line) => line.includes("release download")) < edit,
    );
    assert.match(calls[edit], /--latest=false/);
    assert.ok(calls.every((line) => !line.includes("--clobber")));
    f.state.calls.length = 0;
    assert.equal(publishRelease(f.options, f.execute), "verified");
    assert.ok(
      f.state.calls.every(
        (args) => !["upload", "create", "edit"].includes(args[2]),
      ),
    );
  } finally {
    f.cleanup();
  }
});

test("an interrupted draft upload resumes only missing assets from the identical artifact set", () => {
  const f = fixture();
  try {
    f.state.fail = "upload";
    assert.throws(() => publishRelease(f.options, f.execute), /upload failure/);
    assert.equal(f.state.release?.draft, true);
    assert.equal(f.state.remote.size, 1);
    f.state.calls.length = 0;
    delete f.state.fail;
    assert.equal(publishRelease(f.options, f.execute), "published");
    const upload = f.state.calls.find((args) => args[2] === "upload");
    assert.ok(upload);
    assert.ok(upload.includes(join(f.directory, "SHA256SUMS")));
    assert.ok(!upload.includes(join(f.directory, "app.zip")));
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
    assert.ok(
      f.state.calls.some(
        (args) => args[2] === "repos/fixture/repo/releases/17",
      ),
    );
    assert.ok(f.state.calls.every((args) => args[2] !== "create"));
    const upload = f.state.calls.find((args) => args[2] === "upload");
    assert.ok(upload?.includes(join(f.directory, "SHA256SUMS")));
    assert.ok(!upload?.includes(join(f.directory, "app.zip")));
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

test("release identity changes during upload stop publication", () => {
  const f = fixture();
  try {
    const execute: Runner = (command, args, capture) => {
      const result = f.execute(command, args, capture);
      if (command === "gh" && args[1] === "upload") {
        assert.ok(f.state.release);
        f.state.release.id = 18;
      }
      return result;
    };
    assert.throws(
      () => publishRelease(f.options, execute),
      /Draft release changed/,
    );
    assert.equal(f.state.release?.draft, true);
    assert.ok(f.state.calls.every((args) => args[2] !== "edit"));
  } finally {
    f.cleanup();
  }
});

test("listing, tag, upload, byte-validation and publish failures never expose a release", () => {
  for (const failure of ["list", "tag", "upload", "corrupt", "edit"]) {
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

test("changed drafts, incomplete published releases and mismatched tags/channels cannot be repaired", () => {
  for (const scenario of ["bytes", "incomplete", "tag", "channel", "extra"]) {
    const f = fixture();
    try {
      f.seed(
        scenario !== "incomplete",
        scenario === "incomplete" ? ["app.zip"] : undefined,
      );
      if (scenario === "bytes") {
        writeFileSync(
          join(f.directory, "app.zip"),
          Buffer.alloc(readFileSync(join(f.directory, "app.zip")).length),
        );
      } else if (scenario === "tag") f.state.commit = "b".repeat(40);
      else if (scenario === "channel") {
        assert.ok(f.state.release);
        f.state.release.prerelease = false;
      } else if (scenario === "extra") {
        assert.ok(f.state.release);
        f.state.release.assets.push({
          name: "extra.zip",
          size: 1,
          state: "uploaded",
        });
      }
      assert.throws(() => publishRelease(f.options, f.execute), scenario);
      assert.ok(
        f.state.calls.every(
          (args) => !["upload", "create", "edit"].includes(args[2]),
        ),
      );
    } finally {
      f.cleanup();
    }
  }
});

test("stable tags must preexist and annotated tags resolve to their peeled source commit", () => {
  const f = fixture();
  try {
    assert.throws(
      () => ensureTag("fixture/repo", tag, sha, false, f.execute),
      /stable release tag/,
    );
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

test("dry-run mode is explicit and can only be selected for manual dispatch", () => {
  assert.equal(dryRun("workflow_dispatch", "true"), true);
  assert.equal(dryRun("workflow_dispatch", "false"), false);
  assert.equal(dryRun("push", "false"), false);
  for (const [event, value] of [
    ["push", "true"],
    ["pull_request", "false"],
    ["workflow_dispatch", ""],
    ["workflow_dispatch", "TRUE"],
  ])
    assert.throws(() => dryRun(event, value));
});

test("publication CLIs refuse dry runs before reading assets or mutating GitHub", () => {
  for (const [script, command, ref] of [
    ["release.mts", "publish", "refs/heads/release"],
    ["release.mts", "ensure-tag", "refs/heads/release"],
    ["webkit-release.mts", "publish", "refs/heads/main"],
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
