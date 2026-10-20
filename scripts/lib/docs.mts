import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readdirSync,
  readFileSync,
  readlinkSync,
  realpathSync,
  rmSync,
  symlinkSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { repoRoot } from "./paths.mts";

export type LinkEntry = { link: string; target: string; type: "file" | "dir" };
export type DocumentEntry = {
  name: string;
  en: string;
  zh: string;
  section: "development" | "calibration" | "topics";
};
type Manifest = {
  conventions: string[];
  documents: DocumentEntry[];
  links: LinkEntry[];
};
export const manifest = JSON.parse(
  readFileSync(join(repoRoot, "scripts/docs-manifest.json"), "utf8"),
) as Manifest;

function linkState(path: string): "missing" | "symlink" | "other" {
  try {
    return lstatSync(path).isSymbolicLink() ? "symlink" : "other";
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return "missing";
    throw error;
  }
}

function portableTarget(path: string): string {
  return path.replaceAll("\\", "/");
}

export function assertSymlinkSupport(root = repoRoot): void {
  const base = join(root, ".local");
  mkdirSync(base, { recursive: true });
  const folder = mkdtempSync(join(base, "symlink-check-"));
  try {
    writeFileSync(join(folder, "source.txt"), "DevDuo");
    mkdirSync(join(folder, "source-dir"));
    symlinkSync("source.txt", join(folder, "file-link"), "file");
    symlinkSync("source-dir", join(folder, "dir-link"), "dir");
    if (
      !lstatSync(join(folder, "file-link")).isSymbolicLink() ||
      !lstatSync(join(folder, "dir-link")).isSymbolicLink()
    )
      throw new Error("Symbolic links were not created.");
  } catch (error) {
    throw new Error(
      "Contributor symbolic-link support is required. On Windows enable Developer Mode or run with symlink privileges, use a supporting filesystem, and clone with core.symlinks=true. See CONTRIBUTING.md.",
      { cause: error },
    );
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
}

export function syncLinks(entries: LinkEntry[], root = repoRoot): void {
  for (const entry of entries) {
    const path = resolve(root, entry.link),
      target = resolve(root, entry.target);
    if (!existsSync(target))
      throw new Error(`Missing managed target: ${entry.target}`);
    const expected = portableTarget(relative(dirname(path), target));
    const state = linkState(path);
    if (state === "other")
      throw new Error(
        `Refusing to overwrite a regular path: ${entry.link}. Check Windows core.symlinks and reclone if links were checked out as text.`,
      );
    if (state === "symlink" && portableTarget(readlinkSync(path)) === expected)
      continue;
    mkdirSync(dirname(path), { recursive: true });
    if (state === "symlink") unlinkSync(path);
    symlinkSync(expected, path, entry.type);
  }
}

export function checkLinks(entries: LinkEntry[], root = repoRoot): void {
  for (const entry of entries) {
    const path = resolve(root, entry.link);
    if (linkState(path) !== "symlink")
      throw new Error(
        `Required relative symlink is missing: ${entry.link}. Run just docs-sync after checking CONTRIBUTING.md.`,
      );
    const expected = portableTarget(
      relative(dirname(path), resolve(root, entry.target)),
    );
    if (portableTarget(readlinkSync(path)) !== expected || !existsSync(path))
      throw new Error(`Incorrect or broken managed link: ${entry.link}`);
  }
}

export function checkParity(root = repoRoot): void {
  for (const doc of manifest.documents) {
    if (!["development", "calibration", "topics"].includes(doc.section))
      throw new Error(`Missing documentation reading group: ${doc.name}`);
  }
  const names = (language: string) =>
    readdirSync(join(root, "docs", language))
      .filter((name) => name.endsWith(".md"))
      .sort();
  const en = names("en"),
    zh = names("zh");
  if (JSON.stringify(en) !== JSON.stringify(zh))
    throw new Error(
      `English/Chinese documentation filenames differ: ${JSON.stringify({ en, zh })}`,
    );
  const expected = [
    ...manifest.conventions,
    ...manifest.documents.map((doc) => doc.name),
  ].sort();
  if (JSON.stringify(en) !== JSON.stringify(expected))
    throw new Error(
      "Document inventory differs from scripts/docs-manifest.json.",
    );
  for (const name of manifest.conventions) {
    for (const path of [join(root, name), join(root, "docs/zh", name)]) {
      if (linkState(path) !== "other")
        throw new Error(
          `Canonical document must be a regular file: ${relative(root, path)}`,
        );
    }
  }
  for (const name of readdirSync(root)) {
    if (
      /^(README|SECURITY|PRIVACY|CONTRIBUTING|CHANGELOG)[_.][a-z]{2}\.md$/i.test(
        name,
      )
    )
      throw new Error(
        `Language-suffixed root document is not allowed: ${name}`,
      );
  }
}

export function syncDocs(): void {
  assertSymlinkSupport();
  syncLinks(manifest.links);
  checkDocs();
}

export function checkDocs(): void {
  checkLinks(manifest.links);
  checkParity();
  console.log(
    `PASS: ${manifest.links.length} relative links and ${manifest.documents.length + manifest.conventions.length} bilingual document pairs`,
  );
}

function siteRoute(source: string): string | undefined {
  const normalized = source.split(sep).join("/");
  if (normalized === "LICENSE") return "/license";
  const name = normalized.split("/").at(-1) ?? "";
  const chinese = normalized.startsWith("docs/zh/");
  const prefix = chinese ? "/zh" : "";
  if (manifest.conventions.includes(name)) {
    return name === "README.md"
      ? `${prefix}/`
      : `${prefix}/${name.replace(/\.md$/, "").toLowerCase().replaceAll("_", "-")}`;
  }
  if (manifest.documents.some((doc) => doc.name === name))
    return `${prefix}/docs/${name.replace(/\.md$/, "")}`;
  return undefined;
}

// Resolve from the canonical file, not its docsite symlink, preserving GitHub and site links.
export function siteHref(href: string, sourcePath: string): string {
  if (
    !href ||
    href.startsWith("#") ||
    href.startsWith("/") ||
    /^[a-z][a-z0-9+.-]*:/i.test(href)
  )
    return href;
  const separator = href.search(/[?#]/),
    path = separator < 0 ? href : href.slice(0, separator),
    suffix = separator < 0 ? "" : href.slice(separator);
  if (!sourcePath || !existsSync(sourcePath)) return href;
  const source = realpathSync(sourcePath),
    target = resolve(dirname(source), path);
  if (!existsSync(target)) return href;
  const route = siteRoute(relative(repoRoot, realpathSync(target)));
  if (route) return route + suffix;
  const repoPath = relative(repoRoot, realpathSync(target))
    .split(sep)
    .join("/");
  return `https://github.com/aitiotekt/DevDuo/blob/main/${repoPath}${suffix}`;
}
