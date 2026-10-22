import { appendFileSync, readdirSync } from "node:fs";
import {
  assemble,
  verifyArtifacts,
  verifyPublished,
} from "./lib/release-artifacts.mts";
import {
  dryRun,
  ensureTag,
  publishRelease,
  repositoryName,
} from "./lib/release-github.mts";
import {
  checkReleaseConfiguration,
  checksMatrix,
  currentVersion,
  releasePlan,
} from "./lib/release-policy.mts";
import { preflightApplicationRelease } from "./lib/release-preflight.mts";
import {
  bumpVersion,
  checkVersion,
  setVersion,
  syncVersion,
} from "./lib/version.mts";
import { checkWebKitConfiguration } from "./lib/webkit-release.mts";

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
  else if (command === "check") {
    checkWebKitConfiguration();
    console.log(
      `Release configuration for ${checkReleaseConfiguration()} is consistent.`,
    );
  } else if (command === "checks-matrix") {
    const matrix = checksMatrix();
    if (process.env.GITHUB_OUTPUT)
      appendFileSync(
        process.env.GITHUB_OUTPUT,
        `matrix=${JSON.stringify(matrix)}\n`,
      );
    console.log(JSON.stringify(matrix, null, 2));
  } else {
    const plan = releasePlan(
      currentVersion(),
      process.env.GITHUB_REF ?? "",
      process.env.GITHUB_SHA ?? "",
    );
    if (command === "plan") {
      checkReleaseConfiguration();
      checkWebKitConfiguration();
      const mode = dryRun(
        process.env.GITHUB_EVENT_NAME ?? "",
        process.env.RELEASE_DRY_RUN ?? "",
      );
      preflightApplicationRelease(plan);
      if (process.env.GITHUB_OUTPUT)
        for (const [key, value] of Object.entries({ ...plan, dry_run: mode }))
          appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
      console.log(JSON.stringify({ ...plan, dry_run: mode }, null, 2));
    } else if (command === "assemble")
      assemble("temp/release/downloads", "temp/release/final", plan);
    else if (command === "verify")
      verifyArtifacts(process.argv[3] ?? "temp/release/final", plan);
    else if (command === "verify-published")
      verifyPublished("temp/release/final", "temp/release/existing", plan);
    else if (command === "ensure-tag") {
      if (
        !plan.publish ||
        dryRun(
          process.env.GITHUB_EVENT_NAME ?? "",
          process.env.RELEASE_DRY_RUN ?? "",
        )
      )
        throw new Error(
          "Development and dry-run modes cannot create release tags.",
        );
      ensureTag(
        repositoryName(),
        plan.tag,
        plan.sourceSha,
        plan.channel !== "stable",
      );
    } else if (command === "publish") {
      if (
        !plan.publish ||
        dryRun(
          process.env.GITHUB_EVENT_NAME ?? "",
          process.env.RELEASE_DRY_RUN ?? "",
        )
      )
        throw new Error(
          "Development and dry-run modes cannot publish a release.",
        );
      const directory = "temp/release/final";
      verifyArtifacts(directory, plan);
      const outcome = publishRelease({
        repository: repositoryName(),
        tag: plan.tag,
        sha: plan.sourceSha,
        prerelease: plan.prerelease,
        mayCreateTag: plan.channel !== "stable",
        latest: !plan.prerelease,
        title: `${plan.displayName} ${plan.tag}`,
        notes: ".github/release-notes.md",
        directory,
        files: readdirSync(directory),
      });
      if (process.env.GITHUB_STEP_SUMMARY)
        appendFileSync(
          process.env.GITHUB_STEP_SUMMARY,
          `${plan.tag}: ${outcome}; source ${plan.sourceSha}.\n`,
        );
    } else
      throw new Error(
        "Use check, checks-matrix, check-version, set-version, bump-version, sync-version, plan, assemble, verify, verify-published, ensure-tag or publish.",
      );
  }
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "Release command failed.",
  );
  process.exitCode = 1;
}
