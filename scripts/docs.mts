import { spawnSync } from "node:child_process";
import { assertSymlinkSupport, checkDocs, syncDocs } from "./lib/docs.mts";
import { repoRoot } from "./lib/paths.mts";

const [command, ...extra] = process.argv.slice(2);

try {
  if (extra.length)
    throw new Error("Use docs sync|check|doctor without arguments.");
  switch (command) {
    case "sync":
      syncDocs();
      break;
    case "check":
      checkDocs();
      break;
    case "doctor": {
      const result = spawnSync("git", ["config", "--get", "core.symlinks"], {
        cwd: repoRoot,
        encoding: "utf8",
        shell: false,
        windowsHide: true,
      });
      if (result.error) throw result.error;
      if (result.status !== 0 && result.status !== 1)
        throw new Error("Cannot read Git symbolic-link configuration.");
      if (result.stdout.trim() === "false")
        throw new Error(
          "core.symlinks=false is unsupported. Set git config core.symlinks true and use a symlink-capable checkout; see CONTRIBUTING.md.",
        );
      assertSymlinkSupport();
      console.log("PASS: real relative file and directory symbolic links");
      break;
    }
    case undefined:
    case "--help":
      console.log(
        "Documentation helper: node scripts/docs.mts sync|check|doctor",
      );
      break;
    default:
      throw new Error(`Unknown documentation command: ${command}. Use --help.`);
  }
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}
