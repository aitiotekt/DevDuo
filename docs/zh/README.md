# DevDuo

Pesudo iPhone Duo Viewport Simulator for Web Developers

DevDuo 是面向 Web 开发者的实验性桌面视口调试工具，依据 iPhone Duo 模拟器实测视口检查布局。Avalonia 在 macOS 上承载系统 WKWebView，在 Windows 上承载 Microsoft Edge WebView2，可选择页面衍生颜色的悬浮 Safari 外壳或沉浸式 App WebView。

浏览器仍是实际桌面引擎。Safari 光学外观属于近似；物理毫米、真实硬件圆角和 iOS Liquid Glass 内核仍未知。本项目独立于 Apple。

## 快速开始

安装 Git 和 [mise](https://mise.jdx.dev/installing-mise.html)。macOS 原生编译需要 Xcode 与 macOS 27 SDK/runtime；Windows 需要 WebView2 Runtime。**所有贡献平台均须支持真实文件和目录符号链接，包括 Windows。** 初始化与权限说明统一见[开发与贡献指南](CONTRIBUTING.md)。

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
```

分别在两个终端运行，同样适用于 PowerShell：

```sh
mise exec -- just serve
mise exec -- just dev
```

选择 Safari/沉浸、折叠/展开，使用**机身 +90°** 切换角度。**多标签页栏**表示展开外壳变体，不管理真实标签。macOS Fit 保留 CSS 尺寸并缩放呈现。应用支持持久化的 English、简体中文和系统默认语言选择。

**保存状态**在源码检出中写入忽略的 `captures/desktop-validation/container-modes/`，独立安装使用本地应用数据的 `DevDuo/captures`。`DEVDUO_OUTPUT_DIR` 修改该位置，`DEVDUO_URL` 修改初始网址。接收器原始测量与桌面复现记录独立保留。

## 当前状态

- macOS 已有 macOS 27 WKWebView 原生交互基线。Windows 交叉编译通过，输入、DPI、zoom Fit 和外观仍待原生验收；Linux 应用检查/发布在元数据中禁用。
- 参考覆盖折叠/展开 × 四机身角度 × 初始/滚动，展开另含单/多标签。屏幕遮罩与外壳路径独立，部分展开几何尚不可用。
- 已检入配置无需私有 Safari 截图或 Apple PDF 即可运行。Device Hub 新采集需要已安装的模拟器 runtime，重新提取参考需要原始证据。
- 宿主控件已可用，真实地址栏、导航绑定和视觉改进仍在[路线图](100-ROADMAP.md)中。

## 开发与阅读入口

**日常开发先读本 README 和[贡献指南](CONTRIBUTING.md)即可。** 环境、命令、锁文件、清理和文档规范都集中在该指南；其它内容按修改范围查阅：

| 任务 | 按需阅读 |
| --- | --- |
| 修改浏览器组合/布局 | [架构与容器](001-ARCHITECTURE.md) |
| 修改实测数据或采集观测 | [测量约定](002-MEASUREMENT-CONTRACT.md)、[标定与验证](003-CALIBRATION-WORKFLOW.md)、[场景矩阵](005-SAFARI-SCENES.md) |
| 平台或语言工作 | [Windows 交接](009-WINDOWS-HANDOFF.md)、[本地化](011-LOCALIZATION.md) |
| 维护分发与基础设施 | [发布与仓库管理](013-RELEASE.md) |

根目录保存英文规范源文件，`docs/zh` 保存对应中文，`docs/en` / `docs/zh` 保存编号技术文档。独立 VitePress 工作区通过相对符号链接引用这些源；配置站点为 [dev-duo.aitiotekt.com](https://dev-duo.aitiotekt.com/)，基础设施就绪后仅由 `main` 部署，不维护网站内容副本。

## 证据与分发

原始截图、浏览记录和私人笔记保留在本地，保持字节/哈希不变；公开衍生物另行脱敏审查。第一方代码使用 [MIT](../../LICENSE)，模拟器图形再分发仍有[第三方说明](THIRD_PARTY_NOTICES.md)中的未确认事项。开发包未签名；正式签名和平台策略由 `devduo-metadata.toml` 与发布指南管理，不捆绑 WebView2 Runtime。

[安全](SECURITY.md) · [隐私](PRIVACY.md) · [变更日志](CHANGELOG.md)

[English](../../README.md) | [中文](README.md)
