# Windows 交接

为现有 Avalonia/WebView2 路线建立 Windows 原生基线，再按[路线图](100-ROADMAP.md)改进宿主控件。macOS 已有交互证据，Windows 目前仅交叉编译。交接中不加入机器名、个人检出路径或模拟器标识。

## 初始化与平台代码

遵循[贡献指南](CONTRIBUTING.md)，包括真实文件/目录符号链接和 `core.symlinks=true`；单独安装 WebView2 Runtime。在 mise 环境运行 `just check`，再分别开启 `just serve` / `just dev`。Windows 自动选择 `net10.0-windows` 及其 NuGet 锁，不为 Windows 独有问题改写 macOS 锁。浏览器 profile 保存在本地应用数据的 `DevDuo/WebView2`。

| 范围 | 源码 |
| --- | --- |
| WebView2 生命周期、裁剪与外壳 | `src/DevDuo/WindowsWebViewPanel.cs`、`NativeWebView.cs` |
| 宿主 UI 与缩放 | `src/DevDuo/MainWindow.cs` |
| 共享场景/选项及遥测 | `SafariScene.cs`、`SafariContainerOptions.cs`、`Fixtures/browser-observer.js`、`config/safari-scenes.json` |
| 需保留的 macOS 行为 | `src/DevDuo/macos/WebViewBridge.swift` |

## 原生验收清单

1. 使用仅公开文件的新检出启动。检查 Runtime 初始化及缺失时的明确提示，记录 OS/runtime/DPI，不包含机器标识。
2. 检查导航/重载、焦点、输入、滚动、原生销毁、窗口缩放和反复模式/设备切换。原生子视图须被内屏遮罩裁剪，不能覆盖宿主控件。
3. 覆盖两种容器、折叠/展开和四个机身角度；Safari 增加初始/滚动与展开单/多标签。尺寸统一以[场景矩阵](005-SAFARI-SCENES.md)为准。比较真实视口、框、组件和碰撞，不仅看请求选项；Closed 180° 保留旋转历史限定。
4. 检查 100%、150%、200% DPI 和可用的跨显示器切换。区分 CSS 像素、zoom、DIP 和图像像素，macOS Fit 不代表 Windows zoom 正确。
5. 沉浸模式清空 Safari 组件和旧预留，保留屏幕裁剪；外壳独立切换。展开四个角度均保留完整选中标签、关闭区域和居中的 14 点场景文字。
6. 用颜色夹具和实时内容检查主题提示、边缘取色和滚动转换。GDI 着色/alpha 不含原生 backdrop blur；Windows 当前缩小 WebView2 框，组件下方普通流等价行为尚未验证，见[架构](001-ARCHITECTURE.md)。
7. 检查 English/简体中文/系统默认、重启持久化、错误和无障碍，包括 Runtime 失败路径。在已滚动页面切语言，比较 URL、scroll 和真实视口；资源行为见[本地化指南](011-LOCALIZATION.md)。
8. 检查宿主界面：重定向后的提交地址和标题，后退/前进/重新加载/停止，加载与带**重试**的失败状态；在宿主控件和 WebView2 内部分别持有焦点时测试 Ctrl+L、Ctrl+R/F5、Alt+←/→、Ctrl+S 和 Ctrl+\。在窄窗口中滚动 1:1 预览，确认 WebView2 始终位于预览区内，不覆盖侧栏、工具栏、状态栏或滚动条。检查 SukiUI 渲染与标题栏、ComboBox 和提示弹出层是否位于 WebView2 子视图之上、讲述人与键盘遍历、浅色/深色主题，以及 100%、150%、200% 下的侧栏和状态栏布局。切换外框、缩放、语言和主题须保留页面与滚动位置；切换形态、角度和模式按[架构](001-ARCHITECTURE.md)所述重新创建视图。

## 证据与交付

遵循[标定与验证](003-CALIBRATION-WORKFLOW.md)，在忽略的 `captures/` 保存原始截图与状态。公开配置足够运行，独立 Safari 来源/图像验证另需经审查的原始证据，缺少原件不能判为通过。图片工具参数统一见 [Python 指南](../../scripts/python/README.md)。

交付说明已观察结果、缺失情形和复现步骤。宿主 UI 改动与实测几何分开，物理毫米/半径、铰链角和 iOS 材质内核继续未知。仅在明确请求时 commit/推送。

## 文档末端延伸调研（2026-10-22）

独立的官方 Windows WebKit `324423@main` 探针仍属实验，尚不是上文已集成的 WebView2 路线。其报告的 `scrollY=1820`、视口高 580、文档高 2400 时旧行号残留，不是 Safari 应有的目标效果。本机 iOS 27.1 Device Hub Safari 在受控的 2400 CSS 像素条纹页中没有重复行号：Y=1770 时，视口下方继续显示剩余 50 像素普通流，之后填页面底色；Y=1820 时，整个 98 像素 inset 都使用背景。符合条件的紫色通栏 fixed 底栏会向该区域延伸颜色；移除底栏后再显示窄标记时使用根背景。颜色延伸的判定和历史与旧文档像素残留不同。本次使用夹具 `scrollTo`，没有独立重跑 Windows 滚轮测试。原始窗口 PNG、DOM 记录和夹具源码保存在本地 `captures/desktop-validation/document-end/`。

对于顶部 inset 为零、未缩放的场景，设 `D` 为根文档高度、`Y` 为实际 scrollY、`V` 为未遮挡视口高度、`B` 为底部 inset，有效延伸长度为 `L = clamp(D - Y - V, 0, B)`。保留 `[V, V+L)` 中的实时内容，每次相关更新都在最终原生绘制表面明确清除并补色 `[V+L, V+B)`。本例从 `2400 - 580 - 98 = 1722` 之后开始出现补色区域。保留 `D`、`V`、滚动范围和 fixed 框；添加 DOM padding 或过早盖住整个 inset 都会改变语义或隐藏有效内容。CSS 坐标必须按真实 zoom/DPI 转换。只刷父 HWND 背景无法清除不透明子窗口或后备表面中保留的旧像素。

补色优先使用引擎解析出的页面底层/canvas 背景，html/body 背景回退须明确标注；不要复制最后一行，也不要直接拿工具栏主题着色当页面背景。fixed 底栏颜色延伸作为独立的外观能力处理。[WebCore 的 `insetClipLayerRect`](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebCore/page/LocalFrameView.cpp#L2763) 在文档边界裁剪延伸；[Cocoa 的 fixed 颜色延伸视图](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebKit/UIProcess/API/Cocoa/WKWebView.mm#L3692) 属于额外的原生行为，[underPageBackgroundColor](https://developer.apple.com/documentation/webkit/wkwebview/underpagebackgroundcolor) 定义页面边界外的底色。应检查 Windows Port 的裁剪、失效区域和后备表面清理；这些是排查方向，尚非确认的根因。接入 Avalonia 前补验滚轮/程序滚动、短文档、文档高度变化、两种底部 inset，以及离开末端后再返回。

首次 macOS 对照发现了独立的灰色 canvas/底栏未延伸问题，没有重复行号。现已恢复 WebKit canvas 绘制，并增加纯色通栏 fixed 底栏的被动颜色近似层。98/80 两种底部 inset 和底栏移除均已检查，真实视口、fixed 框及滚动范围未改变；见[回归流程及边界](003-CALIBRATION-WORKFLOW.md#文档末端与原生位置回归)。此 macOS 修复不代表实验性 Windows 后备表面问题已经解决或验收。

[English](../en/009-WINDOWS-HANDOFF.md) | [中文](009-WINDOWS-HANDOFF.md)
