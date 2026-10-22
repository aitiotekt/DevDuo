import { join } from "node:path";
import { loadMetadata } from "./metadata.mts";
import { run } from "./release-process.mts";

export type Runner = typeof run;
export type GitHubRelease = {
  id: number;
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  assets: { name: string; size: number; state: string }[];
};

export function repositoryName(metadata = loadMetadata()): string {
  const url = new URL(metadata.project.repositoryUrl);
  if (
    url.hostname !== "github.com" ||
    !/^\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(url.pathname)
  )
    throw new Error("Release requires a GitHub project repository URL.");
  return url.pathname.slice(1);
}

export function dryRun(value: string): boolean {
  return value === "true";
}

export function findRelease(
  repository: string,
  tag: string,
  execute: Runner = run,
): GitHubRelease | undefined {
  // A failed listing must never be interpreted as an absent release.
  const pages: unknown = JSON.parse(
    execute(
      "gh",
      [
        "api",
        "--paginate",
        "--slurp",
        `repos/${repository}/releases?per_page=100`,
      ],
      true,
    ),
  );
  if (!Array.isArray(pages) || pages.some((page) => !Array.isArray(page)))
    throw new Error("Invalid GitHub release listing.");
  const matches = pages.flat().filter((release) => release?.tag_name === tag);
  if (matches.length > 1) throw new Error("Duplicate GitHub release tags.");
  const release = matches[0];
  if (release) return validateRelease(release);

  // Drafts can have an `untagged-*` REST tag while their pending tag is the
  // requested version. Resolve that association through GraphQL, as gh does.
  const [owner, name] = repository.split("/");
  const lookup = JSON.parse(
    execute(
      "gh",
      [
        "api",
        "graphql",
        "-f",
        "query=query($owner:String!,$name:String!,$tag:String!){repository(owner:$owner,name:$name){release(tagName:$tag){databaseId isDraft}}}",
        "-f",
        `owner=${owner}`,
        "-f",
        `name=${name}`,
        "-f",
        `tag=${tag}`,
      ],
      true,
    ),
  );
  const result = lookup?.data?.repository;
  if (
    (lookup?.errors !== undefined &&
      (!Array.isArray(lookup.errors) || lookup.errors.length)) ||
    !result ||
    typeof result !== "object" ||
    !Object.hasOwn(result, "release")
  )
    throw new Error("Cannot confirm GitHub release by its pending tag.");
  if (result.release === null) return undefined;
  const pending = result.release;
  if (
    !Number.isSafeInteger(pending?.databaseId) ||
    pending.databaseId <= 0 ||
    typeof pending.isDraft !== "boolean"
  )
    throw new Error("Invalid GitHub pending release response.");
  const resolved = validateRelease(
    JSON.parse(
      execute(
        "gh",
        ["api", `repos/${repository}/releases/${pending.databaseId}`],
        true,
      ),
    ),
  );
  if (
    resolved.id !== pending.databaseId ||
    (!resolved.draft && resolved.tag_name !== tag)
  )
    throw new Error("GitHub release identity changed during lookup.");
  return { ...resolved, tag_name: tag };
}

function validateRelease(release: unknown): GitHubRelease {
  if (
    !release ||
    typeof release !== "object" ||
    !("id" in release) ||
    typeof release.id !== "number" ||
    !Number.isSafeInteger(release.id) ||
    release.id <= 0 ||
    !("tag_name" in release) ||
    typeof release.tag_name !== "string" ||
    !("draft" in release) ||
    typeof release.draft !== "boolean" ||
    !("prerelease" in release) ||
    typeof release.prerelease !== "boolean" ||
    !("assets" in release) ||
    !Array.isArray(release.assets)
  )
    throw new Error("Invalid GitHub release response.");
  return release as GitHubRelease;
}

export function tagCommit(
  tag: string,
  execute: Runner = run,
): string | undefined {
  const ref = `refs/tags/${tag}`;
  const output = execute(
    "git",
    ["ls-remote", "origin", ref, `${ref}^{}`],
    true,
  );
  if (!output) return undefined;
  const lines = output.split(/\r?\n/);
  return (
    lines.find((line) => line.endsWith(`${ref}^{}`)) ??
    lines.find((line) => line.endsWith(ref))
  )?.split(/\s+/)[0];
}

export function ensureTag(
  repository: string,
  tag: string,
  sha: string,
  execute: Runner = run,
): void {
  if (tagCommit(tag, execute)) return;
  execute(
    "gh",
    [
      "api",
      "--method",
      "POST",
      `repos/${repository}/git/refs`,
      "-f",
      `ref=refs/tags/${tag}`,
      "-f",
      `sha=${sha}`,
    ],
    true,
  );
}

type Publication = {
  repository: string;
  tag: string;
  sha: string;
  prerelease: boolean;
  latest: boolean;
  title: string;
  notes: string;
  directory: string;
  files: string[];
};

// Creates the tag and a draft when needed, uploads assets the release does not
// have yet, then publishes the draft. Existing assets are never replaced.
export function publishRelease(
  options: Publication,
  execute: Runner = run,
): "published" | "existing" {
  const { repository, tag, files, directory } = options;
  let release = findRelease(repository, tag, execute);
  if (!release) {
    ensureTag(repository, tag, options.sha, execute);
    execute("gh", [
      "release",
      "create",
      tag,
      "--repo",
      repository,
      "--verify-tag",
      "--draft",
      "--title",
      options.title,
      "--notes-file",
      options.notes,
      `--prerelease=${options.prerelease}`,
      "--latest=false",
    ]);
    release = findRelease(repository, tag, execute);
    if (!release) throw new Error("GitHub did not create the draft release.");
  }
  const present = new Set(
    release.assets
      .filter((asset) => asset.state === "uploaded")
      .map((asset) => asset.name),
  );
  const missing = files.filter((name) => !present.has(name));
  if (missing.length)
    execute("gh", [
      "release",
      "upload",
      tag,
      ...missing.map((name) => join(directory, name)),
      "--repo",
      repository,
    ]);
  if (!release.draft) return "existing";
  execute("gh", [
    "release",
    "edit",
    tag,
    "--repo",
    repository,
    "--draft=false",
    `--prerelease=${options.prerelease}`,
    `--latest=${options.latest}`,
  ]);
  return "published";
}
