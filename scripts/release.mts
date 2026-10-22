import { appendFileSync, readdirSync } from "node:fs";
import { assemble } from "./lib/release-artifacts.mts";
import {
  dryRun,
  ensureTag,
  publishRelease,
  repositoryName,
} from "./lib/release-github.mts";
import {
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

try {
  const command = process.argv[2];
  if (command === "set-version")
    console.log(`Version set to ${setVersion(process.argv[3] ?? "")}.`);
  else if (command === "bump-version")
    console.log(`Version bumped to ${bumpVersion(process.argv[3] ?? "")}.`);
  else if (command === "sync-version")
    console.log(`Version synchronized to ${syncVersion()}.`);
  else if (command === "check-version" || command === "check")
    console.log(`Version ${checkVersion()} matches devduo-metadata.toml.`);
  else if (command === "checks-matrix") {
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
    const mode = dryRun(process.env.RELEASE_DRY_RUN ?? "");
    if (command === "plan") {
      preflightApplicationRelease(plan);
      if (process.env.GITHUB_OUTPUT)
        for (const [key, value] of Object.entries({ ...plan, dry_run: mode }))
          appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`);
      console.log(JSON.stringify({ ...plan, dry_run: mode }, null, 2));
    } else if (command === "assemble")
      assemble("temp/release/downloads", "temp/release/final", plan);
    else if (command === "ensure-tag" || command === "publish") {
      if (!plan.publish || mode)
        throw new Error(
          "Development and dry-run modes cannot create tags or publish a release.",
        );
      if (command === "ensure-tag")
        ensureTag(repositoryName(), plan.tag, plan.sourceSha);
      else {
        const directory = "temp/release/final";
        const outcome = publishRelease({
          repository: repositoryName(),
          tag: plan.tag,
          sha: plan.sourceSha,
          prerelease: plan.prerelease,
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
      }
    } else
      throw new Error(
        "Use check, checks-matrix, check-version, set-version, bump-version, sync-version, plan, assemble, ensure-tag or publish.",
      );
  }
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "Release command failed.",
  );
  process.exitCode = 1;
}
