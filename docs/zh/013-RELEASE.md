# 发布与仓库管理

## 当前范围

DevDuo 是实验性 Avalonia 工作台，已有 macOS WKWebView 基线及 Windows WebView2 实现，但 Windows 原生输入、DPI 和绘制验收尚未完成。Linux 运行跨平台检查和文档工具，不生成 Linux 桌面包。构建和签名成功不代表原生验收通过，也不代表已取得模拟器衍生几何数据的再分发权。保留[第三方声明](THIRD_PARTY_NOTICES.md)。

发布流水线生成自带 .NET 10 的 **macOS 27 arm64** 和 **Windows x64** 包。Windows 仍需 WebView2 Runtime。macOS 使用 GitHub 的 `xcode-27` 预览运行器，已对照[运行器清单](https://github.com/actions/runner-images#available-images)核实；运行器可用性是外部依赖。原有 `just package-macos` 仍生成未签名、依赖外部 .NET 的开发包。发布包保留 `local.DevDuo.Calibration` Bundle ID，直接使用原生应用启动器。

## 版本和触发规则

[`devduo-metadata.toml`](../../devduo-metadata.toml) 统一管理版本、应用标识、URL 和发布策略。`just set-version VERSION` 同步 TOML、两个 Node 清单和原生项目。`just bump-version LEVEL` 支持 `major`、`minor`、`patch`、`alpha`、`beta`、`prerelease`、`stable`，预览版编号从零开始。例如 `0.1.0-dev` 经 `alpha` 变为 `0.1.0-alpha.0`，再经 `prerelease` 变为 `0.1.0-alpha.1`。

直接编辑 TOML 版本后执行 `just sync-version`。`just check-version` 检查版本一致性；`just release-check` 额外校验工作流的分支/tag 过滤器与元数据一致。修改版本不会发布或创建 tag，双语变更日志需另行编辑。

| 来源 | 版本 | 结果 |
| --- | --- | --- |
| `release` 分支 | `X.Y.Z-dev` | 明确跳过成功，不打包、不发布 |
| `release` 分支 | `X.Y.Z-alpha.N` / `X.Y.Z-beta.N` | 检查、构建、签名、生成来源证明并发布预览版 |
| 精确的 `vX.Y.Z[-alpha.N/-beta.N]` tag | 与包版本一致 | 检查并发布；正式版需要审批 |
| 其他分支或不匹配的 tag | 任意 | 在访问签名凭据前拒绝 |

推送到 `release` 或 `v*` tag 触发流水线。手动触发使用所选 ref 及其不可变的 `github.sha`，不能指定无关源码 SHA。所有任务检出同一个触发提交。可复用 Checks 在签名任务启动前重新执行跨平台测试、本地化测试、lint/typecheck、文档构建及 macOS/Windows 原生编译。发布串行执行，凭据不传给 PR 或可复用检查任务。

正式版需通过 `stable-release` 环境审批。预览版与正式版的 macOS 包均强制 Developer ID 签名和 Apple 公证；与参考项目的可配置未签名预览版不同，本项目没有未签名发布降级。签名启用安全时间戳、加固运行时和 .NET JIT 权限。先签全部内嵌 Mach-O，再签应用，然后公证、装订票据并通过 Gatekeeper 检查后压缩。参见 [Microsoft macOS 部署指南](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)。

## 产物和重试

- `DevDuo-vVERSION-osx-arm64.zip`：已签名、公证的 `.app`，含 .NET 和声明文件。
- `DevDuo-vVERSION-win-x64.zip`：自带运行时的便携目录和声明文件；**没有 Authenticode 签名**。
- `release-manifest.json`：精确版本、源码 ref/SHA、预期文件、大小、SHA-256 和平台签名状态。
- `SHA256SUMS`：两份压缩包及清单的哈希。四个下载文件均生成 GitHub 构建来源证明；它不会让 Windows 显示“已验证的发布者”。

组装必须收到两个平台的报告和压缩包，且不能夹带其他文件。发布任务再次校验下载的产物集，创建或验证 tag，上传草稿，重新下载验证字节后才公开。已有 Release 仅在全部文件相同时接受；内容不同或不完整的草稿会失败，不覆盖文件。上传中断时，检查草稿并用原始最终产物补齐，或明确删除尚未公开的草稿后重试。重建签名包可能改变字节，因此重试不能静默替换已发布版本。相同内容的空操作重试不会重新提升为 latest。

发布暂存位于忽略的 `temp/build/release/`，最终包和报告位于 `temp/release/`。独立的 `packages.net10.0.osx-arm64.lock.json`、`packages.net10.0-windows.win-x64.lock.json` 锁定发布运行时依赖，不改变日常开发还原。需要主动更新时，使用 `dotnet restore`，分别传入 `-p:RuntimeIdentifier=osx-arm64` 或 `-p:RuntimeIdentifier=win-x64 -p:DevDuoWindows=true -p:EnableWindowsTargeting=true`，再加 `--force-evaluate`，审查依赖变化。

## GitHub 管理与文档

独立的 **aitiotekt-infra** 仓库通过 `meta/github-repositories.yml` 中的 DevDuo 条目管理默认只读工作流权限、`release` 分支保护、审批/签名/Pages 环境及 Pages 配置。Maintain/Admin 可绕过合并检查直接推送，但不能跳过发布工作流验证；独立安全规则禁止强推和删除。PR 合并要求 `Checks report` 状态。应用脚本不重复管理这些设置。

`release-signing` 环境仅接受 `release` 和 `v*` tag，需要 `APPLE_CERTIFICATE`、`APPLE_CERTIFICATE_PASSWORD`、`APPLE_SIGNING_IDENTITY`、`APPLE_ID`、`APPLE_PASSWORD` 和 `APPLE_TEAM_ID`。基础设施通过 SOPS 和 OpenBao 引用共享 Developer ID 证书，凭据不得进入本仓库。正式版审批允许唯一维护者自行审批，禁止管理员绕过。

文档配置为 **https://dev-duo.aitiotekt.com/**，VitePress base 为 `/`。文档在 `main`、`release` 和 PR 上构建，仅 `main` 通过 `github-pages` 环境部署。基础设施管理指向 `aitiotekt.github.io` 的仅 DNS CNAME 和 Pages 自定义域名。运行发布前需应用配置并配置环境密钥。GitHub 签发证书后再启用 Pages 强制 HTTPS。源码中的配置本身不证明远端已部署、已签发证书或已有 Release。

[English](../en/013-RELEASE.md) | [中文](013-RELEASE.md)
