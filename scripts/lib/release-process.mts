import { spawnSync } from "node:child_process";
import { repoRoot } from "./paths.mts";

export function run(command: string, args: string[], capture = false): string {
  const result = spawnSync(command, args, {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: capture ? "pipe" : "inherit",
  });
  // Arguments can include signing credentials; never echo them in failures.
  if (result.error || result.status !== 0)
    throw new Error(`${command} failed (${result.status ?? "spawn error"}).`);
  return result.stdout?.trim() ?? "";
}
