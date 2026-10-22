import { existsSync, lstatSync, mkdirSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { loadMetadata } from "./metadata.mts";
import { digest } from "./release-artifacts.mts";
import { run, temporary } from "./release-process.mts";

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
  const repository = url.pathname.slice(1);
  if (
    process.env.GITHUB_ACTIONS === "true" &&
    process.env.GITHUB_REPOSITORY !== repository
  )
    throw new Error("Workflow repository does not match release metadata.");
  return repository;
}

export function dryRun(event: string, value: string): boolean {
  if (
    !["push", "workflow_dispatch"].includes(event) ||
    !["true", "false"].includes(value)
  )
    throw new Error(
      "Release requires push/manual dispatch and an explicit boolean dry-run mode.",
    );
  if (event !== "workflow_dispatch" && value === "true")
    throw new Error("Only manual dispatch can select a release dry run.");
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
  const commit = (
    lines.find((line) => line.endsWith(`${ref}^{}`)) ??
    lines.find((line) => line.endsWith(ref))
  )?.split(/\s+/)[0];
  if (!commit || !/^[a-f0-9]{40}$/.test(commit))
    throw new Error("Cannot resolve the exact remote release tag.");
  return commit;
}

export function ensureTag(
  repository: string,
  tag: string,
  sha: string,
  mayCreate: boolean,
  execute: Runner = run,
): void {
  if (!/^[a-f0-9]{40}$/.test(sha))
    throw new Error("A full source commit is required.");
  const existing = tagCommit(tag, execute);
  if (!existing) {
    if (!mayCreate)
      throw new Error("The exact stable release tag must already exist.");
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
  if (tagCommit(tag, execute) !== sha)
    throw new Error("Existing release tag belongs to another commit.");
}

type Publication = {
  repository: string;
  tag: string;
  sha: string;
  prerelease: boolean;
  mayCreateTag: boolean;
  latest: boolean;
  title: string;
  notes: string;
  directory: string;
  files: string[];
};

export function publishRelease(
  options: Publication,
  execute: Runner = run,
): "published" | "verified" {
  const { repository, tag, files, directory } = options;
  if (
    !files.length ||
    new Set(files).size !== files.length ||
    files.some((name) => !/^[A-Za-z0-9][A-Za-z0-9_.-]*$/.test(name))
  )
    throw new Error("Publication requires unique, plain asset filenames.");
  for (const name of files)
    if (
      !lstatSync(join(directory, name)).isFile() ||
      !lstatSync(join(directory, name)).size
    )
      throw new Error(`Missing release asset: ${name}`);
  let release = findRelease(repository, tag, execute);
  if (release && release.prerelease !== options.prerelease)
    throw new Error("Existing release channel does not match metadata.");
  if (release && !tagCommit(tag, execute))
    throw new Error(
      "Existing release has no exact remote tag; inspect it before retrying.",
    );
  // Validate every existing byte before uploading any missing asset.
  const verifyExisting = (
    current: GitHubRelease,
    destination: string,
  ): Set<string> => {
    const seen = new Set<string>();
    for (const asset of current.assets) {
      if (
        !files.includes(asset.name) ||
        seen.has(asset.name) ||
        asset.state !== "uploaded"
      )
        throw new Error(
          "Unexpected, duplicate or unfinished existing release asset.",
        );
      seen.add(asset.name);
      const local = join(directory, asset.name);
      if (asset.size !== lstatSync(local).size)
        throw new Error(
          `Published asset differs; refusing to overwrite ${asset.name}.`,
        );
      execute("gh", [
        "release",
        "download",
        tag,
        "--repo",
        repository,
        "--pattern",
        asset.name,
        "--dir",
        destination,
      ]);
      const downloaded = join(destination, asset.name);
      if (!existsSync(downloaded) || digest(downloaded) !== digest(local))
        throw new Error(
          `Published asset differs; refusing to overwrite ${asset.name}.`,
        );
    }
    if (readdirSync(destination).length !== seen.size)
      throw new Error("Unexpected downloaded release assets.");
    return seen;
  };
  const present = temporary((path) =>
    release ? verifyExisting(release, path) : new Set<string>(),
  );
  const missing = files.filter((name) => !present.has(name));
  if (release && !release.draft && missing.length)
    throw new Error(
      "Published release is incomplete; it cannot be repaired automatically.",
    );
  ensureTag(repository, tag, options.sha, options.mayCreateTag, execute);
  if (!release) {
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
    if (!release?.draft || release.prerelease !== options.prerelease)
      throw new Error("GitHub did not confirm the new draft release.");
  }
  if (!release.draft) return "verified";
  if (missing.length)
    execute("gh", [
      "release",
      "upload",
      tag,
      ...missing.map((name) => join(directory, name)),
      "--repo",
      repository,
    ]);
  const uploaded = findRelease(repository, tag, execute);
  if (
    !uploaded?.draft ||
    uploaded.prerelease !== options.prerelease ||
    uploaded.id !== release.id
  )
    throw new Error("Draft release changed during publication.");
  temporary((path) => {
    mkdirSync(path, { recursive: true });
    if (verifyExisting(uploaded, path).size !== files.length)
      throw new Error("Draft upload is incomplete.");
  });
  ensureTag(repository, tag, options.sha, false, execute);
  execute("gh", [
    "release",
    "edit",
    tag,
    "--repo",
    repository,
    "--verify-tag",
    "--draft=false",
    `--prerelease=${options.prerelease}`,
    `--latest=${options.latest}`,
  ]);
  const published = findRelease(repository, tag, execute);
  if (
    !published ||
    published.draft ||
    published.prerelease !== options.prerelease ||
    published.id !== release.id
  )
    throw new Error("GitHub did not confirm release publication.");
  return "published";
}
