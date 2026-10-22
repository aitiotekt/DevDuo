import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
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
import test from "node:test";
import {
  macosBundlePlist,
  relocateMacOSResources,
  signMacOSBundle,
} from "../lib/macos-bundle.mts";
import { loadMetadata } from "../lib/metadata.mts";
import { repoRoot } from "../lib/paths.mts";

function fixture() {
  const directory = mkdtempSync(join(tmpdir(), "devduo-macos-bundle-"));
  const app = join(directory, "Fixture.app"),
    payload = join(app, "Contents/MacOS");
  const resources = {
    "WebAssets/Pages/color-scroll.html": "<p>fixture page</p>",
    "config/safari-scenes.json": "[]",
    "debugging/front_end/devtools_app.html": "<p>fixture inspector</p>",
  };
  for (const [name, contents] of Object.entries(resources)) {
    mkdirSync(join(payload, name, ".."), { recursive: true });
    writeFileSync(join(payload, name), contents);
  }
  writeFileSync(
    join(app, "Contents/Info.plist"),
    macosBundlePlist(loadMetadata(), "Fixture"),
  );
  writeFileSync(join(payload, "Fixture.dll"), "managed assembly");
  return { directory, app, payload, resources };
}

test("macOS data resides in Resources while BaseDirectory aliases remain readable on repeated bundling", () => {
  const f = fixture();
  try {
    relocateMacOSResources(f.app);
    relocateMacOSResources(f.app);
    for (const name of ["WebAssets", "config", "debugging"]) {
      assert.ok(lstatSync(join(f.payload, name)).isSymbolicLink());
      assert.equal(
        readlinkSync(join(f.payload, name)).replaceAll("\\", "/"),
        `../Resources/${name}`,
      );
    }
    for (const [name, contents] of Object.entries(f.resources)) {
      assert.equal(readFileSync(join(f.payload, name), "utf8"), contents);
      assert.equal(
        readFileSync(join(f.app, "Contents/Resources", name), "utf8"),
        contents,
      );
    }
    assert.equal(
      readFileSync(join(f.payload, "Fixture.dll"), "utf8"),
      "managed assembly",
    );
    assert.ok(lstatSync(join(f.payload, "Fixture.dll")).isSymbolicLink());
    assert.equal(
      readFileSync(join(f.app, "Contents/Resources/Fixture.dll"), "utf8"),
      "managed assembly",
    );
  } finally {
    rmSync(f.directory, { recursive: true, force: true });
  }
});

test("native macOS signing seals relocated HTML/config/DevTools resources and preserves links in ZIPs", {
  skip: process.platform !== "darwin",
}, () => {
  const f = fixture();
  try {
    copyFileSync("/usr/bin/true", join(f.payload, "Fixture"));
    const command = (name: string, args: string[]) => {
      const result = spawnSync(name, args, { encoding: "utf8" });
      assert.ifError(result.error);
      assert.equal(result.status, 0, result.stderr);
      return result.stdout;
    };
    const library = join(f.payload, "libFixture.dylib");
    const compiled = spawnSync(
      "xcrun",
      ["clang", "-dynamiclib", "-x", "c", "-o", library, "-"],
      {
        input: "int fixture(void) { return 42; }",
        encoding: "utf8",
      },
    );
    assert.equal(compiled.status, 0, compiled.stderr);
    command("codesign", ["--remove-signature", library]);
    const before = spawnSync("codesign", ["--force", "--sign", "-", f.app], {
      encoding: "utf8",
    });
    assert.notEqual(before.status, 0);
    relocateMacOSResources(f.app);
    signMacOSBundle(
      f.app,
      "Fixture",
      "-",
      join(repoRoot, "config/macos-release.entitlements.plist"),
      (name, args) => command(name, args),
    );
    command("codesign", ["--verify", "--deep", "--strict", f.app]);
    const archive = join(f.directory, "signed.zip"),
      unpacked = join(f.directory, "unpacked");
    command("ditto", ["-c", "-k", "--keepParent", f.app, archive]);
    command("ditto", ["-x", "-k", archive, unpacked]);
    const extracted = join(unpacked, "Fixture.app");
    command("codesign", ["--verify", "--deep", "--strict", extracted]);
    assert.ok(
      existsSync(
        join(extracted, "Contents/MacOS/debugging/front_end/devtools_app.html"),
      ),
    );
  } finally {
    rmSync(f.directory, { recursive: true, force: true });
  }
});
