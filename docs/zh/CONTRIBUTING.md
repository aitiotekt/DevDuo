# 开发与贡献指南

这是日常开发入口。[README](README.md)负责安装版的使用，本指南负责源码初始化、命令和贡献规范。按修改范围查阅专题即可，无需通读全部技术文档。编码代理还须遵守 [AGENTS.md](../../AGENTS.md)。

## 环境与初始化

安装 Git 与 mise。macOS 原生编译需要 Xcode 和 macOS 27 SDK/runtime；Windows 需要源码构建的 WebKit 宿主运行时。**所有贡献者均须支持真实的相对文件和目录符号链接**，Windows 也一样：使用支持的文件系统，启用开发者模式或等价权限，克隆前设置 `core.symlinks=true`。不支持复制品、junction 或普通文本替代链接。

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
mise exec -- just doctor
```

完成下述平台设置后，用 `mise exec -- just dev` 从源码启动。离线欢迎页无需接收服务；进行测量时，先在独立终端运行 `mise exec -- just serve`，再打开**本地标定页**，详见[标定指南](003-CALIBRATION-WORKFLOW.md)。

Windows 首次执行 `just dev` 或 `just test-windows` 前需构建原生运行时：

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
```

应用自动查找源码检出中的 `temp/build/windows-webkit-host/bin`。在检出目录外，可在应用旁提供 `webkit/bin` 与 `webkit/runtime-manifest.json`，或将 `DEVDUO_WEBKIT_RUNTIME` 设为匹配的绝对 bin 目录。这只选择运行时位置；浏览器实现固定为 WebKit。缺失或不兼容时明确报错，详见 [Windows WebKit 指南](014-WINDOWS-WEBKIT.md)。

这些命令也适用于 PowerShell。`doctor` 实际测试链接创建，同步操作拒绝覆盖普通文件。若链接被检出成文本，先保留改动，再使用正确的符号链接检出。参见 [Windows 开发者设置](https://learn.microsoft.com/en-us/windows/advanced-settings/developer-mode)与 [Git core.symlinks](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks)。

| 层 | 职责 |
| --- | --- |
| mise | .NET 10、Python 3.14、Node 26、pnpm 12 兼容系列及当前 uv/just/prek，仅选择工具与环境 |
| just | 仓库工作流、任务组合、原生构建与打包 |
| pnpm | JavaScript/TypeScript 工具和独立 VitePress 包 |
| uv | 锁定的 Python 环境与 `devduo-tools` 包 |

未激活 mise 时使用 `mise exec -- just <recipe>`。`global.json` 定义 SDK 基线及滚动策略，依赖锁保留精确包版本。Python 来自 mise，禁止 uv 下载解释器，依赖安装在 `.venv`。不使用全局 pip、系统 Python、`py -3` 或临时下载依赖。两个 pnpm 包在依赖缺失/过期时直接失败，不隐式安装。

## 源码组织

| 目录 | 职责 |
| --- | --- |
| `src/DevDuo/Host/`、`src/DevDuo/Browser/`、`src/DevDuo/DeveloperTools/`、`src/DevDuo/Device/` | 宿主 UI/偏好、共享浏览器生命周期/状态、调试服务及设备/场景模型 |
| `src/DevDuo/macos/`、`src/DevDuo/windows/` | 平台浏览器桥接和呈现，包括原生 Windows 合成器 |
| `src/DevDuo/WebAssets/Pages/`、`src/DevDuo/WebAssets/Scripts/` | 随应用复制的内置页面及运行脚本 |
| `native/webkit/windows/` | 生产引擎锁、宿主补丁、构建和归档支持 |
| `tests/` | 可移植检查、原生应用验收及独立引擎探针；测试页面归各自检查程序 |
| `scripts/` | 维护中的工具、打包及文档辅助代码；Python/Swift 指南与归档范围见下方链接 |

应用运行资源、测试夹具和原始证据分别保留；具体入口见[架构代码分工](001-ARCHITECTURE.md#代码分工)。

根目录 `tsconfig.json` 是 solution 文件，按职责引用各子项目：`tsconfig.scripts.json` 负责维护中的 Node 工具，`tsconfig.tests.json` 负责其 `node --test` 测试。两者继承 `tsconfig.base.json`，声明输出到 `temp/build/tsc/`。独立的 VitePress 包保留自己的 `docsite/tsconfig.json`。`pnpm typecheck` 构建该 solution，`just typecheck` 还会检查文档配置。

## 日常命令

下表假定已激活 mise。接收器和应用在不同终端运行。

| 命令 | 用途 |
| --- | --- |
| `just serve` / `just dev` | loopback 标定接收器 / 原生应用 |
| `just check` | 文档链接/双语配对、公开文件审查、Ruff 与 JS/Python/可移植 .NET 测试 |
| `just verify` | check 加 lint/类型检查、发布一致性、文档构建及当前平台编译 |
| `just test-core` | `DevDuo.Core.Tests`：偏好、URL/导航载荷、设备/场景不变量、标题和滚动策略 |
| `just test-localization` | `DevDuo.Localization.Tests`：资源、语言选择/持久化、页面就地翻译和几何保持 |
| `just build-windows` | Windows 编译，原生运行验收单独进行 |
| `just build-native-tests` | 仅编译当前平台的原生验收工具，不运行 UI；供 CI 使用 |
| `just test-macos` | AppKit 裁剪/resize/Fit、逻辑 bounds 和被动底栏颜色层回归，实际窗口另行验收 |
| `just references-check` | 已审查 Safari 参考图的尺寸、元数据与 48 项覆盖 |
| `just test-windows-safari` | 原始 Safari 夹具、外观、原生滚动和姿态定向检查；先运行 `just serve` |
| `just test-windows` | 原生 Avalonia WebKit 场景/生命周期/视口单位及缺失运行时检查，会打开 Windows 窗口 |
| `just test-windows-developer-tools` | 原生 Inspector/WIP、页面 CDP 生命周期及代理路由检查；需要重新构建包含开发者 API 的运行时，会打开 Windows 窗口 |
| `just test-windows-browser-identity` | 两种容器的首文档/子资源/fetch 原生 User-Agent、重定向及旋转保持检查；会打开 Windows 窗口 |
| `just test-windows-navigation-latency` | HTTP/内联冷启动、首次源像素、无外链导航计时及独立进程的 Network 崩溃恢复；会打开 Windows 窗口 |
| `just test-windows-presentation` | 检查 WebKit 物理窗口尺寸、重绘区域及最大化/Inspector/恢复后的实时像素，会打开 Windows 窗口 |
| `just test-windows-presentation-insets` | 单独检查工具栏收起后新露出的未经修改颜色页源像素；会打开 Windows 窗口，与最终 DWM 验收分开 |
| `just test-windows-presentation-rotations` | 检查八种姿态、弹窗关闭和快速旋转后的新文档动画帧；会打开 Windows 窗口，与完整固定遮罩历史分开 |
| `just test-windows-fixed-overlay` | 原生弹窗遮罩视口 alpha、inset 颜色及几何保持检查；会打开 Windows 窗口，不代表完整 Safari 光学验收 |
| `just test-windows-page-dialogs` | 真实 WebKit alert/confirm/prompt listener、本地化原生界面、系统键盘/UIA、DPI 及导航/销毁取消 |
| `just test-windows-accessibility` | 实际 Windows UIA 页面名称、值、动作、旋转边界及过期文档拒绝；需要 desktop API v1 |
| `just test-windows-popups` | 八种机身姿态下真实原生 select 锚点、鼠标／键盘选择及 Escape 关闭 |
| `just test-windows-ime` | 已安装中文文本服务在 input／textarea／contenteditable 中的预编辑、候选提交、取消及选区替换 |
| `just test-windows-screenshots` | 原生 REST/CDP 截图、八种姿态及滚动/fixed 几何保持；会打开 Windows 窗口 |
| `just test-windows-scroll-indicators` | 原生输入、淡出、CSS 抑制及屏幕外缘指示器像素与页面几何保持检查；会打开 Windows 窗口，不声明 Safari 精确线宽或淡出时长 |
| `just test-windows-run-loop` | 在外部 Windows 消息泵和 WTF 自有循环下检查实际固定版本 WTF 定时器；需要已构建的原生运行时 |
| `just test-debugging` | 真实回环 CDP HTTP/WebSocket 发现、token/Origin/生命周期检查，不证明原生页内注入 |
| `just prepare-debugging` | 将锁定的 chii/chobitsu 离线资源复制到 `temp/build/debugging/`，已纳入应用 dev/build/package 命令 |
| `just probe-windows-webkit` | 固定官方 WebKit 版本的独立实时延伸/文档边界诊断，会打开 Windows 窗口 |
| `just test-windows-webkit` | 普通 HTTP 页面、成对真实视口及顶部/侧边 inset 检查，会打开 Windows 窗口 |
| `just probe-windows-app` | 使用已审查回环页面进行 Avalonia WebKit 实际呈现检查 |
| `just package-macos` | 未签名、依赖运行时的 `temp/DevDuo.app` |
| `just package-local-windows` | 使用已有本地引擎生成自包含未签名 Windows 体验包，输出到 `temp/local-release/` |
| `just docs` / `just build-docs` | 文档开发服务 / 严格构建至 `temp/build/docsite/` |
| `just docs-sync` / `just docs-check` | 同步 / 检查相对源链接与双语覆盖 |
| `just format` / `just clean` | 格式化维护中的工具 / 清理声明的构建及缓存输出 |

Core、本地化和调试检查使用无外部测试框架依赖的 .NET 检查程序及已提交锁文件，不启动原生应用。Windows 应用场景位于 `tests/DevDuo.Windows.Tests/Checks/`，生命周期/服务支持位于 `Harness/`，截图/来源支持位于 `Evidence/`。构建该项目不会执行原生检查。可移植结果、编译和实际窗口验收应分别报告；跨平台交接见[平台对齐与macOS验收](015-PLATFORM-PARITY.md)。

Windows 增量工作采用[已验收里程碑与回归选择规则](009-WINDOWS-HANDOFF.md#windows-里程碑与增量回归)。未受影响的结果沿用基线，只在当前可用的单一实际 DPI 执行相关 recipe；系统缩放切换、全部原生 recipe 和三 DPI 矩阵都不是日常要求。`just verify` 仍是可移植检查／构建的集成入口，不会启动 Windows 原生验收套件。只有引擎输入发生变化才重建引擎，文档或托管宿主修改不需要重建 WebKit。

自定义参数直接传给对应生态，例如 `mise exec -- uv run --locked devduo-tools --help` 或 `mise exec -- pnpm --dir docsite dev --port 5174 --strictPort`。重新构建后需重启静态 preview。安装可选 hooks 使用 `mise exec -- prek install`。

构建和缓存放在 `temp/build/`、`temp/cache/`；.NET 还使用标准且被忽略的 `bin/`、`obj/`。`just clean` 保留应用包、依赖环境和原始证据。一次性验证副本使用系统临时目录，用完清理。原始截图与测量保存在各自的忽略目录。`.gitignore` 不会移除已有暂存项，发布前仍需检查差异。

必需的 Windows WebKit 源码构建通过 `mise.windows-webkit.toml` 及锁文件管理便携工具。先执行 `mise --env windows-webkit install`，再执行 `mise --env windows-webkit exec -- just build-windows-webkit-runtime`。Visual Studio C++/Clang 和 Windows SDK 仍为系统依赖；固定源码补丁与验收门槛见 [WebKit 指南](014-WINDOWS-WEBKIT.md#宿主-api-源码构建)。

Windows 引擎独立手动发布，专用自托管 runner 保留原生缓存；普通应用发布下载 metadata 指定版本的运行时。配置和命令见[运行时发布说明](013-RELEASE.md#windows-webkit-运行时发布)。

`just test-debugging` 使用真实回环 HTTP/WebSocket 检查 CDP 发现、token、Origin 及生命周期，不证明原生页内注入通过。`just prepare-debugging` 从锁定的 chii/chobitsu 复制离线资源到 `temp/build/debugging/`，应用 dev/build/package 命令均依赖该步骤；直接使用 `dotnet` 命令前先执行它，安装后用户无需 Node。

原生开发工具和代理需单独验收：独立 Inspector 不改变视口，原生远程连接/断开，真实 chii DOM/Console 交互，目标导航后的前端重连，监听释放，以及各平台代理路由。Windows 需包含第三宿主补丁及 `hostDeveloperApiVersion: 1` 的新运行时；macOS 使用系统 WebKit，当前原生验收状态见[平台对齐](015-PLATFORM-PARITY.md)。参见[开发工具架构](001-ARCHITECTURE.md#开发工具与页面代理)。

## 依赖与版本

日常 setup/check/build 使用冻结的 pnpm、uv 和分目标 NuGet 锁。有意升级时同步修改清单与相关锁：

- Python：编辑 `pyproject.toml`，运行 `mise exec -- uv lock` 与 `mise exec -- uv sync --locked --all-groups`。
- JS/文档：修改对应包后，运行 `mise exec -- pnpm install` 或 `mise exec -- pnpm --dir docsite install`。
- NuGet：有意识地更新相关目标，共享变更验证两个平台。

`devduo-metadata.toml` 统一管理版本、标识、URL 和发布策略。使用 `just set-version VERSION` 或 `just bump-version LEVEL`；直接修改元数据版本后运行 `just sync-version`，并通过 `just release-check`。签名、平台/runtime 锁、部署和仓库管理见[发布指南](013-RELEASE.md)。

## 编辑与评审

测试应执行行为并验证结果或失败状态。禁止扫描源码或工作流、任务、工具配置，断言命令写法、固定 job/runner 名称或偏好的接线方式；正则、token 匹配等命令快照变体也不允许。应运行函数、CLI、协议或原生操作来验证。配置 schema、生成产物和外部 API 契约通过可观察行为覆盖。此要求由评审落实，不再新增扫描测试来检查测试。

同步维护英中源文件、语言内链接和双向页脚。专题以当前行为／流程及长期有效裁决为主，逐次日志放在证据中，摘要指标只保留在当前验收基线。根目录英文规范文件是主源，`docs/en` 用相对符号链接引用，`docs/zh` 保存翻译；`docsite` 链接同一批源文件。增删技术页时同步 `scripts/docs-manifest.json`，不维护网站内容副本。

标识符与代码注释使用英语。评审说明改后行为、相关检查与未验证平台。原生变更需平台构建及真实 UI 验收；Windows 交叉编译不能替代运行。区分模拟器坐标和硬件尺寸，保持原始证据完整；修改标定数据前阅读[测量约定](002-MEASUREMENT-CONTRACT.md)。未经请求不 commit 或推送。

包含 Codex 协助内容的提交保留维护者为主要作者，在说明后空一行，添加一次以下共同作者标记。保留其他真实共同作者；纯人工改动无需这项署名。

```text
Co-authored-by: Codex <noreply@openai.com>
```

此身份关联 [Codex](https://github.com/codex) 账号。GitHub 按关联邮箱识别[共同作者标记](https://docs.github.com/en/pull-requests/how-tos/commit-changes/creating-a-commit-with-multiple-authors)，发布并进入默认分支的有效提交才会进入贡献者统计。添加这项约定不会修改 Git 作者配置、重写既有历史，也不代表获准提交或推送。

| 修改范围 | 按需阅读 |
| --- | --- |
| 原生浏览器组合、宿主布局 | [架构与容器](001-ARCHITECTURE.md) |
| 参考数据、采集和比较 | [测量约定](002-MEASUREMENT-CONTRACT.md)、[标定与验证](003-CALIBRATION-WORKFLOW.md)、[场景矩阵](005-SAFARI-SCENES.md) |
| 平台行为或应用文案 | [平台对齐与macOS验收](015-PLATFORM-PARITY.md)、[Windows 交接](009-WINDOWS-HANDOFF.md)、[本地化](011-LOCALIZATION.md) |
| 测量工具 | [Python 指南](../../scripts/python/README.md)、[Swift 指南](../../scripts/swift/README.md) |
| 历史复现 | [归档指南与审查索引](../../scripts/archive/README.md) |

公开文件不得含个人路径、机器或账户标识、局域网设备/VLAN 编号、实际网络地址、原始浏览记录、秘密或未审查证据。示例使用保留的文档地址，本地夹具使用回环地址。提交前执行 `just public-audit-index`，只读检查工作区文件及暂存文本，不修改暂存区。公开边界见[隐私](PRIVACY.md)、[安全](SECURITY.md)及[第三方说明](THIRD_PARTY_NOTICES.md)。

[English](../../CONTRIBUTING.md) | [中文](CONTRIBUTING.md)
