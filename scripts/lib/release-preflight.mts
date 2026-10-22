import type { ReleasePlan } from "./release-policy.mts";
import { preflightPublishedWebKit } from "./webkit-release.mts";

export function preflightApplicationRelease(
  plan: ReleasePlan,
  requireRuntime: () => unknown = preflightPublishedWebKit,
): void {
  if (!plan.publish) return;
  // The Windows job downloads this runtime; fail before compilation or signing.
  requireRuntime();
}
