# 开发与贡献指南

这是日常开发入口。先按 [README](README.md) 运行应用，再按修改范围查阅专题，无需通读全部技术文档。

## 环境与初始化

安装 Git 与 mise。macOS 原生编译需要 Xcode 和 macOS 27 SDK/runtime；Windows 需要 WebView2 Runtime。**所有贡献者均须支持真实的相对文件和目录符号链接**，Windows 也一样：使用支持的文件系统，启用开发者模式或等价权限，克隆前设置 `core.symlinks=true`。不支持复制品、junction 或普通文本替代链接。

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
mise exec -- just doctor
```

这些命令也适用于 PowerShell。`doctor` 实际测试链接创建，同步操作拒绝覆盖普通文件。若链接被检出成文本，先保留改动，再使用正确的符号链接检出。参见 [Windows 开发者设置](https://learn.microsoft.com/en-us/windows/advanced-settings/developer-mode)与 [Git core.symlinks](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks)。

| 层 | 职责 |
| --- | --- |
| mise | .NET 10、Python 3.14、Node 26、pnpm 12 兼容系列及当前 uv/just/prek，仅选择工具与环境 |
| just | 仓库工作流、任务组合、原生构建与打包 |
| pnpm | JavaScript/TypeScript 工具和独立 VitePress 包 |
| uv | 锁定的 Python 环境与 `devduo-tools` 包 |

未激活 mise 时使用 `mise exec -- just <recipe>`。`global.json` 定义 SDK 基线及滚动策略，依赖锁保留精确包版本。Python 来自 mise，禁止 uv 下载解释器，依赖安装在 `.venv`。不使用全局 pip、系统 Python、`py -3` 或临时下载依赖。两个 pnpm 包在依赖缺失/过期时直接失败，不隐式安装。

## 日常命令

下表假定已激活 mise。接收器和应用在不同终端运行。

| 命令 | 用途 |
| --- | --- |
| `just serve` / `just dev` | loopback 标定接收器 / 原生应用 |
| `just check` | 文档链接/双语配对、公开文件审查、Ruff 与 JS/Python/本地化测试 |
| `just verify` | check 加 lint/类型检查、发布一致性、文档构建及当前平台编译 |
| `just build-windows` | Windows 编译，原生运行验收单独进行 |
| `just package-macos` | 未签名、依赖运行时的 `temp/DevDuo.app` |
| `just docs` / `just build-docs` | 文档开发服务 / 严格构建至 `temp/build/docsite/` |
| `just docs-sync` / `just docs-check` | 同步 / 检查相对源链接与双语覆盖 |
| `just format` / `just clean` | 格式化维护中的工具 / 清理声明的构建及缓存输出 |

自定义参数直接传给对应生态，例如 `mise exec -- uv run --locked devduo-tools --help` 或 `mise exec -- pnpm --dir docsite dev --port 5174 --strictPort`。重新构建后需重启静态 preview。安装可选 hooks 使用 `mise exec -- prek install`。

构建和缓存放在 `temp/build/`、`temp/cache/`；.NET 还使用标准且被忽略的 `bin/`、`obj/`。`just clean` 保留应用包、依赖环境和原始证据。一次性验证副本使用系统临时目录，用完清理。原始截图与测量保存在各自的忽略目录。`.gitignore` 不会移除已有暂存项，发布前仍需检查差异。

## 依赖与版本

日常 setup/check/build 使用冻结的 pnpm、uv 和分目标 NuGet 锁。有意升级时同步修改清单与相关锁：

- Python：编辑 `pyproject.toml`，运行 `mise exec -- uv lock` 与 `mise exec -- uv sync --locked --all-groups`。
- JS/文档：修改对应包后，运行 `mise exec -- pnpm install` 或 `mise exec -- pnpm --dir docsite install`。
- NuGet：有意识地更新相关目标，共享变更验证两个平台。

`devduo-metadata.toml` 统一管理版本、标识、URL 和发布策略。使用 `just set-version VERSION` 或 `just bump-version LEVEL`；直接修改元数据版本后运行 `just sync-version`，并通过 `just release-check`。签名、平台/runtime 锁、部署和仓库管理见[发布指南](013-RELEASE.md)。

## 编辑与评审

同步维护英中源文件、语言内链接和双向页脚。根目录英文规范文件是主源，`docs/en` 用相对符号链接引用，`docs/zh` 保存翻译；`docsite` 链接同一批源文件。增删技术页时同步 `scripts/docs-manifest.json`，不维护网站内容副本。

标识符与代码注释使用英语。评审说明改后行为、相关检查与未验证平台。原生变更需平台构建及真实 UI 验收；Windows 交叉编译不能替代运行。区分模拟器坐标和硬件尺寸，保留证据哈希；修改标定数据前阅读[测量约定](002-MEASUREMENT-CONTRACT.md)。未经请求不 commit 或推送。

| 修改范围 | 按需阅读 |
| --- | --- |
| 原生浏览器组合、宿主布局 | [架构与容器](001-ARCHITECTURE.md) |
| 参考数据、采集和比较 | [测量约定](002-MEASUREMENT-CONTRACT.md)、[标定与验证](003-CALIBRATION-WORKFLOW.md)、[场景矩阵](005-SAFARI-SCENES.md) |
| Windows 行为或应用文案 | [Windows 交接](009-WINDOWS-HANDOFF.md)、[本地化](011-LOCALIZATION.md) |
| 测量工具 | [Python 指南](../../scripts/python/README.md)、[Swift 指南](../../scripts/swift/README.md) |
| 历史复现 | [归档指南与审查索引](../../scripts/archive/README.md) |

公开文件不得含个人路径、机器标识、原始浏览记录、秘密或未审查证据。公开边界见[隐私](PRIVACY.md)、[安全](SECURITY.md)及[第三方说明](THIRD_PARTY_NOTICES.md)。

[English](../../CONTRIBUTING.md) | [中文](CONTRIBUTING.md)
