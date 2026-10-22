import assert from "node:assert/strict";
import {
  cpSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { macosBundlePlist } from "../lib/macos-bundle.mts";
import { loadMetadata, metadataFile, parseMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";
import {
  artifactName,
  checkReleaseConfiguration,
  releasePlan,
} from "../lib/release-policy.mts";
import { nextVersion } from "../lib/release-version.mts";
import {
  bumpVersion,
  checkVersion,
  setVersion,
  syncVersion,
} from "../lib/version.mts";

const source = readFileSync(join(repoRoot, metadataFile), "utf8");
const managedFiles = [
  metadataFile,
  "package.json",
  "docsite/package.json",
  "src/DevDuo/DevDuo.csproj",
  "src/DevDuo/ProjectLinks.cs",
];
function fixture(): string {
  const root = mkdtempSync(join(tmpdir(), "devduo-version-"));
  for (const file of [...managedFiles, ".github/workflows/release.yml"]) {
    mkdirSync(dirname(join(root, file)), { recursive: true });
    cpSync(join(repoRoot, file), join(root, file));
  }
  return root;
}

test("project links are generated from metadata and drift is rejected", () => {
  const root = fixture();
  try {
    const path = join(root, metadataFile);
    writeFileSync(
      path,
      source
        .replace(
          /repository_url = "[^"]+"/,
          'repository_url = "https://example.test/source"',
        )
        .replace(
          /docs_url = "[^"]+"/,
          'docs_url = "https://example.test/docs/"',
        ),
    );
    syncVersion(root);
    const links = join(root, "src/DevDuo/ProjectLinks.cs");
    assert.match(
      readFileSync(links, "utf8"),
      /https:\/\/example\.test\/source/,
    );
    assert.match(
      readFileSync(links, "utf8"),
      /https:\/\/example\.test\/docs\//,
    );
    writeFileSync(
      links,
      readFileSync(links, "utf8").replace("/docs/", "/stale/"),
    );
    assert.throws(() => checkVersion(root), /ProjectLinks.cs/);
    syncVersion(root);
    assert.equal(checkVersion(root), loadMetadata(root).project.version);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("metadata rejects missing identities, unsafe refs and incomplete artifact or signing policies", () => {
  const metadata = loadMetadata();
  assert.equal(metadata.project.identifier, "local.DevDuo.Calibration");
  assert.equal(metadata.project.version, checkVersion());
  for (const [before, after] of [
    ["schema_version = 1", "schema_version = 2"],
    ['identifier = "local.DevDuo.Calibration"', 'identifier = ""'],
    ['binary_name = "DevDuo"', 'binary_name = "../DevDuo"'],
    ['branch = "release"', 'branch = "release//invalid"'],
    ['tag_prefix = "v"', 'tag_prefix = "v*"'],
    ['macos = "signed"', 'macos = "unsigned"'],
    ['macos = "signed"', 'macos = "signed"\nwindows = "signed"'],
    ['platform = "windows"', 'platform = "macos"'],
    ['target = "win-x64"', 'target = "osx-arm64"'],
    ['extension = "zip"', 'extension = "exe"'],
    [
      'repository_url = "https://github.com',
      'repository_url = "http://github.com',
    ],
  ])
    assert.throws(() => parseMetadata(source.replace(before, after)));
  assert.throws(() =>
    parseMetadata(source.slice(0, source.lastIndexOf("[[release.artifact]]"))),
  );
  assert.throws(() => parseMetadata(`${source}\ninvalid = [\n`));
});

test("release names, refs and macOS identity come from metadata", () => {
  const metadata = parseMetadata(
    source
      .replace('binary_name = "DevDuo"', 'binary_name = "FixtureApp"')
      .replace('display_name = "DevDuo"', 'display_name = "Fixture & App"')
      .replace('branch = "release"', 'branch = "shipping"')
      .replace('tag_prefix = "v"', 'tag_prefix = "app-v"'),
  );
  assert.equal(
    artifactName("1.2.3-beta.1", "win-x64", metadata),
    "FixtureApp-app-v1.2.3-beta.1-win-x64.zip",
  );
  assert.equal(
    releasePlan("1.2.3-beta.1", "refs/heads/shipping", "a".repeat(40), metadata)
      .tag,
    "app-v1.2.3-beta.1",
  );
  assert.equal(
    releasePlan("1.2.3", "refs/tags/app-v1.2.3", "a".repeat(40), metadata)
      .displayName,
    "Fixture & App",
  );
  assert.throws(() =>
    releasePlan("1.2.3-beta.1", "refs/heads/release", "a".repeat(40), metadata),
  );
  const plist = macosBundlePlist(metadata, metadata.project.binaryName);
  assert.match(plist, /<string>local.DevDuo.Calibration<\/string>/);
  assert.match(plist, /Fixture &amp; App/);
  assert.match(
    plist,
    /<key>CFBundleExecutable<\/key><string>FixtureApp<\/string>/,
  );
  assert.match(
    plist,
    /<key>LSMinimumSystemVersion<\/key><string>27.0<\/string>/,
  );
});

for (const [lineEnding, eol] of [
  ["LF", "\n"],
  ["CRLF", "\r\n"],
]) {
  test(`set and sync update all version consumers and preserve metadata comments and ${lineEnding}`, () => {
    const root = fixture();
    try {
      const path = join(root, metadataFile);
      writeFileSync(
        path,
        source
          .replaceAll("\r\n", "\n")
          .replace(
            /^version\s*=.*$/m,
            "version = '0.1.0-dev' # Keep this comment",
          )
          .replaceAll("\n", eol),
      );
      assert.ok(
        readFileSync(path, "utf8").includes(
          `version = '0.1.0-dev' # Keep this comment${eol}`,
        ),
        "The fixture must contain a comment before testing its preservation.",
      );
      setVersion("1.2.3-beta.4", root);
      assert.equal(checkVersion(root), "1.2.3-beta.4");
      assert.ok(
        readFileSync(path, "utf8").includes(
          `version = "1.2.3-beta.4" # Keep this comment${eol}`,
        ),
      );
      writeFileSync(
        path,
        readFileSync(path, "utf8").replace('"1.2.3-beta.4"', '"1.2.3-beta.5"'),
      );
      assert.throws(() => checkVersion(root), /package.json.*DevDuo.csproj/);
      assert.equal(syncVersion(root), "1.2.3-beta.5");
      assert.equal(checkVersion(root), "1.2.3-beta.5");
      assert.equal(bumpVersion("prerelease", root), "1.2.3-beta.6");
      assert.equal(checkVersion(root), "1.2.3-beta.6");
      const before = managedFiles.map((file) =>
        readFileSync(join(root, file), "utf8"),
      );
      syncVersion(root);
      assert.deepEqual(
        managedFiles.map((file) => readFileSync(join(root, file), "utf8")),
        before,
      );
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test("malformed consumers and invalid bumps fail before changing any version file", () => {
  const root = fixture();
  try {
    const native = join(root, "src/DevDuo/DevDuo.csproj");
    writeFileSync(
      native,
      readFileSync(native, "utf8").replace(/<Version>[^<]+<\/Version>/, ""),
    );
    const before = managedFiles.map((file) =>
      readFileSync(join(root, file), "utf8"),
    );
    assert.throws(() => setVersion("1.2.3", root), /exactly one/);
    assert.throws(() => setVersion("invalid", root));
    assert.deepEqual(
      managedFiles.map((file) => readFileSync(join(root, file), "utf8")),
      before,
    );
    cpSync(join(repoRoot, "src/DevDuo/DevDuo.csproj"), native);
    writeFileSync(join(root, "docsite/package.json"), "{");
    const malformed = managedFiles.map((file) =>
      readFileSync(join(root, file), "utf8"),
    );
    assert.throws(() => setVersion("1.2.3", root));
    assert.deepEqual(
      managedFiles.map((file) => readFileSync(join(root, file), "utf8")),
      malformed,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("version increments follow SemVer and reject prerelease regressions", () => {
  for (const [current, level, expected] of [
    ["1.2.3", "major", "2.0.0"],
    ["1.2.3", "minor", "1.3.0"],
    ["1.2.3", "patch", "1.2.4"],
    ["0.1.0-dev", "alpha", "0.1.0-alpha.0"],
    ["1.2.3-alpha.2", "alpha", "1.2.3-alpha.3"],
    ["1.2.3-alpha.2", "beta", "1.2.3-beta.0"],
    ["1.2.3-beta.2", "prerelease", "1.2.3-beta.3"],
    ["1.2.3-beta.2", "stable", "1.2.3"],
  ])
    assert.equal(nextVersion(current, level), expected);
  assert.throws(() => nextVersion("1.2.3-beta.2", "alpha"), /greater/);
  assert.throws(() => nextVersion("1.2.3", "stable"), /greater/);
  assert.throws(
    () => nextVersion("0.1.0-dev", "prerelease"),
    /Choose alpha or beta/,
  );
  assert.throws(() => nextVersion("1.2.3", "arbitrary"), /Use major/);
});

test("release check rejects workflow filters that drift from metadata", () => {
  const root = fixture();
  try {
    assert.equal(
      checkReleaseConfiguration(root),
      loadMetadata(root).project.version,
    );
    const path = join(root, metadataFile);
    writeFileSync(
      path,
      source.replace('branch = "release"', 'branch = "shipping"'),
    );
    assert.throws(() => checkReleaseConfiguration(root), /push filters/);
    writeFileSync(
      path,
      source.replace('tag_prefix = "v"', 'tag_prefix = "app-v"'),
    );
    assert.throws(() => checkReleaseConfiguration(root), /push filters/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
