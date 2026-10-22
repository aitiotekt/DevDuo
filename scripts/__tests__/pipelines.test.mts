import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";
import { parse } from "yaml";
import { repoRoot } from "../lib/paths.mts";

const workflow = (name: string) =>
  parse(
    readFileSync(join(repoRoot, ".github/workflows", `${name}.yml`), "utf8"),
  );

test("application pushes publish automatically while manual workflows default to dry runs", () => {
  const app = workflow("release"),
    engine = workflow("webkit-release");
  assert.deepEqual(app.on.push, { branches: ["release"], tags: ["v*"] });
  assert.deepEqual(Object.keys(engine.on), ["workflow_dispatch"]);
  for (const pipeline of [app, engine]) {
    assert.deepEqual(
      pipeline.on.workflow_dispatch.inputs["dry-run"].type,
      "boolean",
    );
    assert.equal(pipeline.on.workflow_dispatch.inputs["dry-run"].default, true);
    assert.equal(
      pipeline.jobs.publish.if,
      "needs.plan.outputs.dry_run == 'false'",
    );
    assert.deepEqual(pipeline.permissions, { contents: "read" });
    assert.deepEqual(pipeline.jobs.publish.permissions, { contents: "write" });
    assert.ok(
      pipeline.jobs.publish.steps.some((step: { run?: string }) =>
        step.run?.includes("publish"),
      ),
    );
  }
  assert.match(
    app.jobs.plan.steps.find((step: { id?: string }) => step.id === "plan").env
      .RELEASE_DRY_RUN,
    /github.event_name == 'workflow_dispatch'/,
  );
  assert.ok(
    app.jobs.macos.steps.some((step: { run?: string }) =>
      step.run?.includes("preflight osx-arm64"),
    ),
  );
  assert.ok(
    app.jobs.windows.steps.some((step: { run?: string }) =>
      step.run?.includes("fetch-windows-webkit-runtime"),
    ),
  );
});

test("self-hosted jobs install mise locally and do not use the GitHub Actions cache", () => {
  const action = parse(
    readFileSync(
      join(repoRoot, ".github/actions/setup-workspace/action.yml"),
      "utf8",
    ),
  );
  const install = action.runs.steps.find((step: { uses?: string }) =>
    step.uses?.startsWith("jdx/mise-action@"),
  );
  assert.equal(install.with.cache, "${{ runner.environment != 'self-hosted' }}");
});

test("Windows jobs enable Git long paths in the same account configuration as symlinks", () => {
  for (const name of ["webkit-release", "release", "checks"]) {
    const text = readFileSync(
      join(repoRoot, ".github/workflows", `${name}.yml`),
      "utf8",
    );
    assert.match(text, /git config --global core\.symlinks true/);
    assert.match(text, /git config --global core\.longpaths true/);
    assert.equal(text.includes("git config --system core.longpaths"), false);
  }
});

test("persistent engine jobs retain caches, reject workspace edits before checkout and finish within token lifetime", () => {
  const engine = workflow("webkit-release"),
    build = engine.jobs.build;
  const checkout = build.steps.findIndex((step: { uses?: string }) =>
    step.uses?.startsWith("actions/checkout@"),
  );
  assert.ok(
    build.steps
      .slice(0, checkout)
      .some((step: { run?: string }) =>
        step.run?.includes("git diff --cached --exit-code"),
      ),
  );
  assert.equal(build.steps[checkout].with.clean, false);
  assert.equal(build.steps[checkout].with["persist-credentials"], false);
  assert.ok(build["timeout-minutes"] < 1440);
  const compile = build.steps.find((step: { run?: string }) =>
    step.run?.includes("exec -- just build-windows-webkit-runtime"),
  );
  assert.ok(compile["timeout-minutes"] < build["timeout-minutes"]);
  assert.ok(
    build.steps.every(
      (step: { run?: string }) => !step.run?.includes("just clean"),
    ),
  );
  assert.deepEqual(engine.jobs.report.needs, ["plan", "build", "publish"]);
});

test("all workflows pin external actions and deployment waits for a publication audit", () => {
  for (const name of ["checks", "docs", "release", "webkit-release"]) {
    const pipeline = workflow(name);
    for (const job of Object.values(pipeline.jobs) as {
      steps?: { uses?: string }[];
    }[])
      for (const step of job.steps ?? [])
        if (step.uses && !step.uses.startsWith("./"))
          assert.match(step.uses, /@[a-f0-9]{40}$/);
  }
  const docs = workflow("docs");
  assert.equal(docs.jobs.deploy.needs, "build");
  assert.match(docs.jobs.deploy.if, /github.ref == 'refs\/heads\/main'/);
  assert.ok(
    docs.jobs.build.steps.some((step: { run?: string }) =>
      step.run?.includes("public-audit"),
    ),
  );
  const app = workflow("release");
  assert.equal(app.jobs["release-report"].if, "always()");
  assert.deepEqual(app.jobs["release-report"].needs, [
    "plan",
    "checks",
    "stable-approval",
    "macos",
    "windows",
    "assemble",
    "publish",
  ]);
});

test("untrusted changes stay on hosted read-only runners at the exact triggering commit", () => {
  for (const name of ["checks", "docs", "release", "webkit-release"]) {
    const pipeline = workflow(name);
    assert.equal(pipeline.on.pull_request_target, undefined);
    assert.equal(pipeline.on.workflow_run, undefined);
    for (const job of Object.values(pipeline.jobs) as {
      "timeout-minutes"?: number;
      uses?: string;
      steps?: { uses?: string; with?: Record<string, unknown> }[];
    }[]) {
      if (!job.uses) assert.ok(job["timeout-minutes"]);
      for (const step of job.steps ?? []) {
        if (step.uses?.startsWith("actions/checkout@")) {
          assert.equal(step.with?.ref, `\${{ github.sha }}`);
          assert.equal(step.with?.["persist-credentials"], false);
        }
      }
    }
  }
  const checks = workflow("checks");
  assert.ok(checks.on.pull_request === null);
  assert.deepEqual(checks.permissions, { contents: "read" });
  assert.equal(checks.jobs.portable["runs-on"], "ubuntu-24.04");
  assert.equal(checks.jobs.portable.strategy, undefined);
  assert.equal(checks.jobs.native.if, "needs.plan.outputs.native == 'true'");
  assert.equal(
    checks.jobs.plan.steps.find((step: { uses?: string }) =>
      step.uses?.startsWith("actions/checkout@"),
    ).with["fetch-depth"],
    0,
  );
  assert.equal(workflow("release").jobs.checks.with["force-native"], true);
  const runs = JSON.stringify([checks.jobs.portable, checks.jobs.native]);
  assert.doesNotMatch(
    runs,
    /secrets\.|build-windows-webkit-runtime|test-windows/,
  );
  const appKit = checks.jobs.native.steps.filter((step: { run?: string }) =>
    step.run?.includes("test-macos"),
  );
  assert.equal(appKit.length, 1);
  assert.equal(appKit[0].if, "runner.os == 'macOS'");
  assert.match(appKit[0].run, /just build test-macos$/);
});

test("documentation PRs share portable checks and deployment cancels superseded commits", () => {
  const docs = workflow("docs");
  assert.equal(docs.on.pull_request, undefined);
  assert.equal(docs.concurrency["cancel-in-progress"], true);
  assert.deepEqual(docs.on.push.branches, ["main", "release"]);
  assert.ok(docs.on.push.paths.includes("docs/**"));
  assert.ok(docs.on.push.paths.includes("pnpm-lock.yaml"));
  assert.equal(workflow("checks").on.push.paths, undefined);
});

test("release queues preserve pending versions and schema exceptions are exact and path-scoped", () => {
  const config = parse(
    readFileSync(join(repoRoot, ".github/actionlint.yaml"), "utf8"),
  );
  assert.deepEqual(Object.keys(config.paths).sort(), [
    ".github/workflows/release.yml",
    ".github/workflows/webkit-release.yml",
  ]);
  for (const [name, group] of [
    ["release", "devduo-release"],
    ["webkit-release", "devduo-webkit-release"],
  ]) {
    const pipeline = workflow(name);
    assert.deepEqual(pipeline.concurrency, {
      group,
      queue: "max",
      "cancel-in-progress": false,
    });
    for (const job of Object.values(pipeline.jobs) as {
      concurrency?: unknown;
    }[])
      assert.equal(job.concurrency, undefined);
    const ignores = config.paths[`.github/workflows/${name}.yml`].ignore;
    assert.equal(ignores.length, 1);
    const exception = new RegExp(ignores[0]);
    assert.equal(
      ignores[0],
      '^unexpected key "queue" for "concurrency" section\\. expected one of "cancel-in-progress", "group"$',
    );
    assert.equal(
      exception.test(
        'unexpected key "queue" for "concurrency" section. expected one of "cancel-in-progress", "group"',
      ),
      true,
    );
    assert.equal(
      exception.test(
        'unexpected key "queues" for "concurrency" section. expected one of "cancel-in-progress", "group"',
      ),
      false,
    );
  }
});
