import { gt, inc, valid } from "semver";

export function parseVersion(version: string) {
  const match =
    /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(dev|alpha\.(?:0|[1-9]\d*)|beta\.(?:0|[1-9]\d*)))?$/.exec(
      version,
    );
  if (!match || !valid(version))
    throw new Error("Use MAJOR.MINOR.PATCH[-dev|-alpha.N|-beta.N].");
  return {
    version,
    core: `${match[1]}.${match[2]}.${match[3]}`,
    channel: match[4]?.split(".")[0] ?? "stable",
  };
}

export function nextVersion(version: string, level: string): string {
  const parsed = parseVersion(version);
  let next: string | null;
  switch (level) {
    case "major":
    case "minor":
    case "patch":
      next = inc(version, level);
      break;
    case "alpha":
    case "beta":
      next = inc(version, "prerelease", level);
      break;
    case "prerelease":
      if (parsed.channel !== "alpha" && parsed.channel !== "beta")
        throw new Error(
          "Choose alpha or beta before incrementing a prerelease.",
        );
      next = inc(version, "prerelease");
      break;
    case "stable":
      next = parsed.core;
      break;
    default:
      throw new Error(
        "Use major, minor, patch, alpha, beta, prerelease or stable.",
      );
  }
  // -dev is an unpublished marker; starting alpha/beta may sort lower lexically in SemVer.
  const startsPrerelease =
    parsed.channel === "dev" && (level === "alpha" || level === "beta");
  if (!next || (!startsPrerelease && !gt(next, version)))
    throw new Error(
      "The new version must be greater than the current version.",
    );
  parseVersion(next);
  return next;
}
