# 发布与仓库管理

## 当前范围

DevDuo 是实验性 Avalonia 工作台，已有 macOS WKWebView 基线及 Windows WebKit 实现，已完成有限视口/内容延伸原生验收，更广输入、DPI 和兼容性仍待验证。Linux 平台检查与发布由 `[platforms.linux]` 的 `check = false`、`release = false` 关闭；工作流计划、汇总和文档仍可使用 Ubuntu。构建和签名成功不代表原生验收通过，也不代表已取得模拟器衍生几何数据的再分发权。保留[第三方声明](THIRD_PARTY_NOTICES.md)。

发布流水线生成自带 .NET 10 的 **macOS 27 arm64** 和 **Windows x64** 包。Windows 唯一依赖固定源码构建的 WebKit 宿主运行时，独立发布，再按 metadata 的精确版本/哈希下载并放入 Windows 应用归档的 `webkit/`。在应用旁 `webkit/` 下安装完整 `bin` 目录与清单，或设置绝对路径 `DEVDUO_WEBKIT_RUNTIME`；文件缺失或不兼容即明确报错。官方预编译测试运行时不能替代宿主 API v2，见 [Windows WebKit 初始化](014-WINDOWS-WEBKIT.md#avalonia-后端)。macOS 使用 GitHub 的 `xcode-27` 预览运行器，已对照[运行器清单](https://github.com/actions/runner-images#available-images)核实；运行器可用性是外部依赖。原有 `just package-macos` 仍生成未签名、依赖外部 .NET 的开发包。发布包保留 `local.DevDuo.Calibration` Bundle ID，直接使用原生应用启动器。

## CI 职责与资源预算

| 触发 / 任务 | 工作 | 上限与边界 |
| --- | --- | --- |
| Checks：`main`、PR、手动、可复用 | 单个 Ubuntu 跨平台任务执行 Python/JS、Core/Localization/CDP 服务测试、lint、typecheck、发布一致性和严格文档构建 | 30 分钟；跨平台初始化不还原应用后端 |
| Checks：原生矩阵 | metadata 启用的 macOS/Windows 编译应用、原生 bridge 和原生测试工具，macOS 另执行 AppKit 几何断言 | 各 30 分钟；不进行交互式浏览器验收、不用私人截图、不下载引擎、不编译 WebKit 源码 |
| 文档推送 / 手动 | `main`/`release` 相关路径构建部署输入；仅 `main` 部署 | 构建 30 分钟、部署 15 分钟；PR 文档已由 Checks 覆盖 |
| 应用 Release | 可复用 Checks 通过后才打包、签名 | macOS 120 分钟、Windows 60 分钟、组装/发布各 30 分钟 |
| 手动 WebKit 发布 | 持久自托管 Windows runner 增量编译完整引擎 | 编译步骤 22 小时、完整任务 23 小时；托管计划/发布任务不编译 WebKit |

仅**完整 PR 差异确认纯文档**时可跳过原生矩阵。比较范围未知时执行完整检查。每次 `main` 推送仍编译原生目标，因为前一次代码推送可能在通过前被取消。应用发布复用验证时始终强制原生编译。Checks 取消同 ref 的过时运行；文档也取消过时部署。全部任务检出不可变的触发 SHA，关闭 checkout 凭据持久化。只读 PR 任务不使用引擎 runner、签名环境或发布凭据。

应用与引擎发布各有一个全局串行组，设置 `queue: max`、`cancel-in-progress: false`。GitHub 最多允许 100 个等待任务，队列满后拒绝新运行；顺序按进入等待队列的时间，而非触发时间，见 [GitHub 并发语义](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency)。当前 actionlint 1.7.12 尚无新 `queue` schema；`.github/actionlint.yaml` 仅对这两份文件的精确错误提供兼容例外，同时由 YAML 测试验证完整并发配置。工具支持该语法后删除这些窄范围例外。

中间平台、引擎和文档预览 artifact 仅保留**一天**，最终应用 artifact 保留**七天**。ZIP 上传关闭重复压缩；主分支文档仅上传 Pages 所需 artifact。持久引擎缓存和 mise 工具安装留在自托管磁盘上。自托管任务不读写 GitHub Actions 缓存；GitHub 托管任务仍使用该缓存。短保留期降低存储占用时间，但不能保证首次大型运行时上传符合账户剩余额度；触发前检查实际用量，并通过仓库管理清理过期临时 artifact。Release 资产仍是长期运行时依赖。托管执行、存储和缓存有不同计费与限制，参阅当前 [Actions 计费规则](https://docs.github.com/en/billing/concepts/product-billing/github-actions)和[运行器/资源限制](https://docs.github.com/en/actions/reference/limits)，不能因为公开仓库执行免费就假定全部存储无限。

## Windows WebKit 运行时发布

应用 dev/build/package 命令执行 `just prepare-debugging`，将根 pnpm 锁定的精确 chii/chobitsu 浏览器资源及许可复制到 `temp/build/debugging/`，MSBuild 再将该树作为 `debugging/` 复制到应用输出/发布目录。安装后的应用通过 .NET 和原生 WebView 离线提供资源，不要求用户安装 Node，也不启动 chii Node 服务。每个包须保留生成清单及许可目录；资源缺失时启用 CDP 会明确失败。Windows 运行时包还需提供 `hostDeveloperApiVersion: 1` 和第三开发工具补丁，仅旧视口构建成功并不够。原生运行时验收与资源打包、CDP 传输测试分别记录。

`devduo-metadata.toml` 的 `[webkit]` 统一管理独立的引擎版本、tag/产物前缀、Windows 目标、DLL 标识、上游 revision、手动发布分支、runner 标签及消费端归档 SHA-256。当前 ABI 保留上游 `WebKit2.dll` 名称，ZIP 名称按 `<asset_prefix>-<version>-win-x64.zip` 派生。应用版本变化不会提升引擎版本。`runtime.lock.json` 继续固定官方诊断归档，并须与 metadata 的上游 revision 一致；更新上游时还须审查原生绑定及其要求的能力。

`.github/workflows/webkit-release.yml` **仅接受 `workflow_dispatch`**，并限定 metadata 配置的分支。计划在托管 runner 执行，编译使用配置标签对应的自托管 Windows/x64 runner。工作流文件须先进入默认分支，才能手动触发。现有基础设施配置已将原生 Windows 服务 runner 直接注册到本仓库，并使用持久工作目录。Metadata 选择其默认 `["self-hosted", "Windows", "X64"]` 标签，不依赖私人机器标签，也无需再次注册 runner。本仓库可用且匹配全部标签的 runner 均可接收任务；以后增加 runner 时，可通过 IaC 管理的能力标签收紧 `runner_labels`。[GitHub 默认标签匹配说明](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/use-in-a-workflow#using-default-labels-to-route-jobs)。

工作区应长期保留并使用稳定绝对路径。Windows 作业在检出前用 `git config --global` 同时打开 `core.symlinks` 和 `core.longpaths`，只写入该作业账号。runner 工作目录加上引擎树会超过 `MAX_PATH`，因此主机还须已启用 Windows 长路径，编译器才能创建这些路径。runner 账户须具备真实符号链接权限，安装 Git for Windows/Bash、Windows PowerShell、mise，以及系统级 Visual Studio C++/Clang 和 Windows SDK。当前服务配置使用 LocalSystem，依赖须对此服务账户可见，不能只安装在交互用户的配置中。便携引擎工具及 GitHub CLI 由 mise 管理。公开配置不记录私人服务器名。普通 PR 和应用发布不会触发引擎流水线或在这台 runner 上执行。

Checkout 使用 `clean: false`，在 checkout 可能重置文件前拒绝修改过的已跟踪输入，完成后再次检查工作区。任务之间不要对这个构建工作区执行 `just clean`。Ninja 输出保留在 `temp/build/windows-webkit-host/`；下载、源码 pin、vcpkg 二进制包和已应用补丁记录保留在 `temp/cache/windows-webkit/`。上游 revision、编译器哈希或 Windows SDK/VC 工具版本变化时，构建树在 `temp/build/windows-webkit-trees/` 下保存和恢复。缺少该身份记录的旧构建树另存保留，首次需要重新构建。补丁更新只撤销精确记录的旧补丁，不丢弃额外源码改动；遇到意外状态明确失败。历史构建树会占用磁盘，须明确清理，不承诺自动永久保存。编译步骤限制为 22 小时，整个任务限制为 23 小时，低于 [GitHub token 的 24 小时有效期](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idtimeout-minutes)。冷构建超时后，对同一持久工作区重跑可继续 Ninja 构建，不要先清理缓存。

缓存目录存在不等于源码 checkout 成功。首次 fetch/checkout 中断后，若 `HEAD` 尚未建立且没有索引文件记录，会复用已有 Git 对象继续初始化。重试普通 checkout 前，将残留工作文件（包括未知改动）完整移到旁边的 `<cache-name>.incomplete-<timestamp>-<id>`，须审查后再清理。内层源码和 vcpkg 仓库自行设置 Git 长路径与符号链接选项。已有提交但 revision/origin 不符、未完成缓存包含索引文件记录、Git 锁及非仓库目录仍明确失败，须手动处理或保留，不能通过 reset 或删除绕过。

构建任务在 Windows 上先执行 `test-webkit-release-tools`，再安装引擎工具并编译。2026-10-24 的 SSH 恢复已建立固定源码的 `HEAD`，完整保留 7,525 个残留文件并核对 SHA-256，两个已有对象包也均保留。维护补丁同步和固定 vcpkg 初始化连续验证通过，Windows 打包/构建树与源码恢复回归测试也已通过。这些检查不代表 CMake 完整配置、引擎完整编译或运行时发布通过。

发布顺序：

1. 设置新的 `[webkit].version`，保留预期源码 revision/补丁，未发布版本将 `archive_sha256` 留空。审查并将这些输入合并到配置分支。
2. 先以默认的 `dry-run=true` 手动运行 **WebKit runtime release**：演练构建并保留候选产物，不创建 tag 或 Release。随后在同一提交上以 `dry-run=false` 正式发布。候选哈希不等于已发布 pin。流水线构建完整运行时、检查必需导出、保留上游声明及源码输入，并上传同次运行的 Actions artifact。托管发布任务校验身份/哈希，创建草稿 Release，重新下载比较上传字节，最后以 `latest=false` 发布。已存在但不匹配的 tag/资产不覆盖。自托管编译任务只有仓库读取权限，发布凭据仅在托管任务中使用。
3. 将发布报告/任务摘要给出的实际 `archive_sha256` 写回 metadata，审查并合并该 pin。单独补充摘要不会改变引擎源码指纹。在填写前，应用发布计划及运行时下载明确失败，不伪造摘要或首次发布成功记录。
4. 普通 Windows 应用打包按精确 Release tag/资产下载，核对锁定归档摘要，再验证 revision、补丁/源码指纹、API 能力及全部运行时文件，最后放入 `DevDuo/webkit/`。匹配的本地下载缓存可以复用；不查找 `latest`，不自动编译引擎，不回退到会过期的 artifact 或官方测试运行时。

使用 `mise exec -- just check-webkit-release` 和 `test-webkit-release-tools` 检查策略与完整性；后者在 Windows 上还执行真实 ZIP 及合成持久缓存检查。`package-webkit-release` 打包匹配的原生构建；`fetch-windows-webkit-runtime` 下载并校验 metadata pin；`publish-webkit-release` 是流水线发布入口。`just release-preflight` 在配置检查后只读查询 GitHub：固定引擎必须已公开发布，资产集完整，源码 tag、补丁指纹、归档 pin 和校验报告一致。首次 pin 为空时会有意失败。Windows 消费端进一步检查实际归档及每个解压文件；下载或解压失败不会形成可复用的完整缓存。Release 资产作为长期依赖，Actions artifact 只在任务间传递。参考 [GitHub 自托管 runner 行为](https://docs.github.com/en/actions/concepts/runners/self-hosted-runners)及[工作流产物保留规则](https://docs.github.com/en/actions/tutorials/store-and-share-data)。

流水线不从 runner 服务中执行交互桌面验收。运行时清单保留明确的原生验收状态，导出、编译和哈希不代表渲染、输入或 DPI 通过。首次托管任务及后续 SSH 排查已确认 runner 可接收任务，并完成源码缓存准备；服务账户下的完整构建、GitHub 上传/下载及使用已发布 pin 的应用发布仍未验收。保留声明及源码来源本身不代表完成[第三方说明](THIRD_PARTY_NOTICES.md)要求的再分发审查。

## 版本和触发规则

[`devduo-metadata.toml`](../../devduo-metadata.toml) 统一管理版本、应用标识、URL 和发布策略。`just set-version VERSION` 同步 TOML、两个 Node 清单和原生项目。`just bump-version LEVEL` 支持 `major`、`minor`、`patch`、`alpha`、`beta`、`prerelease`、`stable`，预览版编号从零开始。例如 `0.1.0-dev` 经 `alpha` 变为 `0.1.0-alpha.0`，再经 `prerelease` 变为 `0.1.0-alpha.1`。

直接编辑 TOML 版本后执行 `just sync-version`。`just check-version` 检查版本一致性；`just release-check` 额外校验工作流的分支/tag 过滤器与元数据一致。修改版本不会发布或创建 tag，双语变更日志需另行编辑。

| 来源 | 版本 | 结果 |
| --- | --- | --- |
| `release` 分支 | `X.Y.Z-dev` | 明确跳过成功，不打包、不发布 |
| `release` 分支 | `X.Y.Z-alpha.N` / `X.Y.Z-beta.N` | 检查、构建、签名、生成来源证明并发布预览版 |
| 精确的 `vX.Y.Z[-alpha.N/-beta.N]` tag | 与包版本一致 | 检查并发布；正式版需要审批 |
| 其他分支或不匹配的 tag | 任意 | 在访问签名凭据前拒绝 |

推送到 `release` 或 `v*` tag 继续触发**自动正式发布**。手动触发默认 `dry-run=true`，仍执行验证、签名公证、组装、来源证明和 Actions 产物上传，但跳过创建 tag 和公开 Release；手动正式发布须明确设置 `dry-run=false`。演练选项不会抑制另一次推送触发的发布。手动触发使用所选 ref 及其不可变的 `github.sha`，不能指定无关源码 SHA。所有任务检出同一个触发提交。计划阶段先检查真实已发布的 WebKit 报告和 metadata pin。macOS 签名任务在构建前检查六个环境输入及 SDK；检查存在性不代表证书或 Apple 凭据有效。可复用 Checks 从元数据生成平台矩阵，在签名任务启动前重新执行跨平台测试、本地化测试、lint/typecheck、文档构建及 macOS/Windows 原生编译。发布串行执行，凭据不传给 PR 或可复用检查任务。

正式版需通过 `stable-release` 环境审批。预览版与正式版的 macOS 包均强制 Developer ID 签名和 Apple 公证；与参考项目的可配置未签名预览版不同，本项目没有未签名发布降级。签名启用安全时间戳、加固运行时和 .NET JIT 权限。先签全部内嵌 Mach-O，再签应用，然后公证、装订票据并通过 Gatekeeper 检查后压缩。参见 [Microsoft macOS 部署指南](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)。

## 产物和重试

计划阶段在运行时预检、编译或签名前查询精确的应用 tag。若该版本已指向另一提交，立即明确失败：递增版本，或重试原始源码/产物。仅文档改动推送到 `release` 也不能绕过版本身份或静默成功。同源重试仍需通过全部完整性门禁；恢复发布应复用原始 `release-final` artifact，不能替换已签名字节。

- `DevDuo-vVERSION-osx-arm64.zip`：已签名、公证的 `.app`，含 .NET 和声明文件。
- `DevDuo-vVERSION-win-x64.zip`：自带运行时的便携目录和声明文件；**没有 Authenticode 签名**。
- `release-manifest.json`：精确版本、源码 ref/SHA、预期文件、大小、SHA-256、平台签名状态，以及所带 Windows WebKit 的版本、tag、资产、归档哈希和上游 revision。
- `SHA256SUMS`：两份压缩包及清单的哈希。四个下载文件均生成 GitHub 构建来源证明；它不会让 Windows 显示“已验证的发布者”。

组装必须收到两个平台的报告和压缩包，且不能夹带其他文件。发布任务再次校验下载的产物集，创建或验证 tag，上传草稿，重新下载验证字节后才公开。已公开 Release 仅在全部文件相同时接受。恢复草稿时先下载并比较所有已存在资产，再仅上传同一原始产物集中缺失的文件，最后重新下载验证完整产物后才公开。字节、发布渠道或源码 tag 不同，出现额外资产，或已公开 Release 缺文件，均明确失败且不覆盖任何文件。中断后若留下未完成资产，须先检查草稿，工具不会自动删除；重试发布应使用原始 `release-final` artifact。重建签名包可能改变字节，因此重试不能静默替换已发布版本。相同内容的空操作重试不会重新提升为 latest，WebKit 始终使用 `latest=false`。两条流水线都有 `always()` 汇总任务，必需阶段失败或意外跳过时不能报告成功。哈希使用固定大小缓冲区，不把整个引擎归档读入内存。

发布暂存位于忽略的 `temp/build/release/`，最终包和报告位于 `temp/release/`。独立的 `packages.net10.0.osx-arm64.lock.json`、`packages.net10.0-windows.win-x64.lock.json` 锁定发布运行时依赖，不改变日常开发还原。需要主动更新时，使用 `dotnet restore`，分别传入 `-p:RuntimeIdentifier=osx-arm64` 或 `-p:RuntimeIdentifier=win-x64 -p:DevDuoWindows=true -p:EnableWindowsTargeting=true`，再加 `--force-evaluate`，审查依赖变化。

## 首次发布清单

当前应用与引擎候选版本均为 **0.1.0-beta.1**，发布门槛和未来版本更新仍各自独立。[首次托管 WebKit 运行](https://github.com/aitiotekt/DevDuo/actions/runs/40045146774)于 2026-10-24 到达构建步骤，但因不含 `HEAD` 的未完成源码缓存失败，整体运行随后被取消。这证明 workflow 已可用，不代表完整引擎构建通过。`[webkit].archive_sha256` 仍为空。发布规划正确地拒绝继续，包括 dry run；必须先实际发布并固定引擎。在配置的发布分支应用审查后的缓存恢复修复，再重试构建。

三个命名环境已存在，但当前 GitHub 凭据查询签名 secrets 和仓库 runner 时返回 HTTP 403；其就绪状态是**未核实**，不能据此认定缺失。此次托管构建失败已证明有 Windows runner 接收任务。本地准备和随后 SSH 排查均未触发 workflow、创建标签、上传或发布。[macOS 集成记录](015-PLATFORM-PARITY.md#beta-集成记录-2026-10-24)区分实际原生观察与平台缺口；构建通过不代表这些项目已验收。

1. 将工作流和完整原生源码、补丁输入放到默认分支；本次本地准备不会自动提交或推送。确认本仓库专用的 Windows runner 在线，服务账户可见 Git/Bash、mise、Visual Studio C++/Clang、Windows SDK，并具备符号链接权限。检查源码、依赖和归档构建树所需磁盘空间。
2. 核对 IaC 管理的 `release-signing`、`stable-release`、`github-pages` 环境和签名 secret 名称，不输出值。Runner/SDK 可用性和 secret 内容需要实际远端验收。
3. 执行 `mise exec -- just release-check` 和发布工具测试；先演练 WebKit 手动构建，再明确正式发布。以托管发布任务成功后的摘要为准，将归档 SHA-256 写入 metadata，不得只凭演练候选哈希设置 pin。
4. 使用已认证的 GitHub CLI 执行 `mise exec -- just release-preflight`。引擎发布与应用消费之间保持引擎源码、补丁和工具 pin 一致，否则发布新的引擎版本。维护双语变更日志，检查声明及再分发状态。
5. **推送编号版本到 `release` 之前完成上述准备**，因为该推送会自动正式发布。稳定版还要求精确版本 tag 和环境审批。可对已经符合条件的 ref 手动演练，但演练选项不会关闭另一次推送触发的正式发布。
6. 检查最终报告、两个平台 ZIP、`release-manifest.json`、`SHA256SUMS` 和来源证明。在干净目标机器分别验证解压、启动及固定引擎初始化；CI 编译和签名不能替代这些验收。Windows 仍需原生工具链对应的 Microsoft Visual C++ x64 运行库，.NET 自包含包不提供全部 C++ 依赖，见 [Microsoft 部署说明](https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp)。原生场景、输入及 DPI 验收仍独立记录。

本地检查不会触发工作流或发布 Release。首次云端运行、Apple 签名公证、干净机器启动和公开资产往返下载在实际执行前仍属未验收。

## GitHub 管理与文档

独立的 **aitiotekt-infra** 仓库通过 `meta/github-repositories.yml` 中的 DevDuo 条目管理默认只读工作流权限、`release` 分支保护、审批/签名/Pages 环境及 Pages 配置。Maintain/Admin 可绕过合并检查直接推送，但不能跳过发布工作流验证；独立安全规则禁止强推和删除。PR 合并要求 `Checks report` 状态。应用脚本不重复管理这些设置。

`release-signing` 环境仅接受 `release` 和 `v*` tag，需要 `APPLE_CERTIFICATE`、`APPLE_CERTIFICATE_PASSWORD`、`APPLE_SIGNING_IDENTITY`、`APPLE_ID`、`APPLE_PASSWORD` 和 `APPLE_TEAM_ID`。基础设施通过 SOPS 和 OpenBao 引用共享 Developer ID 证书，凭据不得进入本仓库。正式版审批允许唯一维护者自行审批，禁止管理员绕过。

文档配置为 **https://dev-duo.aitiotekt.com/**，VitePress base 为 `/`。文档安装锁定的 Python 审计工具，在打包 Pages artifact 前拒绝私人数据及发布输入违规，并在 `main`、`release` 和 PR 上构建；仅 `main` 通过 `github-pages` 环境部署。基础设施管理指向 `aitiotekt.github.io` 的仅 DNS CNAME 和 Pages 自定义域名。运行发布前需应用配置并配置环境密钥。GitHub 签发证书后再启用 Pages 强制 HTTPS。源码中的配置本身不证明远端已部署、已签发证书或已有 Release。

[English](../en/013-RELEASE.md) | [中文](013-RELEASE.md)
