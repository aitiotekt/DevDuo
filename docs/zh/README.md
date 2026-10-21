# DevDuo

Pesudo iPhone Duo Viewport Simulator for Web Developers

DevDuo 是面向 Web 开发者的实验性桌面视口调试工具。它使用 Avalonia，在 macOS 上承载系统 WKWebView，在 Windows 上承载 Microsoft Edge WebView2。Safari 模式呈现依据模拟器测量数据绘制的悬浮组件及页面衍生颜色；沉浸式 App WebView 模式使用完整逻辑屏幕。

浏览器仍是实际桌面引擎。Safari 光学外观属于近似；物理毫米、真实硬件圆角和 iOS Liquid Glass 内核仍未知。本项目独立于 Apple。

## 当前状态

| 范围 | 状态 |
| --- | --- |
| macOS | 已有 macOS 27 系统 WKWebView 原生构建和交互验证基准 |
| Windows | 已交叉编译；原生运行、输入、DPI 和材质外观验收待完成 |
| 场景 | 折叠/展开 × 四个机身旋转角度 × 初始/滚动态；展开支持单标签页和多标签页外壳 |
| 几何 | 屏幕遮罩与外壳独立；使用模拟器坐标，不推导硬件毫米 |
| 宿主 UI | 已有基础标定控件；真实地址栏、导航状态及视觉改进仍在路线图中 |

## 环境要求

安装 Git 和 [mise](https://mise.jdx.dev/installing-mise.html)。**所有贡献者都必须支持真实的文件和目录符号链接，包括 Windows。** Windows 需要相应开发权限及支持链接的文件系统，并在克隆时设置 `core.symlinks=true`。复制件、junction 和内容为链接目标的普通文本文件均不受支持，详见[贡献指南](CONTRIBUTING.md)。

`mise.toml` 选择兼容的 Python 3.14、Node 26、pnpm 12、.NET 10 系列，uv、just、prek 使用当前版本。`global.json` 定义最低 SDK 及特性带滚动策略。Python 依赖由 `uv.lock` 锁定，安装到项目虚拟环境；uv 不得另行下载解释器。无需系统 Python、全局 pip 包或 Windows Python launcher。

macOS 原生构建需要 Xcode 与 macOS 27 SDK/运行环境；只有采集新 Safari 参考数据才需要 Device Hub 及 iOS runtime。Windows 需要 WebView2 Runtime。Linux 支持可移植工具和文档站，尚无 Linux 原生浏览器宿主。

## 安装和运行

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
```

分别在两个终端运行，PowerShell 使用同样的命令：

```sh
mise exec -- just serve
mise exec -- just dev
```

just 负责仓库工作流，mise 选择工具，pnpm 负责 JavaScript/文档命令，uv 负责维护中的 Python 包。工具自定义参数直接交给对应生态入口：

```sh
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output captures/comparison
```

App 支持 **English**、**简体中文**和系统默认，并保存语言偏好。切换语言保留当前页面和测量视口，详见[本地化](011-LOCALIZATION.md)。

选择 Safari/沉浸模式、折叠/展开，并使用 **Body +90°** 切换机身角度。**多标签页栏** 开关表示展开态 Safari 外壳的单/多标签页变体，并不管理真实标签页。macOS Fit 预览保留 CSS 视口并缩放显示；Windows Fit 仍需原生验证。

**Save state** 在源码检出环境下写入被忽略的 `captures/desktop-validation/container-modes/`；独立安装的应用使用当前用户本地应用数据中的 `DevDuo/captures`。`DEVDUO_OUTPUT_DIR` 可覆盖输出目录，`DEVDUO_URL` 可覆盖初始页面。接收器测量与桌面复现记录分别保存。

## 开发命令

| 命令 | 用途 |
| --- | --- |
| `mise exec -- just doctor` | 检查托管工具及必需的符号链接权限 |
| `mise exec -- just check` | 文档链接/语言配对、公开文件、Python 工具和 CLI 回归检查 |
| `mise exec -- just verify` | 另加工具 lint/类型检查、文档站构建和当前平台原生编译 |
| `mise exec -- just build-windows` | Windows 编译，不代表原生运行通过 |
| `mise exec -- just package-macos` | 生成未签名、依赖 .NET 运行时的 `temp/DevDuo.app` |
| `mise exec -- just docs` | 本地预览双语文档站 |
| `mise exec -- just build-docs` | 严格检查内部链接并构建 VitePress |
| `mise exec -- just docs-sync` | 同步文档的相对符号链接 |

`pnpm-lock.yaml`、`docsite/pnpm-lock.yaml`、`uv.lock` 和各目标 NuGet lock file 共同约束依赖。日常安装及检查使用 frozen/locked 操作。升级方法、可选工具和参数转发见[开发工具链](010-DEVELOPMENT.md)。

## 文档结构

根目录约定文档是英文源文件，并链接到 `docs/en`；中文约定文档及技术译文位于 `docs/zh`；英文编号技术文档位于 `docs/en`。独立的 `docsite/` VitePress 工作区通过相对符号链接复用这些源文件，提供英文/中文导航、本地搜索和 `/DevDuo/` 基础路径。没有独立手工维护的网站副本，也没有配置生产部署。

- [项目概览](000-OVERVIEW.md) · [架构](001-ARCHITECTURE.md)
- [测量约定](002-MEASUREMENT-CONTRACT.md) · [标定流程](003-CALIBRATION-WORKFLOW.md)
- [Safari 场景](005-SAFARI-SCENES.md) · [验证记录](008-VALIDATION.md)
- [开发工具链](010-DEVELOPMENT.md) · [本地化](011-LOCALIZATION.md) · [脚本清单](012-SCRIPT-INVENTORY.md)
- [Windows 交接](009-WINDOWS-HANDOFF.md) · [宿主 UI 路线图](100-ROADMAP.md)
- [安全](SECURITY.md) · [隐私](PRIVACY.md) · [贡献](CONTRIBUTING.md) · [变更日志](CHANGELOG.md)

## 项目目录

| 路径 | 职责 |
| --- | --- |
| `src/DevDuo/` | Avalonia 宿主、共享场景、macOS WKWebView 桥接、Windows WebView2 及夹具 |
| `config/` | 可移植模拟器几何、Safari 场景及明确的未知值 |
| `web/` | 标定页面 |
| `scripts/python/`、`scripts/swift/` | 维护中的测量包与 macOS 视频诊断 |
| `scripts/DeviceHubUITests/` | 采集真实 Safari 帧的 Device Hub XCUITest 探针 |
| `scripts/archive/` | 已审查的历史会话工具和实验 |
| `scripts/`、`justfiles/` | JS/文档辅助程序与仓库任务配方 |
| `docs/en/`、`docs/zh/`、`docsite/` | 双语文档源文件与链接后的 VitePress 工作区 |
| `captures/`、`measurements/`、`.local/` | 被忽略的原始证据与私人笔记 |
| `temp/`、`.venv/`、`node_modules/` | 被忽略的生成输出、缓存与已安装依赖 |

## 证据与分发

检入的配置足以运行应用。Safari 原始截图、浏览记录和 Apple PDF 属于本地证据，不进入 Git。重新提取需要明确提供本地输入，缺失证据不得被伪造成验证通过。PDF 遮罩检查仍可选依赖外部 Poppler；接触表使用已锁定的 Pillow 运行时依赖。

第一方代码使用 [MIT 许可](../../LICENSE)。模拟器衍生路径数据的再分发授权尚未确认，另见[第三方说明](THIRD_PARTY_NOTICES.md)。尚无签名发行版，也未捆绑 WebView2 Runtime。

[English](../../README.md) | [中文](README.md)
