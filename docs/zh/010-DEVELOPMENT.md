# 开发工具链

## 环境与职责

安装 Git 与 mise，然后运行：

```sh
mise install
mise exec -- just setup
mise exec -- just doctor
```

`mise.toml` 选择兼容系列：.NET 10、Python 3.14、Node 26、pnpm 12；uv、just、prek 使用 `latest`。这些选择器允许复用兼容的已安装工具，不要求偶然使用的某个补丁版。`global.json` 定义 .NET SDK 基线与特性带滚动策略。依赖锁文件仍然记录精确的依赖版本。Xcode、WebView2 Runtime 和 Git 是平台或引导前提。

| 层 | 负责范围 |
| --- | --- |
| mise | 仅选择工具和环境，不维护任务别名 |
| just / `justfiles/` | 仓库工作流、任务组合、原生构建与打包 |
| pnpm | JavaScript/TypeScript 工具与 VitePress 命令 |
| uv | Python 环境、依赖与 `devduo-tools` |
| SwiftPM / Xcode | macOS 诊断程序与原生平台能力 |

以 `just` 为主命令驱动器；已激活 mise 的 shell 可以省略 `mise exec --`。不要在四层各维护一套相同的 setup/check/build 工作流。自定义 Python 参数直接传给 Python CLI，自定义 VitePress 参数直接传给对应 Node 包。

## Python 与诊断参数

Python 由 mise 选择，uv 创建仓库 `.venv`，禁止自动下载解释器。不使用全局 pip、系统 Python 或另一份 uv 下载的解释器。维护中的工具集中在可安装包 `scripts/python/devduo_tools`，各命令独立解析参数：

```sh
mise exec -- uv run --locked devduo-tools --help
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output captures/comparison
mise exec -- uv run --locked devduo-tools serve --host 127.0.0.1 --port 8765 --output measurements/session-local
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

Pillow 是锁定的运行时依赖，用于图片解码和对比图；Ruff 属于开发组。便携测试覆盖服务持久化/并发、图像解码与差异、Unicode 路径、错误状态、CLI 和公开配置。逐个脚本的审查、现用命令与历史边界见[脚本清单](012-SCRIPT-INVENTORY.md)。

macOS 视频诊断使用 `swift run --package-path scripts/swift --scratch-path temp/build/swift-diagnostics devduo-inspect-video VIDEO NEW_OUTPUT_DIRECTORY`。Poppler 仅用于本地 PDF mask 比较。Safari 采集需要 Xcode/Device Hub 和已安装的模拟器运行时，便携检查不需要。

## 主命令

| 命令（在 mise 环境中） | 行为 |
| --- | --- |
| `just setup` | 检查工具/符号链接、锁定安装 Python/JS 依赖并恢复 NuGet |
| `just serve` / `just dev` | loopback 标定接收器 / 当前平台应用 |
| `just check` | 文档配对/链接、公开文件审查、Ruff、JS/Python/本地化测试 |
| `just verify` | check 加 JS lint/类型检查、严格文档构建、当前平台原生编译 |
| `just test-localization` | 资源、偏好、格式与场景不变性测试 |
| `just build-windows` | Windows 交叉编译，原生运行验收单独进行 |
| `just package-macos` | 未签名开发包，需要 .NET 10 运行时 |
| `just docs` / `just docs-preview` | 文档开发服务 / 上次构建的预览 |
| `just docs-sync` / `just docs-check` | 同步 / 验证规范源相对符号链接与双语清单 |
| `just build-docs` | 严格构建至被忽略的 `temp/build/docsite/` |
| `just set-version VERSION` / `just bump-version LEVEL` | 同步元数据和各清单；指定完整版本或按 SemVer 递增 |
| `just sync-version` / `just check-version` | 从 `devduo-metadata.toml` 同步版本 / 检查版本一致性 |
| `just format` | 格式化维护中的 JS/TS 与 Python，完成后检查差异 |
| `just clean` | 清理声明的构建输出和缓存，保留证据、已安装依赖及打包应用 |

自定义文档端口使用 `mise exec -- pnpm --dir docsite dev --port 5174 --strictPort`。重新构建后请重启已运行的 preview，使静态资源清单与新输出一致。安装可选 hooks 需显式运行 `mise exec -- prek install`。

两个 pnpm 工作区均配置 `verifyDepsBeforeRun: error`：依赖缺失或过期时执行命令直接失败，不隐式安装或改锁文件。`doctor` 在依赖安装前直接运行仅依赖标准库的 Node 辅助程序。安装由显式的冻结 setup 配方负责。

## 生成文件与清理

生成输出统一放在被忽略的 `temp/`：应用包为 `temp/DevDuo.app`，诊断构建在 `temp/build/`，Ruff/VitePress/Swift 缓存在 `temp/cache/`。.NET 保留标准且被忽略的 `bin/`、`obj/` 中间目录。`just clean` 清理声明的构建/缓存路径及旧缓存位置，保留原始证据、依赖环境与应用包。

一次性验证副本使用系统临时目录，完成后删除。`.gitignore` 排除临时目录、缓存、日志和编辑器/补丁备份，源码、测试和依赖锁文件正常纳入版本控制。忽略规则不会移除已跟踪文件，提交前需检查暂存区。原始测量放在 `captures/` 或 `measurements/`，与可清理构建输出分开。

## 锁文件与升级

日常 setup/check/build 不更新依赖锁。有意修改 Python 依赖时，编辑 `pyproject.toml`，运行 `mise exec -- uv lock`，然后 `mise exec -- uv sync --locked --all-groups`，审查锁文件差异并重新检查。Python 支持范围应有兼容性依据。

修改根 `package.json` 后运行 `mise exec -- pnpm install`；独立文档包使用 `mise exec -- pnpm --dir docsite install`。一起审查相关清单和锁文件。NuGet 的 macOS/Windows 锁分开维护，共享变更验证两个目标。不要增加会绕过 mise 兼容系列选择的精确包管理器自引导版本。

## 文档与 Windows 前提

根目录英文规范文档是主源。`docs/en` 包含其符号链接别名和编号技术文档；`docs/zh` 包含对应中文源。`docsite` 直接链接这些源，不复制内容。同步更新两种语言、清单与语言切换页脚。

所有贡献者都必须支持真实的相对文件和目录符号链接，Windows 也一样。克隆前启用适当权限并配置 `core.symlinks=true`。`doctor` 实际测试链接创建，同步过程拒绝覆盖普通文件。参见[贡献指南](CONTRIBUTING.md)与 [Windows 交接](009-WINDOWS-HANDOFF.md)。

发布打包、版本命令、签名要求和独立运行时锁文件见[发布说明](013-RELEASE.md)。

[English](../en/010-DEVELOPMENT.md) | [中文](010-DEVELOPMENT.md)
