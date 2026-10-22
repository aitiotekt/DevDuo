import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { repoRoot } from "./paths.mts";

export function run(command: string, args: string[], capture = false): string {
  const result = spawnSync(command, args, {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: capture ? "pipe" : "inherit",
    maxBuffer: 16 * 1024 * 1024,
    windowsHide: true,
  });
  // Arguments can include signing credentials; never echo them in failures.
  if (result.error || result.status !== 0)
    throw new Error(`${command} failed (${result.status ?? "spawn error"}).`);
  return result.stdout?.trim() ?? "";
}

export function temporary<T>(operation: (path: string) => T): T {
  const path = mkdtempSync(join(tmpdir(), "devduo-release-"));
  if (
    dirname(resolve(path)) !== resolve(tmpdir()) ||
    !path.startsWith(join(tmpdir(), "devduo-release-"))
  )
    throw new Error("Refusing to use an unexpected release staging path.");
  try {
    return operation(path);
  } finally {
    rmSync(path, { recursive: true });
  }
}
