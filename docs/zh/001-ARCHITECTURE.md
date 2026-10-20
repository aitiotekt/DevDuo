# 架构与浏览器容器

Avalonia 管理宿主窗口和设备/场景选择。`NativeWebView` 在 macOS 中嵌入系统 WKWebView，在 Windows 中嵌入 WebView2。平台渲染器将页面与 Safari 示意组件组合在独立屏幕遮罩内；关闭外壳图形不会移除该遮罩。

## 代码分工

| 层 | 源码 | 职责 |
| --- | --- | --- |
| 宿主 | `src/DevDuo/MainWindow.cs`、`NativeWebView.cs` | 控件、网址提交、预览缩放、状态保存和原生生命周期 |
| 场景模型 | `config/`、`DeviceProfile.cs`、`SafariScene.cs`、`SafariContainerOptions.cs` | 路径、机身/场景映射、组件/标签框与呈现选项 |
| macOS | `src/DevDuo/macos/WebViewBridge.swift` | WKWebView、系统玻璃、裁剪、遮挡 inset 与取色 |
| Windows | `src/DevDuo/WindowsWebViewPanel.cs` | WebView2、原生区域裁剪、GDI 合成与 zoom Fit |
| 观察器 | `src/DevDuo/Fixtures/browser-observer.js` | 真实桌面视口、滚动、主题、标题和主机名 |
| 标定 | `web/`、`scripts/python/`、`scripts/swift/`、`scripts/DeviceHubUITests/` | 浏览器记录、真实 Safari 组件框、采集和比较 |

接收器仅提供固定标定资源并保存本地 JSON，不控制应用或执行页面提供的命令。环境与命令职责统一见[贡献指南](CONTRIBUTING.md)。

## 模式与交互

- **Safari 近似模拟**添加页面衍生颜色的悬浮组件，随实际滚动改变内容视口。展开态提供单/多标签变体；开关表示外壳几何，不管理真实标签页。
- **应用 WebView 沉浸模式**移除浏览器组件，使用完整逻辑场景。外壳开关不改变原生屏幕裁剪。

默认选择 Safari、折叠、机身 0°、1:1。八个测量映射决定实际场景，包括 Closed 180° 的旋转历史限定。macOS Fit 缩放原生坐标空间并保留 CSS 尺寸，Windows Fit 使用 WebView2 zoom，需原生验收。视口、标签栏预留与选中标签几何统一查阅[场景矩阵](005-SAFARI-SCENES.md)。部分展开透视模型尚不可用。

Back/Reload 使用原生导航，其它图示组件仅有有限演示交互。UA、DPR、方向、safe area 和键盘均保持真实桌面行为。Safari 外观不代表引擎变成 iPhone Safari。

## 悬浮组件下方绘制

已验证的 macOS 27 折叠模式保留完整 WKWebView，通过公开 `obscuredContentInsets` 调整页面视口。466 × 678 原生框设置侧边 84、底部 98，得到 382 × 580 页面视口；紧凑底部 80 得到 382 × 598。普通流继续绘制到地址栏下方，fixed-bottom 保持在较小视口内；270° 场景将侧边 inset 移到左侧。

`minimumViewportInset` / `maximumViewportInset` 表示底部 80…98 范围。独立探针测得 100svh = 580、100lvh = 598、100dvh = 580/598。沉浸模式清空 inset。Fit 只缩放呈现，不改变上述 CSS 尺寸；状态区分真实视口、绘制框、DIP 与遮挡 inset。

展开及 Windows 仍采用较小原生内容框。展开顶部延伸为衍生颜色近似，Windows 尚未建立等价的组件下方视口方案。当前 WK runtime 的 `takeSnapshot` 会遗漏真实窗口可见的延伸，因此必须同时检查实际窗口。归档中的 [inset 探针](../../scripts/archive/README.md)及本地原始 `captures/desktop-validation/under-chrome/` 记录保留了这一差别。

API 参考：[WKWebView obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets)、[WebKit viewport 支持](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/)。

## 材质与诊断

macOS 通过同一贝塞尔遮罩裁剪 WKWebView 和兄弟 AppKit 视图，避免普通 Avalonia 控件覆盖原生浏览器。当前 macOS 27 包使用 `NSGlassEffectView` clear/tint；较旧 `NSVisualEffectView` 分支需要降低部署目标重编并另行验证。着色、材质贡献、阴影和半径可调，iOS Liquid Glass 私有模糊/折射内核仍未知。

取色优先 theme-color，其次是实际 WK 渲染快照的边缘条带，computed background 仅作标明的回退。异步取色可能滞后移动内容。Windows 观察滚动/主题、采样预览边缘，再以 WinForms/GDI 着色和 alpha 绘制；未实现原生 backdrop blur，输入、DPI 和外观仍待原生验收。

**颜色演示**是用于主题色及内容取色的独立夹具。**保存状态**和 `window.devduoContainerTelemetry` 提供请求/实际框、滚动/chrome 状态、可见与点击边界、颜色来源/RGB 和材质参数。源码检出写入 `captures/desktop-validation/container-modes/`，独立安装使用本地应用数据的 `DevDuo/captures`；`DEVDUO_OUTPUT_DIR` 可覆盖位置。

测量语义与来源统一见[测量约定](002-MEASUREMENT-CONTRACT.md)，验收和已有平台边界见[标定与验证](003-CALIBRATION-WORKFLOW.md)。后续宿主控件与实测 Safari 几何分开推进，见[路线图](100-ROADMAP.md)。

[English](../en/001-ARCHITECTURE.md) | [中文](001-ARCHITECTURE.md)
