import { type Runner, repositoryName, tagCommit } from "./release-github.mts";
import type { ReleasePlan } from "./release-policy.mts";
import { run } from "./release-process.mts";
import { preflightPublishedWebKit } from "./webkit-release.mts";

export function preflightApplicationRelease(
  plan: ReleasePlan,
  execute: Runner = run,
  validateRuntime: () => unknown = preflightPublishedWebKit,
): void {
  if (!/^[a-f0-9]{40}$/.test(plan.sourceSha))
    throw new Error("Application preflight requires a full source commit SHA.");
  repositoryName();
  if (!plan.publish) return;
  const existing = tagCommit(plan.tag, execute);
  if (existing && existing !== plan.sourceSha)
    throw new Error(
      "This application version is already tagged at another commit. Bump the version, or retry the original source and release-final artifact; published assets cannot be replaced.",
    );
  // Check this cheap source gate before runtime downloads, compilation or signing.
  validateRuntime();
}
