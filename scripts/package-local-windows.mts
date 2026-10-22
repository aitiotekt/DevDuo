import { execFileSync } from "node:child_process";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { loadMetadata } from "./lib/metadata.mts";
import { repoRoot } from "./lib/paths.mts";
import { digest } from "./lib/release-artifacts.mts";
import {
  regularFiles,
  safeRelative,
  validateRuntime,
} from "./lib/webkit-release.mts";

// A local preview deliberately retains the engine's original build identity.
// It neither invents a published runtime pin nor certifies current build orchestration.
try {
  if (process.platform !== "win32" || process.arch !== "x64")
    throw new Error("Local Windows packaging requires native Windows x64.");
  const metadata = loadMetadata();
  const stage = join(repoRoot, "temp/build/local-release/win-x64");
  const app = join(stage, metadata.project.binaryName);
  const output = join(repoRoot, "temp/local-release");
  const archive = join(
    output,
    `DevDuo-v${metadata.project.version}-win-x64-local.zip`,
  );
  const build = join(repoRoot, "temp/build/windows-webkit-host");
  const runtime = join(app, "webkit");
  const copy = (source: string, destination: string) => {
    mkdirSync(dirname(destination), { recursive: true });
    copyFileSync(source, destination);
  };
  if (process.argv[2] === "prepare") {
    if (
      resolve(stage) !== resolve(repoRoot, "temp/build/local-release/win-x64")
    )
      throw new Error("Refusing an unexpected local staging directory.");
    rmSync(stage, { recursive: true, force: true });
    mkdirSync(stage, { recursive: true });
  } else if (process.argv[2] === "bundle") {
    for (const name of [
      "DevDuo.exe",
      "coreclr.dll",
      "DevDuo.Windows.Compositor.dll",
    ])
      if (!existsSync(join(app, name)))
        throw new Error(`Missing local publish component: ${name}`);
    if (existsSync(runtime))
      throw new Error("Prepare a fresh local package before bundling.");
    const manifestText = readFileSync(
      join(build, "runtime-manifest.json"),
      "utf8",
    ).replace(/^\uFEFF/, "");
    const manifest = JSON.parse(manifestText);
    if (digest(join(build, "build-inputs.json")) !== manifest.buildInputsSha256)
      throw new Error(
        "Original engine build provenance does not match its manifest.",
      );
    for (const file of manifest.files) {
      const name = safeRelative(file.path);
      if (digest(join(build, "bin", name)) !== file.sha256)
        throw new Error(`Local engine integrity mismatch: ${name}`);
      copy(join(build, "bin", name), join(runtime, "bin", name));
    }
    copy(
      join(build, "runtime-manifest.json"),
      join(runtime, "original-runtime-manifest.json"),
    );
    // Windows PowerShell emits a UTF-8 BOM; retain those original bytes separately
    // while giving the shared JavaScript validator a standard UTF-8 JSON document.
    writeFileSync(join(runtime, "runtime-manifest.json"), manifestText);
    copy(join(build, "build-inputs.json"), join(runtime, "build-inputs.json"));
    for (const file of manifest.inputFiles) {
      const name = safeRelative(file.path);
      const frozen = join(build, "inputs", name);
      if (digest(frozen) !== file.sha256)
        throw new Error(`Original engine input mismatch: ${name}`);
      copy(frozen, join(runtime, "original-source-inputs", name));
    }
    const upstream = join(
      repoRoot,
      "temp/cache/windows-webkit",
      `source-${metadata.webkit.upstreamRevision.slice(0, 8)}`,
    );
    let notices = 0;
    for (const area of [
      "Source/WebCore",
      "Source/JavaScriptCore",
      "Source/WTF",
      "Source/ThirdParty",
    ]) {
      const directory = join(upstream, area);
      for (const entry of readdirSync(directory, {
        recursive: true,
        withFileTypes: true,
      })) {
        if (
          !entry.isFile() ||
          !/^(LICENSE|COPYING)([.-].*)?$/i.test(entry.name)
        )
          continue;
        const source = join(entry.parentPath, entry.name);
        copy(
          source,
          join(runtime, "licenses/WebKit", relative(upstream, source)),
        );
        notices++;
      }
    }
    const dependencies = join(
      build,
      "vcpkg_installed/x64-windows-devduo/share",
    );
    for (const name of regularFiles(dependencies).filter((name) =>
      name.endsWith("/copyright"),
    )) {
      copy(join(dependencies, name), join(runtime, "licenses/vcpkg", name));
      notices++;
    }
    if (!notices) throw new Error("Missing upstream notices.");
    for (const name of ["LICENSE", "THIRD_PARTY_NOTICES.md", "PRIVACY.md"])
      copy(join(repoRoot, name), join(app, name));
    validateRuntime(runtime, metadata, false);
    writeFileSync(
      join(app, "LOCAL-PREVIEW.txt"),
      "Local Windows x64 Release preview. Run DevDuo.exe with this complete directory.\n" +
        "Includes .NET and the existing local WebKit engine. Not a signed or published release.\n" +
        "Original engine manifest, frozen build inputs and notices are retained. Build orchestration may have changed since that engine build.\n" +
        "Website data persists in LocalApplicationData/DevDuo/WebKit. F12, Ctrl+Shift+I or Developer tools toggles an independent Web Inspector. Rotation keeps the document.\n" +
        "Developer tools includes opt-in native remote inspection, chobitsu CDP with an offline chii frontend, and page proxy settings. Chrome DevTools requires the system WebView2 Runtime.\n\n" +
        "Without a configured website, startup opens an offline welcome page. Home returns there. Developer tools is the first settings group and starts collapsed; color and calibration pages are at its bottom. Active tab titles follow the document. Native fixed masks and dialog backdrops extend blended colors through obscured insets without resizing the viewport.\n\n" +
        "Windows Safari's main vertical scroll indicator appears at the screen edge during native scrolling and fades at rest, including at the document end. CSS hiding, zero gutter and viewport geometry are preserved. Indicator thickness and timing are engineering approximations.\n\n" +
        "Windows x64 本地 Release 体验包。保持目录完整并运行 DevDuo.exe。\n" +
        "已包含 .NET 和现有本地 WebKit，不是签名或公开发布。保留引擎原始清单、冻结输入和许可，不代表用最新构建编排重新构建或完成全部原生验收。\n" +
        "网站数据保存在用户本地应用数据的 DevDuo/WebKit；F12、Ctrl+Shift+I 或开发者工具按钮开关独立 Inspector；旋转保留文档。\n" +
        "开发者工具提供按需开启的原生远程检查、chobitsu CDP 与离线 chii 前端，以及页面代理设置。Chrome DevTools 工具窗需要系统 WebView2 Runtime。\n" +
        "未配置网站时从离线欢迎页启动，主页按钮可返回。开发者工具置于设置区最上方，每次启动默认折叠；颜色和标定入口位于其最底部。活动标签标题跟随文档；原生 fixed 遮罩和 dialog 背景将混合色延伸到遮挡 inset，不扩大真实视口。\n" +
        "Windows Safari 主纵向滚动指示器在原生滚动时显示于屏幕外缘，静止后淡出，到达文档末端同样适用；保留 CSS 隐藏规则、零沟槽和视口几何。指示器厚度及时长属于工程近似。\n",
    );
    mkdirSync(output, { recursive: true });
    if (existsSync(archive))
      throw new Error(
        "A local archive already exists; preserve or move it before repackaging.",
      );
    execFileSync(
      "tar",
      ["-a", "-c", "-f", archive, "-C", stage, metadata.project.binaryName],
      { stdio: "inherit" },
    );
    const report = {
      version: metadata.project.version,
      localWorkingTreeBuild: true,
      sourceSha: execFileSync("git", ["rev-parse", "HEAD"], {
        cwd: repoRoot,
        encoding: "utf8",
      }).trim(),
      workingTreeDirty:
        execFileSync("git", ["status", "--porcelain"], {
          cwd: repoRoot,
          encoding: "utf8",
        }).length > 0,
      engineSource: "existing-local-build-with-retained-provenance",
      nativeAcceptancePassed: false,
      runtimeManifestSha256: digest(join(runtime, "runtime-manifest.json")),
      originalRuntimeManifestSha256: digest(
        join(runtime, "original-runtime-manifest.json"),
      ),
      archive: { name: relative(output, archive), sha256: digest(archive) },
    };
    writeFileSync(`${archive}.json`, `${JSON.stringify(report, null, 2)}\n`);
    writeFileSync(
      `${archive}.sha256`,
      `${report.archive.sha256}  ${report.archive.name}\n`,
    );
    console.log(archive);
  } else throw new Error("Use prepare or bundle.");
} catch (error) {
  console.error(
    error instanceof Error ? error.message : "Local Windows packaging failed.",
  );
  process.exitCode = 1;
}
