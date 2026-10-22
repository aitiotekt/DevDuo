import { copyFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { repoRoot } from "./paths.mts";

// Copy before signing so both development and release bundles use Icon Composer.
export function copyMacOSIcons(resources: string): void {
  mkdirSync(resources, { recursive: true });
  for (const name of ["Assets.car", "AppIcon.icns"])
    copyFileSync(join(repoRoot, "assets/icons", name), join(resources, name));
}
