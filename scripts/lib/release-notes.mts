import { readFileSync } from "node:fs";
import { join } from "node:path";
import { repoRoot } from "./paths.mts";
import { parseVersion } from "./release-version.mts";

// Changelogs use exact `## VERSION` sections. Ignore headings inside code fences
// so examples cannot select or prematurely terminate a release entry.
export function changelogEntry(source: string, version: string): string {
  parseVersion(version);
  const entries: string[][] = [];
  let current: string[] | undefined;
  let fence: { character: string; length: number } | undefined;
  for (const line of source.replaceAll("\r\n", "\n").split("\n")) {
    const marker = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(line);
    if (fence) {
      current?.push(line);
      if (
        marker &&
        marker[1][0] === fence.character &&
        marker[1].length >= fence.length &&
        !marker[2].trim()
      )
        fence = undefined;
      continue;
    }
    if (marker) {
      fence = { character: marker[1][0], length: marker[1].length };
      current?.push(line);
      continue;
    }
    const heading = /^##\s+(.+?)\s*$/.exec(line);
    if (heading) {
      current = heading[1] === version ? [] : undefined;
      if (current) entries.push(current);
    } else current?.push(line);
  }
  if (
    entries.length !== 1 ||
    !entries[0]
      .join("\n")
      .replace(/<!--[\s\S]*?-->/g, "")
      .trim()
  )
    throw new Error(
      `Changelog must contain exactly one nonempty ## ${version} section.`,
    );
  return entries[0].join("\n").trim();
}

export function releaseNotes(version: string, root = repoRoot): string {
  const entry = (path: string) => {
    try {
      return changelogEntry(readFileSync(join(root, path), "utf8"), version);
    } catch (error) {
      throw new Error(
        `${path}: ${error instanceof Error ? error.message : String(error)}`,
      );
    }
  };
  return `## What's new in ${version}\n\n${entry("CHANGELOG.md")}\n\n## ${version} 更新内容\n\n${entry("docs/zh/CHANGELOG.md")}\n\n---\n\n${readFileSync(join(root, ".github/release-notes.md"), "utf8").trim()}\n`;
}
