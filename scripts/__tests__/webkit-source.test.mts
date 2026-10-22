import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  existsSync,
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
import { repoRoot } from "../lib/paths.mts";

test("pinned source resumes interrupted checkout without losing files or trusting other caches", {
  skip: process.platform !== "win32",
}, () => {
  const root = mkdtempSync(join(tmpdir(), "devduo-source-tests-"));
  try {
    const upstream = join(root, "upstream");
    mkdirSync(join(upstream, "Source"), { recursive: true });
    const git = (directory: string, args: string[]) => {
      const result = spawnSync("git", ["-C", directory, ...args], {
        encoding: "utf8",
      });
      assert.equal(result.status, 0, result.stderr);
      return result.stdout.trim();
    };
    git(upstream, ["init", "--quiet"]);
    git(upstream, ["config", "core.autocrlf", "false"]);
    git(upstream, ["config", "core.longpaths", "true"]);
    writeFileSync(join(upstream, ".gitattributes"), "*.txt text eol=lf\n");
    writeFileSync(join(upstream, "Source/pinned.txt"), "pinned bytes\n");
    const longPath = join(
      "Source",
      ...Array<string>(3).fill("a".repeat(70)),
      "long.txt",
    );
    mkdirSync(join(upstream, longPath, ".."), { recursive: true });
    writeFileSync(join(upstream, longPath), "long-path bytes\n");
    git(upstream, ["add", "."]);
    git(upstream, [
      "-c",
      "user.name=Fixture",
      "-c",
      "user.email=fixture@example.invalid",
      "commit",
      "--quiet",
      "-m",
      "fixture",
    ]);
    const revision = git(upstream, ["rev-parse", "HEAD"]);
    const initialize = (directory: string, pin = revision, url = upstream) =>
      spawnSync(
        "powershell.exe",
        [
          "-NoProfile",
          "-NonInteractive",
          "-Command",
          "$ErrorActionPreference='Stop'; . $env:HELPER; Initialize-PinnedSource $env:SOURCE $env:ORIGIN $env:REVISION @('Source')",
        ],
        {
          encoding: "utf8",
          env: {
            ...process.env,
            HELPER: join(repoRoot, "native/webkit/windows/PersistentBuild.ps1"),
            SOURCE: directory,
            ORIGIN: url,
            REVISION: pin,
          },
        },
      );
    const succeed = (directory: string) => {
      const result = initialize(directory);
      assert.equal(result.status, 0, result.stderr);
      assert.equal(git(directory, ["rev-parse", "HEAD"]), revision);
      assert.equal(
        readFileSync(join(directory, "Source/pinned.txt"), "utf8"),
        "pinned bytes\n",
      );
    };
    const reject = (
      directory: string,
      message: RegExp,
      pin = revision,
      url = upstream,
    ) => {
      const result = initialize(directory, pin, url);
      assert.notEqual(result.status, 0);
      assert.match(result.stdout + result.stderr, message);
    };
    const unborn = (name: string) => {
      const directory = join(root, name);
      mkdirSync(directory);
      git(directory, ["init", "--quiet"]);
      git(directory, ["remote", "add", "origin", upstream]);
      git(directory, ["fetch", "--depth", "1", "origin", revision]);
      return directory;
    };

    const cold = join(root, "cold");
    succeed(cold);
    assert.equal(
      readFileSync(join(cold, longPath), "utf8"),
      "long-path bytes\n",
    );
    assert.equal(git(cold, ["config", "core.longpaths"]), "true");
    assert.equal(git(cold, ["config", "core.symlinks"]), "true");
    writeFileSync(join(cold, "Source/pinned.txt"), "contributor edit\n");
    git(cold, ["config", "core.longpaths", "false"]);
    git(cold, ["config", "core.symlinks", "false"]);
    assert.equal(initialize(cold).status, 0);
    assert.equal(git(cold, ["config", "core.longpaths"]), "true");
    assert.equal(git(cold, ["config", "core.symlinks"]), "true");
    assert.equal(
      readFileSync(join(cold, "Source/pinned.txt"), "utf8"),
      "contributor edit\n",
    );
    reject(cold, /different revision/, "b".repeat(40));
    reject(cold, /different origin/, revision, join(root, "other"));
    assert.equal(
      readFileSync(join(cold, "Source/pinned.txt"), "utf8"),
      "contributor edit\n",
    );

    // A fetch failure leaves a retryable unborn repository, not a valid cache.
    const failed = join(root, "failed-fetch");
    reject(failed, /Pinned source operation failed/, "b".repeat(40));
    succeed(failed);
    succeed(unborn("fetched-only"));

    const partial = unborn("partial");
    mkdirSync(join(partial, "Source"));
    writeFileSync(join(partial, "Source/pinned.txt"), "unknown partial edit\n");
    writeFileSync(join(partial, "notes.txt"), "keep this too\n");
    const objectFiles = git(partial, ["count-objects", "-v"]);
    succeed(partial);
    const archives = readdirSync(root).filter((name) =>
      name.startsWith("partial.incomplete-"),
    );
    assert.equal(archives.length, 1);
    assert.equal(
      readFileSync(join(root, archives[0], "Source/pinned.txt"), "utf8"),
      "unknown partial edit\n",
    );
    assert.equal(
      readFileSync(join(root, archives[0], "notes.txt"), "utf8"),
      "keep this too\n",
    );
    assert.equal(git(partial, ["count-objects", "-v"]), objectFiles);
    assert.equal(initialize(partial).status, 0);
    assert.equal(
      readdirSync(root).filter((name) => name.startsWith("partial.incomplete-"))
        .length,
      1,
    );

    const indexed = unborn("indexed");
    git(indexed, ["read-tree", revision]);
    const index = readFileSync(join(indexed, ".git/index"));
    reject(indexed, /indexed files/);
    assert.deepEqual(readFileSync(join(indexed, ".git/index")), index);
    const locked = unborn("locked");
    writeFileSync(join(locked, ".git/index.lock"), "active lock");
    reject(locked, /cache is locked/);
    assert.equal(
      readFileSync(join(locked, ".git/index.lock"), "utf8"),
      "active lock",
    );

    // Avoid discovering the application's parent Git repository accidentally.
    const unrelated = join(upstream, "unrelated");
    mkdirSync(unrelated);
    writeFileSync(join(unrelated, "notes.txt"), "keep\n");
    reject(unrelated, /not a standalone Git repository/);
    assert.equal(existsSync(join(unrelated, ".git")), false);
    assert.equal(readFileSync(join(unrelated, "notes.txt"), "utf8"), "keep\n");
  } finally {
    rmSync(root, { recursive: true });
  }
});
