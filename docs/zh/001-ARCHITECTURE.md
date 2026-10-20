# 架构与浏览器容器

Avalonia 管理宿主窗口和设备/场景选择。`NativeWebView` 在 macOS 中嵌入系统 WKWebView，在 Windows 中嵌入 WebView2。平台渲染器将页面与 Safari 示意组件组合在独立屏幕遮罩内；关闭外壳图形不会移除该遮罩。

## 代码分工

| 层 | 源码 | 职责 |
| --- | --- | --- |
| 宿主 | `src/DevDuo/MainWindow.cs`、`NativeWebView.cs`、`NavigationState.cs`、`CalibrationUrl.cs` | 场景/选项选择、导航绑定、标定标签、预览缩放、状态保存和原生生命周期 |
| 宿主界面 | `HostAddressBar.cs`、`HostSettingsPanel.cs`、`HostStatusBar.cs`、`HostControls.cs`、`App.axaml`、`Styles/DevDuoStyles.axaml`、`PreferenceStore.cs` | SukiUI 主题、设计 token、控件分组、状态栏、快捷键和宿主设置持久化 |
| 场景模型 | `config/`、`DeviceProfile.cs`、`SafariScene.cs`、`SafariContainerOptions.cs` | 路径、机身/场景映射、组件/标签框与呈现选项 |
| macOS | `src/DevDuo/macos/WebViewBridge.swift` | WKWebView、系统玻璃、裁剪、遮挡 inset 与取色 |
| Windows | `src/DevDuo/WindowsWebViewPanel.cs` | WebView2、原生区域裁剪、GDI 合成与 zoom Fit |
| 观察器 | `src/DevDuo/Fixtures/browser-observer.js` | 真实桌面视口、滚动、主题、标题和主机名 |
| 标定 | `web/`、`scripts/python/`、`scripts/swift/`、`scripts/DeviceHubUITests/` | 浏览器记录、真实 Safari 组件框、采集和比较 |

接收器仅提供固定标定资源并保存本地 JSON，不控制应用或执行页面提供的命令。环境与命令职责统一见[贡献指南](CONTRIBUTING.md)。

## 模式与交互

- **Safari 近似模拟**添加页面衍生颜色的悬浮组件，随实际滚动改变内容视口。展开态提供单/多标签变体；开关表示外壳几何，不管理真实标签页。
- **应用 WebView 沉浸模式**移除浏览器组件，使用完整逻辑场景。外壳开关不改变原生屏幕裁剪。

全新配置默认 Safari、折叠、机身 0°、1:1；已保存的宿主设置会恢复其它选择，但每次启动机身角度均为 0°。八个测量映射决定实际场景，包括 Closed 180° 的旋转历史限定。适应预览跟随可用预览区域，且不超过 1:1。macOS Fit 缩放原生坐标空间并保留 CSS 尺寸，Windows Fit 使用 WebView2 zoom，需原生验收。视口、标签栏预留与选中标签几何统一查阅[场景矩阵](005-SAFARI-SCENES.md)。部分展开透视模型尚不可用。

Back/Reload 使用原生导航，其它图示组件仅有有限演示交互。UA、DPR、方向、safe area 和键盘均保持真实桌面行为。Safari 外观不代表引擎变成 iPhone Safari。

## 宿主控件与导航

宿主控件主题由 Avalonia 12 上的 SukiUI 7 提供；`Styles/DevDuoStyles.axaml` 补充浅色/深色 token、间距、字体层级、图标和组件样式类。这些样式只作用于 Avalonia 宿主控件；设备外框、屏幕遮罩、Safari 组件、标签文字和视口预留仍由平台渲染器和测量配置负责。宿主界面使用不透明背景并位于独立的网格单元。不使用 SukiUI toast、对话框及其它窗口内覆盖层，因为 Avalonia 内容无法绘制在原生浏览器子视图之上。Avalonia 只移动原生子视图，不会按滚动祖先裁剪，因此原生视图不放在预览滚动区内：透明占位随外框滚动，原生视图位于其上方的独立层，尺寸为占位的可见部分（不遮挡滚动条）。原生裁剪视图（macOS 的 `devduo_set_viewport`、Windows 的父窗口）在内部偏移完整尺寸的容器，因此滚动时容器框、场景比例、CSS viewport 和遥测均不变。

宿主地址栏绑定当前原生视图。macOS 观察 WKWebView 的 `url`、`title`、`isLoading`、`estimatedProgress`、历史标志和导航失败，并通过 `devduo_set_navigation_callback` 回调；Windows 使用 WebView2 的 `NavigationStarting`、`SourceChanged`、`DocumentTitleChanged`、`HistoryChanged` 和 `NavigationCompleted`。回调投递到 UI 线程，原生容器销毁后即被忽略；被取消或被后续导航取代的加载不算失败。地址必须为 http/https：缺少协议时，回环、`.local` 和私有网络主机补 `http://`，其它主机补 `https://`；其它协议直接拒绝。query 和 fragment 原样保留。测量标签（`device`、`pose`、`browser`、`state`、`displayMode`、`notes`）只附加到本地接收器页面（回环地址、端口 8765、路径 `/`），会替换旧标签，且不在地址栏显示。

| 变化 | 原生视图 |
| --- | --- |
| 语言、主题、多标签页栏、材质调节、外框 | 原地重新配置或移动，保留页面、历史和滚动位置 |
| 预览缩放 | 原地调整尺寸；仅标定页会重新加载，使 `notes` 标签一致 |
| 设备形态、机身角度、容器模式 | 在当前页面或颜色实验室重新创建；滚动和历史重置，状态栏给出说明 |

快捷键：⌘L/Ctrl+L 聚焦地址栏，⌘R/Ctrl+R 或 F5 重新加载，Esc 停止加载，⌘[ ⌘] 或 Alt+←/→ 浏览历史，⌘S/Ctrl+S 保存状态，⌘\\/Ctrl+\\ 显示或隐藏设置。macOS 同时在原生**导航**菜单中列出这些命令，因此 WKWebView 持有焦点时仍可使用。Windows 通过面板的命令键链转发 WebView2 加速键，需要原生验收。宿主控件具有本地化的无障碍名称、带快捷键的提示，以及禁用项的说明文字；状态消息为 polite live region。

`PreferenceStore.cs` 将宿主设置保存在 `DevDuo/preferences.json` 的可选 `host` 对象中：`theme`、`containerMode`、`fold`、`previewScale`、`deviceOutline`、`showTabStrip`、四个材质值（null 表示沿用 `config/safari-shell.json`）、面板展开状态和 `lastUrl`（仅 http/https）。各字段独立校验，无效字段使用默认值并给出说明。机身角度从不保存，因此折叠 180° 只能经由记录的顺时针序列到达。`DEVDUO_URL` 优先于 `lastUrl`。**重置设置**恢复默认值和机身 0°，不改变语言和当前页面。文件格式与语言行为见[本地化](011-LOCALIZATION.md)。

## 悬浮组件下方绘制

已验证的 macOS 27 折叠模式保留完整 WKWebView，通过公开 `obscuredContentInsets` 调整页面视口。466 × 678 原生框设置侧边 84、底部 98，得到 382 × 580 页面视口；紧凑底部 80 得到 382 × 598。普通流继续绘制到地址栏下方，fixed-bottom 保持在较小视口内；270° 场景将侧边 inset 移到左侧。

`minimumViewportInset` / `maximumViewportInset` 表示底部 80…98 范围。独立探针测得 100svh = 580、100lvh = 598、100dvh = 580/598。沉浸模式清空 inset。Fit 只缩放呈现，不改变上述 CSS 尺寸；状态区分真实视口、绘制框、DIP 与遮挡 inset。

展开及 Windows 仍采用较小原生内容框。展开顶部延伸为衍生颜色近似，Windows 尚未建立等价的组件下方视口方案。当前 WK runtime 的 `takeSnapshot` 会遗漏真实窗口可见的延伸，因此必须同时检查实际窗口。归档中的 [inset 探针](../../scripts/archive/README.md)及本地原始 `captures/desktop-validation/under-chrome/` 记录保留了这一差别。

API 参考：[WKWebView obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets)、[WebKit viewport 支持](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/)。

WKWebView 保留原生 canvas/页面边界外背景绘制，宿主不再强制透明或覆盖引擎解析出的 html/body 底色。折叠模式通过只读 DOM 观察，仅为可见、不透明、实色、通栏的 fixed 底栏在底部 inset 内添加被动 AppKit 颜色层；窄标记和无底栏时清除。它是 Safari 原生颜色延伸的明确近似，未泛化到渐变、半透明、sticky 或不参与命中测试的元素，不改变 DOM 样式、视口预留、fixed 框或滚动范围。`fixedBottomColorExtension` 记录可见性和框。

原生位置在 Avalonia 布局后的 `Loaded` 调度优先级统一同步，包括裁剪/内容配置。AppKit 容器关闭隐式子视图缩放，并在自身框尺寸变化时重新布局，使冷启动、resize 和 Fit 时屏幕内容与外框保持对齐；`nativeClipGeometry` 记录完整容器、逻辑 bounds 和裁剪偏移。详见[验收记录](003-CALIBRATION-WORKFLOW.md)。

## 材质与诊断

macOS 通过同一贝塞尔遮罩裁剪 WKWebView 和兄弟 AppKit 视图，避免普通 Avalonia 控件覆盖原生浏览器。当前 macOS 27 包使用 `NSGlassEffectView` clear/tint；较旧 `NSVisualEffectView` 分支需要降低部署目标重编并另行验证。着色、材质贡献、阴影和半径可调，iOS Liquid Glass 私有模糊/折射内核仍未知。

取色优先 theme-color，其次是实际 WK 渲染快照的边缘条带，computed background 仅作标明的回退。异步取色可能滞后移动内容。Windows 观察滚动/主题、采样预览边缘，再以 WinForms/GDI 着色和 alpha 绘制；未实现原生 backdrop blur，输入、DPI 和外观仍待原生验收。

**颜色演示**是用于主题色及内容取色的独立夹具。**保存状态**和 `window.devduoContainerTelemetry` 提供请求/实际框、滚动/chrome 状态、可见与点击边界、颜色来源/RGB 和材质参数。`previewScale` 记录实际呈现比例。源码检出写入 `captures/desktop-validation/container-modes/`，独立安装使用本地应用数据的 `DevDuo/captures`；`DEVDUO_OUTPUT_DIR` 可覆盖位置。

测量语义与来源统一见[测量约定](002-MEASUREMENT-CONTRACT.md)，验收和已有平台边界见[标定与验证](003-CALIBRATION-WORKFLOW.md)。后续宿主控件与实测 Safari 几何分开推进，见[路线图](100-ROADMAP.md)。

[English](../en/001-ARCHITECTURE.md) | [中文](001-ARCHITECTURE.md)
