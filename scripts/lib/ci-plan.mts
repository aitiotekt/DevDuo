import { spawnSync } from "node:child_process";

type Event = {
  before?: string;
  pull_request?: { base?: { sha?: string }; head?: { sha?: string } };
};

const commit = /^[a-f0-9]{40}$/;

export function changeRange(eventName: string, event: Event, sha: string) {
  if (eventName === "pull_request") {
    const base = event.pull_request?.base?.sha;
    const head = event.pull_request?.head?.sha;
    if (base && head && commit.test(base) && commit.test(head))
      return `${base}...${head}`;
  }
  if (
    eventName === "push" &&
    event.before &&
    commit.test(event.before) &&
    !/^0+$/.test(event.before) &&
    commit.test(sha)
  )
    return `${event.before}..${sha}`;
  return null;
}

export function documentationOnly(paths: string[]) {
  return (
    paths.length > 0 &&
    paths.every(
      (path) =>
        /^[^/]+\.md$/.test(path) ||
        path.startsWith("docs/") ||
        path.startsWith("docsite/") ||
        path === "scripts/docs-manifest.json" ||
        path === "scripts/docs.mts" ||
        path === "scripts/lib/docs.mts" ||
        path === "scripts/__tests__/docs.test.mts",
    )
  );
}

export function planChecks(
  eventName: string,
  event: Event,
  sha: string,
  forceNative: boolean,
  cwd: string,
) {
  if (forceNative)
    return {
      native: true,
      reason: "The caller requires all native compilers.",
    };
  const range = changeRange(eventName, event, sha);
  if (!range)
    return {
      native: true,
      reason: "No complete comparison range; run all checks.",
    };
  const result = spawnSync(
    "git",
    ["diff", "--name-only", "--no-renames", "-z", range, "--"],
    { cwd, encoding: "utf8", maxBuffer: 16 * 1024 * 1024, windowsHide: true },
  );
  if (result.error || result.status !== 0)
    return {
      native: true,
      reason: "The complete comparison is unavailable; run all checks.",
    };
  const paths = result.stdout.split("\0").filter(Boolean);
  // A previous push may have been cancelled before its compilers completed.
  // Only the complete PR comparison can safely omit a native compiler run.
  const native = eventName !== "pull_request" || !documentationOnly(paths);
  return {
    native,
    reason: native
      ? eventName === "push"
        ? "A push baseline may have unfinished checks; compile all enabled targets."
        : "The complete change includes native or shared inputs."
      : "Only documentation inputs changed; native compilation is not required.",
  };
}
