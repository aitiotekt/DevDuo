import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { copyMacOSIcons } from "./lib/app-icons.mts";
import { macosBundlePlist } from "./lib/macos-bundle.mts";
import { loadMetadata } from "./lib/metadata.mts";
import { repoRoot } from "./lib/paths.mts";
import {
  artifactName,
  currentVersion,
  type Target,
  targets,
} from "./lib/release-policy.mts";
import { run } from "./lib/release-process.mts";

try {
  const command = process.argv[2],
    target = process.argv[3] as Target;
  if (!targets.includes(target)) throw new Error("Use osx-arm64 or win-x64.");
  if (
    (target === "osx-arm64" &&
      (process.platform !== "darwin" || process.arch !== "arm64")) ||
    (target === "win-x64" &&
      (process.platform !== "win32" || process.arch !== "x64"))
  )
    throw new Error("Release packaging must run on its native platform.");
  const metadata = loadMetadata(),
    name = metadata.project.binaryName;
  const version = currentVersion(),
    stage = join(repoRoot, "temp/build/release", target);
  const app = join(stage, target === "osx-arm64" ? `${name}.app` : name);
  const output = join(repoRoot, "temp/release/platform");
  const archive = join(output, artifactName(version, target));
  if (command === "preflight" && target === "osx-arm64") {
    for (const key of [
      "APPLE_CERTIFICATE",
      "APPLE_CERTIFICATE_PASSWORD",
      "APPLE_SIGNING_IDENTITY",
      "APPLE_ID",
      "APPLE_PASSWORD",
      "APPLE_TEAM_ID",
    ])
      if (!process.env[key]?.trim())
        throw new Error(`Missing ${key} in the release-signing environment.`);
    if (
      !process.env.APPLE_SIGNING_IDENTITY?.startsWith(
        "Developer ID Application:",
      )
    )
      throw new Error("A Developer ID Application identity is required.");
    const sdk = run("xcrun", ["--sdk", "macosx", "--show-sdk-version"], true);
    if (
      !/^\d+\.\d+(?:\.\d+)?$/.test(sdk) ||
      Number(sdk.split(".")[0]) <
        Number(metadata.macos.minimumSystemVersion.split(".")[0])
    )
      throw new Error("The runner does not provide the required macOS SDK.");
    console.log(
      "Signing inputs and the macOS SDK are available; credentials have not been imported or used.",
    );
  } else if (command === "prepare") {
    rmSync(stage, { recursive: true, force: true });
    rmSync(output, { recursive: true, force: true });
    mkdirSync(stage, { recursive: true });
  } else if (command === "bundle") {
    const payload = target === "osx-arm64" ? join(app, "Contents/MacOS") : app;
    const executable = join(
      payload,
      target === "osx-arm64" ? name : `${name}.exe`,
    );
    if (
      !existsSync(executable) ||
      !existsSync(
        join(
          payload,
          target === "osx-arm64"
            ? "libDevDuoWebView.dylib"
            : "DevDuo.Windows.Compositor.dll",
        ),
      ) ||
      !existsSync(
        join(
          payload,
          target === "osx-arm64" ? "libcoreclr.dylib" : "coreclr.dll",
        ),
      )
    )
      throw new Error(
        "Expected a self-contained publish with the .NET runtime.",
      );
    const notices =
      target === "osx-arm64" ? join(app, "Contents/Resources") : app;
    mkdirSync(notices, { recursive: true });
    for (const file of ["LICENSE", "THIRD_PARTY_NOTICES.md", "PRIVACY.md"])
      copyFileSync(join(repoRoot, file), join(notices, file));
    if (target === "osx-arm64") {
      copyMacOSIcons(notices);
      writeFileSync(
        join(app, "Contents/Info.plist"),
        macosBundlePlist(metadata, name),
      );
    }
  } else if (command === "sign" && target === "osx-arm64") {
    const required = (name: string): string => {
      const value = process.env[name];
      if (!value) throw new Error(`Missing ${name}.`);
      return value;
    };
    const identity = required("APPLE_SIGNING_IDENTITY");
    const appleId = required("APPLE_ID");
    const password = required("APPLE_PASSWORD");
    const teamId = required("APPLE_TEAM_ID");
    if (!identity.startsWith("Developer ID Application:"))
      throw new Error("A Developer ID Application identity is required.");
    const entitlements = join(
      repoRoot,
      "config/macos-release.entitlements.plist",
    );
    // Re-sign all bundled native code with one identity; do not rely on codesign --deep for signing.
    const signFiles = (directory: string): void => {
      for (const entry of readdirSync(directory, { withFileTypes: true })) {
        const path = join(directory, entry.name);
        if (entry.isDirectory()) signFiles(path);
        else if (entry.isFile()) {
          const magic = readFileSync(path).subarray(0, 4).toString("hex");
          if (
            [
              "cffaedfe",
              "cefaedfe",
              "feedfacf",
              "feedface",
              "cafebabe",
              "bebafeca",
            ].includes(magic)
          )
            run("codesign", [
              "--force",
              "--options",
              "runtime",
              "--timestamp",
              "--entitlements",
              entitlements,
              "--sign",
              identity,
              path,
            ]);
        }
      }
    };
    signFiles(app);
    run("codesign", [
      "--force",
      "--options",
      "runtime",
      "--timestamp",
      "--entitlements",
      entitlements,
      "--sign",
      identity,
      app,
    ]);
    run("codesign", ["--verify", "--deep", "--strict", app]);
    const submission = join(stage, "notarization.zip");
    run("ditto", ["-c", "-k", "--keepParent", app, submission]);
    const result = JSON.parse(
      run(
        "xcrun",
        [
          "notarytool",
          "submit",
          submission,
          "--apple-id",
          appleId,
          "--password",
          password,
          "--team-id",
          teamId,
          "--wait",
          "--output-format",
          "json",
        ],
        true,
      ),
    );
    if (result.status !== "Accepted")
      throw new Error("Apple notarization was not accepted.");
    run("xcrun", ["stapler", "staple", app]);
    rmSync(submission);
  } else if (command === "archive") {
    mkdirSync(output, { recursive: true });
    rmSync(archive, { force: true });
    if (target === "osx-arm64") {
      run("codesign", ["--verify", "--deep", "--strict", app]);
      run("xcrun", ["stapler", "validate", app]);
      run("spctl", ["--assess", "--type", "execute", "--verbose=2", app]);
      run("ditto", ["-c", "-k", "--keepParent", app, archive]);
    } else run("tar", ["-a", "-c", "-f", archive, "-C", stage, name]);
  } else throw new Error("Use preflight, prepare, bundle, sign or archive.");
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "Release packaging failed.",
  );
  process.exitCode = 1;
}
