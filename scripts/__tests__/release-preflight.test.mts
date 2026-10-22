import assert from "node:assert/strict";
import test from "node:test";
import { releasePlan } from "../lib/release-policy.mts";
import { preflightApplicationRelease } from "../lib/release-preflight.mts";

const sha = "a".repeat(40);

test("development plans skip the runtime preflight", () => {
  const development = releasePlan("0.1.0-dev", "refs/heads/release", sha);
  preflightApplicationRelease(development, () => {
    assert.fail("Development versions do not publish a release");
  });
});

test("publishing plans require the published runtime before building", () => {
  const plan = releasePlan("0.1.0-alpha.1", "refs/heads/release", sha);
  let runtimeChecks = 0;
  preflightApplicationRelease(plan, () => {
    runtimeChecks++;
  });
  assert.equal(runtimeChecks, 1);
  assert.throws(
    () =>
      preflightApplicationRelease(plan, () => {
        throw new Error("Runtime is not published");
      }),
    /Runtime is not published/,
  );
});
