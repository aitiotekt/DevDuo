# DevDuo

Pesudo iPhone Duo Viewport Simulator for Web Developers

DevDuo 是面向 Web 开发者的实验性桌面视口调试工具，依据 iPhone Duo 模拟器实测视口检查布局。Avalonia 在 macOS 上承载系统 WKWebView，在 Windows 上承载 固定的 Windows WebKit Port，可选择页面衍生颜色的悬浮 Safari 外壳或沉浸式 App WebView。

浏览器仍是实际桌面引擎。Safari 光学外观属于近似；物理毫米、真实硬件圆角和 iOS Liquid Glass 内核仍未知。本项目独立于 Apple。

## 快速开始

安装 Git 和 [mise](https://mise.jdx.dev/installing-mise.html)。macOS 原生编译需要 Xcode 与 macOS 27 SDK/runtime；Windows 需要源码构建的 WebKit 宿主运行时。**所有贡献平台均须支持真实文件和目录符号链接，包括 Windows。** 初始化与权限说明统一见[开发与贡献指南](CONTRIBUTING.md)。

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
mise install
mise exec -- just setup
```

Windows 首次执行 `just dev` 或 `just test-windows` 前需构建原生运行时：

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
```

应用自动查找源码检出中的 `temp/build/windows-webkit-host/bin`。在检出目录外，可在应用旁提供 `webkit/bin` 与 `webkit/runtime-manifest.json`，或将 `DEVDUO_WEBKIT_RUNTIME` 设为匹配的绝对 bin 目录。这只选择运行时位置；浏览器实现固定为 WebKit。缺失或不兼容时明确报错，详见 [Windows WebKit 指南](014-WINDOWS-WEBKIT.md)。

启动应用，同样适用于 PowerShell：

```sh
mise exec -- just dev
```

没有显式启动地址或记住的网站时，应用打开内置离线欢迎页，提供仓库和文档链接；**主页**可随时返回。日常使用不需要标定服务。**开发工具**位于设置侧栏最顶部，每次启动默认折叠。**颜色演示**、**标定页**及其服务提示放在该分组末尾，排在 Inspector、远程检查、CDP 和代理控件之后。标定需要在独立终端运行 `mise exec -- just serve`，且只有该页面会附加测量标签。

在地址栏打开任意 http/https 页面：按 Enter 导航，后退/前进、重新加载/停止、加载进度以及带**重试**的失败状态均跟随当前原生浏览器。设置侧栏分为设备（形态、**机身 +90°**）、容器（Safari/沉浸、**多标签页栏**）、预览（1:1 或适应、外框）和外观（语言、浅色/深色主题）；材质调节和诊断可折叠。**多标签页栏**表示展开外壳变体，固定显示两个示意标签，不管理真实标签。活动槽位显示当前文档真实标题，在实测文字框内使用原生尾部省略。适应预览按可用预览区域缩放呈现，并保留 CSS 尺寸。

除机身角度和开发工具展开状态外的宿主设置会在启动之间保留；材质和诊断的展开状态仍会保存。手动展开开发工具在当前窗口会话内保留，切换语言也不会折叠。**重置设置**恢复 Safari · 折叠 · 机身 0° · 1:1。快捷键：⌘L/Ctrl+L 地址栏，⌘R/Ctrl+R 或 F5 重新加载，⌘[ ⌘] 或 Alt+←/→ 历史，⌘S/Ctrl+S 保存状态，⌘\\/Ctrl+\\ 设置。应用支持持久化的 English、简体中文和系统默认语言选择。

Windows 网站数据（包括 localStorage、IndexedDB、持久 Cookie 和缓存）保存在本地应用数据的 `DevDuo/WebKit`。旋转保留文档、表单状态和历史。侧栏的 **打开 Web Inspector** 按钮、**F12** 和 **Ctrl+Shift+I** 可开关独立原生 Inspector 窗口。`mise exec -- just package-local-windows` 使用已有本地 WebKit 生成自包含未签名体验包，输出到 `temp/local-release/`。

页面在首次请求前设置观察到的 iPhone Safari User-Agent，HTTP 请求头与原生 `navigator.userAgent` 保持一致。两种容器共用此移动网页兼容身份，真实引擎、platform 和 DPR 保持原生值。详见[身份来源与限制](014-WINDOWS-WEBKIT.md#移动页面请求身份)。

**保存状态**在源码检出中写入忽略的 `captures/desktop-validation/container-modes/`，独立安装使用本地应用数据的 `DevDuo/captures`。`DEVDUO_OUTPUT_DIR` 修改该位置，`DEVDUO_URL` 覆盖记住的初始网址。接收器原始测量与桌面复现记录独立保留。

**开发工具**分组分别提供原生 Web Inspector、原生远程检查、**Chrome DevTools / CDP** 和页面代理。两项远程服务每次启动均关闭；启用后默认只监听回环，勾选**允许局域网访问**才开放其它接口。Windows 原生远程检查使用 WebKit Inspector TCP 协议，macOS 通过 Safari 的开发菜单发现页面，两者均独立于 CDP。CDP 向当前页及后续文档注入固定版本的 chobitsu，并在独立系统 WebView 窗口打开包内 chii 前端。复制的 token 地址还提供相对于该地址的 `/json/list`、`/json/version` 和页面 WebSocket 端点。它只实现 JavaScript CDP 子集，不具备引擎断点、性能分析、完整网络覆盖或浏览器级目标。目标页导航后仅刷新调试面板以重新连接。关闭 CDP 会释放监听并移除后续注入；当前页的 hook 需自行刷新该页后才会消失。调试资源随包离线提供，用户无需安装 Node。

代理作用于页面的原生网络存储：Windows 支持系统、直连和自定义，macOS 支持系统与自定义，直连会明确报告不支持。自定义端点使用带端口且无认证的 `http`、`https` 或 `socks5`。应用代理时保留当前文档，后续请求采用新配置。监听偏好和代理配置会保存，服务启用状态及访问 token 不会保存。macOS 桥接改动仍需在 macOS 原生验收。详见[架构](001-ARCHITECTURE.md#开发工具与页面代理)和[隐私](PRIVACY.md)。

## 当前状态

- macOS 使用系统 WKWebView；Windows 唯一使用 [Windows WebKit 后端](014-WINDOWS-WEBKIT.md#avalonia-后端)，通过非测试宿主 API 实现真实四边内容延伸，并使用实时 DirectComposition 玻璃材质。原生检查覆盖 24 个 Safari 状态及 152 组独立页面比较；完整 DPI、本地/远程桌面呈现仍待验收。Linux 应用检查/发布在元数据中禁用。
- 参考覆盖折叠/展开 × 四机身角度 × 初始/滚动，展开另含单/多标签。屏幕遮罩与外壳路径独立，部分展开几何尚不可用。
- 已检入配置无需私有 Safari 截图或 Apple PDF 即可运行。Device Hub 新采集需要已安装的模拟器 runtime，重新提取参考需要原始证据。
- 宿主真实地址栏与原生导航绑定已实现。后续 UI 抛光、无障碍验收和真实标签管理见[路线图](100-ROADMAP.md)；平台差异见[平台对齐与macOS验收](015-PLATFORM-PARITY.md)。

## 开发与阅读入口

**日常开发先读本 README 和[贡献指南](CONTRIBUTING.md)即可。** 环境、命令、锁文件、清理和文档规范都集中在该指南；其它内容按修改范围查阅：

| 任务 | 按需阅读 |
| --- | --- |
| 修改浏览器组合/布局 | [架构与容器](001-ARCHITECTURE.md) |
| 修改实测数据或采集观测 | [测量约定](002-MEASUREMENT-CONTRACT.md)、[标定与验证](003-CALIBRATION-WORKFLOW.md)、[场景矩阵](005-SAFARI-SCENES.md) |
| 平台或语言工作 | [平台对齐与macOS验收](015-PLATFORM-PARITY.md)、[Windows 交接](009-WINDOWS-HANDOFF.md)、[本地化](011-LOCALIZATION.md) |
| 维护分发与基础设施 | [发布与仓库管理](013-RELEASE.md) |

根目录保存英文规范源文件，`docs/zh` 保存对应中文，`docs/en` / `docs/zh` 保存编号技术文档。独立 VitePress 工作区通过相对符号链接引用这些源；配置站点为 [dev-duo.aitiotekt.com](https://dev-duo.aitiotekt.com/)，基础设施就绪后仅由 `main` 部署，不维护网站内容副本。

## 证据与分发

原始截图、浏览记录和私人笔记保留在本地，保持字节/哈希不变；公开衍生物另行脱敏审查。第一方代码使用 [MIT](../../LICENSE)，模拟器图形再分发仍有[第三方说明](THIRD_PARTY_NOTICES.md)中的未确认事项。开发包未签名；正式签名和平台策略由 `devduo-metadata.toml` 与发布指南管理，固定的 Windows WebKit 运行时单独提供，参见上文初始化及第三方说明。

[安全](SECURITY.md) · [隐私](PRIVACY.md) · [变更日志](CHANGELOG.md)

已入库的 [Safari 场景图集](005-SAFARI-SCENES.md#已入库的-device-hub-参考图)包含 48 张经过审查的 WebP 画布参考、四张矩阵总览及颜色触发对照，附真实视口/组件框及源图哈希。

[English](../../README.md) | [中文](README.md)
