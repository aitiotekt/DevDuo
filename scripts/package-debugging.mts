import { createHash } from "node:crypto";
import { cp, mkdir, readdir, readFile, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { repoRoot } from "./lib/paths.mts";

const require = createRequire(import.meta.url);
const chiiRoot = dirname(require.resolve("chii/package.json"));
const chobitsuRoot = dirname(dirname(require.resolve("chobitsu")));
const output = join(repoRoot, "temp/build/debugging");
const packages = [
  ["chii", chiiRoot, "1.15.5"],
  ["chobitsu", chobitsuRoot, "1.8.6"],
] as const;

for (const [name, root, version] of packages) {
  const actual = JSON.parse(await readFile(join(root, "package.json"), "utf8"));
  if (actual.version !== version) {
    throw new Error(
      `Expected ${name} ${version}; run pnpm install --frozen-lockfile`,
    );
  }
}

await mkdir(output, { recursive: true });
await cp(join(chiiRoot, "public/front_end"), join(output, "front_end"), {
  recursive: true,
});
await cp(join(chobitsuRoot, "dist/chobitsu.js"), join(output, "chobitsu.js"));
await cp(join(chiiRoot, "LICENSE"), join(output, "chii-LICENSE.txt"));
await cp(join(chobitsuRoot, "LICENSE"), join(output, "chobitsu-LICENSE.txt"));
await cp(
  join(repoRoot, "scripts/licenses/debugging"),
  join(output, "licenses"),
  {
    recursive: true,
  },
);

// Retain license texts from the locked backend dependency tree as well as
// comments already embedded in the published browser bundle.
const licensedPackages = new Set<string>();
const omittedLicenseSources: Record<string, string> = {
  "licia@1.49.0": "licia-LICENSE.txt",
  "luna-dom-highlighter@1.0.2": "luna-LICENSE.txt",
};
async function copyDependencyLicenses(root: string): Promise<void> {
  const pkg = JSON.parse(await readFile(join(root, "package.json"), "utf8"));
  const identity = `${pkg.name}@${pkg.version}`;
  if (licensedPackages.has(identity)) return;
  licensedPackages.add(identity);
  const texts = (await readdir(root, { withFileTypes: true })).filter(
    (file) =>
      file.isFile() && /^(licen[cs]e|copying|notice)(\.|$)/i.test(file.name),
  );
  // These published packages omit LICENSE; retain reviewed upstream texts.
  if (texts.length === 0 && !omittedLicenseSources[identity])
    throw new Error(`Missing dependency license: ${identity}`);
  const destination = join(output, "licenses", identity.replaceAll("/", "_"));
  await mkdir(destination, { recursive: true });
  for (const text of texts)
    await cp(join(root, text.name), join(destination, text.name));
  if (omittedLicenseSources[identity]) {
    await cp(
      join(
        repoRoot,
        "scripts/licenses/debugging",
        omittedLicenseSources[identity],
      ),
      join(destination, "LICENSE"),
    );
  }
  const resolve = createRequire(join(root, "package.json"));
  for (const name of Object.keys(pkg.dependencies ?? {})) {
    await copyDependencyLicenses(
      dirname(resolve.resolve(`${name}/package.json`)),
    );
  }
}
await copyDependencyLicenses(chobitsuRoot);
// The chii distribution retains bundled license comments in its frontend files.
await cp(
  join(chiiRoot, "public/target.js.LICENSE.txt"),
  join(output, "bundled-LICENSE.txt"),
);
const script = await readFile(join(output, "chobitsu.js"));
await writeFile(
  join(output, "manifest.json"),
  `${JSON.stringify({ chii: "1.15.5", chobitsu: "1.8.6", chobitsuSha256: createHash("sha256").update(script).digest("hex"), dependencyLicenseSources: [...licensedPackages].sort() }, null, 2)}\n`,
);
console.log("Packaged offline chii frontend and chobitsu assets.");
