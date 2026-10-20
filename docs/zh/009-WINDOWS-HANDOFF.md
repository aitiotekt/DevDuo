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

## 证据与交付

遵循[标定与验证](003-CALIBRATION-WORKFLOW.md)，在忽略的 `captures/` 保存原始截图与状态。公开配置足够运行，独立 Safari 来源/图像验证另需经审查的原始证据，缺少原件不能判为通过。图片工具参数统一见 [Python 指南](../../scripts/python/README.md)。

交付说明已观察结果、缺失情形和复现步骤。宿主 UI 改动与实测几何分开，物理毫米/半径、铰链角和 iOS 材质内核继续未知。仅在明确请求时 commit/推送。

[English](../en/009-WINDOWS-HANDOFF.md) | [中文](009-WINDOWS-HANDOFF.md)
