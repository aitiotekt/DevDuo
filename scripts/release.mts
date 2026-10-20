import { appendFileSync } from "node:fs";
import {
  assemble,
  verifyArtifacts,
  verifyPublished,
} from "./lib/release-artifacts.mts";
import {
  checkReleaseConfiguration,
  currentVersion,
  releasePlan,
} from "./lib/release-policy.mts";
import { run } from "./lib/release-process.mts";
import {
  bumpVersion,
  checkVersion,
  setVersion,
  syncVersion,
} from "./lib/version.mts";

try {
  const command = process.argv[2];
  if (command === "set-version")
    console.log(`Version set to ${setVersion(process.argv[3] ?? "")}.`);
  else if (command === "bump-version")
    console.log(`Version bumped to ${bumpVersion(process.argv[3] ?? "")}.`);
  else if (command === "sync-version")
    console.log(`Version synchronized to ${syncVersion()}.`);
  else if (command === "check-version")
    console.log(`Version ${checkVersion()} matches devduo-metadata.toml.`);
  else if (command === "check")
    console.log(
      `Release configuration for ${checkReleaseConfiguration()} is consistent.`,
    );
  else {
    const plan = releasePlan(
      currentVersion(),
      process.env.GITHUB_REF ?? "",
      process.env.GITHUB_SHA ?? "",
    );
    if (command === "plan") {
      if (process.env.GITHUB_OUTPUT)
        for (const [key, value] of Object.entries(plan))
          appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
      console.log(JSON.stringify(plan, null, 2));
    } else if (command === "assemble")
      assemble("temp/release/downloads", "temp/release/final", plan);
    else if (command === "verify")
      verifyArtifacts(process.argv[3] ?? "temp/release/final", plan);
    else if (command === "verify-published")
      verifyPublished("temp/release/final", "temp/release/existing", plan);
    else if (command === "ensure-tag") {
      const ref = `refs/tags/${plan.tag}`;
      const remote = run(
        "git",
        ["ls-remote", "origin", ref, `${ref}^{}`],
        true,
      );
      if (remote) {
        const lines = remote.split("\n");
        const resolved = lines.find((line) => line.endsWith("^{}")) ?? lines[0];
        if (resolved.split(/\s+/)[0] !== plan.sourceSha)
          throw new Error("Existing release tag belongs to another commit.");
      } else {
        if (plan.channel === "stable")
          throw new Error("Stable tag no longer exists.");
        run("git", ["push", "origin", `${plan.sourceSha}:${ref}`]);
      }
    } else
      throw new Error(
        "Use check, check-version, set-version, bump-version, sync-version, plan, assemble, verify, verify-published or ensure-tag.",
      );
  }
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "Release command failed.",
  );
  process.exitCode = 1;
}
