import { packageMacOS } from "./lib/package-macos.mts";

try {
  packageMacOS();
} catch (error) {
  console.error(error instanceof Error ? error.message : error);
  process.exitCode = 1;
}
