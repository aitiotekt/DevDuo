import { appendFileSync, readFileSync } from "node:fs";
import { planChecks } from "./lib/ci-plan.mts";
import { repoRoot } from "./lib/paths.mts";

const plan = planChecks(
  process.env.GITHUB_EVENT_NAME ?? "",
  JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH ?? "", "utf8")),
  process.env.GITHUB_SHA ?? "",
  process.env.CI_FORCE_NATIVE === "true",
  repoRoot,
);
if (process.env.GITHUB_OUTPUT)
  appendFileSync(process.env.GITHUB_OUTPUT, `native=${plan.native}\n`);
if (process.env.GITHUB_STEP_SUMMARY)
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${plan.reason}\n`);
console.log(JSON.stringify(plan, null, 2));
