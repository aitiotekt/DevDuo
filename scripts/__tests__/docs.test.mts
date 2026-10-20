import assert from "node:assert/strict";
import {
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readlinkSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import {
  assertSymlinkSupport,
  checkLinks,
  checkParity,
  manifest,
  siteHref,
  syncLinks,
} from "../lib/docs.mts";
import { repoRoot } from "../lib/paths.mts";

test("documentation requires actual relative file and directory links; regular files are preserved", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo docs links "));
  try {
    assertSymlinkSupport(root);
    writeFileSync(join(root, "README.md"), "Canonical source");
    mkdirSync(join(root, "source"));
    const entries = [
      { link: "view/readme.md", target: "README.md", type: "file" as const },
      { link: "view/docs", target: "source", type: "dir" as const },
    ];
    syncLinks(entries, root);
    checkLinks(entries, root);
    assert.equal(
      readlinkSync(join(root, "view/readme.md")).replaceAll("\\", "/"),
      "../README.md",
    );
    assert.equal(lstatSync(join(root, "view/docs")).isSymbolicLink(), true);
    writeFileSync(join(root, "view/user-file.md"), "Keep this draft");
    assert.throws(
      () =>
        syncLinks(
          [{ link: "view/user-file.md", target: "README.md", type: "file" }],
          root,
        ),
      /Refusing to overwrite/,
    );
    assert.equal(
      readFileSync(join(root, "view/user-file.md"), "utf8"),
      "Keep this draft",
    );
    assert.throws(
      () =>
        checkLinks(
          [{ link: "view/user-file.md", target: "README.md", type: "file" }],
          root,
        ),
      /symlink is missing/,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("missing Chinese translation is rejected instead of silently building English only", () => {
  const root = mkdtempSync(join(tmpdir(), "devduo docs parity "));
  try {
    for (const language of ["en", "zh"]) {
      mkdirSync(join(root, "docs", language), { recursive: true });
      for (const name of [
        ...manifest.conventions,
        ...manifest.documents.map((doc) => doc.name),
      ])
        writeFileSync(join(root, "docs", language, name), "Source");
    }
    for (const name of manifest.conventions)
      writeFileSync(join(root, name), "Canonical English");
    checkParity(root);
    rmSync(join(root, "docs/zh", manifest.documents[0].name));
    assert.throws(() => checkParity(root), /filenames differ/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("website links resolve canonical sources and retain locale and fragments", () => {
  assert.equal(
    siteHref(
      "docs/en/009-WINDOWS-HANDOFF.md#setup",
      join(repoRoot, "README.md"),
    ),
    "/docs/009-WINDOWS-HANDOFF#setup",
  );
  assert.equal(
    siteHref(
      "CONTRIBUTING.md",
      join(repoRoot, "docs/zh/009-WINDOWS-HANDOFF.md"),
    ),
    "/zh/contributing",
  );
  assert.equal(
    siteHref("../../PRIVACY.md", join(repoRoot, "docs/en/001-ARCHITECTURE.md")),
    "/privacy",
  );
  assert.equal(
    siteHref("https://example.com/path", join(repoRoot, "README.md")),
    "https://example.com/path",
  );
  assert.equal(siteHref("#local", join(repoRoot, "README.md")), "#local");
});
