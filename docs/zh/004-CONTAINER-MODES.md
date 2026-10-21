# 桌面容器模式

同一系统 WKWebView/macOS 或 WebView2/Windows 提供两种容器，共享屏幕几何和设备外形。

- **Safari · approximation**：添加页面衍生颜色的悬浮组件，随实际滚动收缩/展开视口。
- **App WebView · immersive**：移除浏览器组件，恢复完整目标逻辑视口。外壳开关独立，内部屏幕遮罩保留。

默认是 Safari、Closed、机身 0°、1:1。八个参考映射决定实际屏幕场景。macOS Fit 缩放原生坐标空间并保留 CSS 视口。展开 Safari 支持多标签页栏开关，标题统一为场景中的 14 点居中文字，详见[场景矩阵](005-SAFARI-SCENES.md)。

## 布局来源

config/safari-shell.json 包含可调逻辑布局及材质默认值，来自本地采集与浏览器遥测，不包含毫米。

| 场景 | 初始内容视口 | 紧凑内容视口 |
| --- | --- | --- |
| Closed 466×678 | 382×580 | 382×598 |
| Closed 678×466 | 594×368 | 594×386 |
| Open 951×669，多标签 | 951×543 | 951×618 |
| Open 669×951，多标签 | 669×825 | 669×900 |

折叠侧边区域为 84 逻辑单位，底部初始/紧凑预留 98/80；展开多标签顶部为 126/51，单标签初始为 80。早期折叠转换的 idle 问题曾让滚动后高度保持 580，旧记录保留，但不替代刷新后的基准。

副屏 nativeRotation270 表明机身纵向与屏幕纵向不同。Closed 180° 使用采集顺时针序列中保留的横向场景。没有经过测量的部分展开铰链角或透视模型。

地址表面的可见、文字及点击区域独立。展开紧凑 AX 252×30.8 不是玻璃轮廓；原始 PNG 的可见边缘估计为约 69.3×20.7 CSS px，软边误差约 ±2 图像像素，文字会影响宽度。折叠紧凑点击框是可用性参数，未伪装为真实 AX 测量。

## 原生材质与取色

macOS 容器用 AppKit 组合 WKWebView 与兄弟悬浮视图，不将普通 Avalonia 控件覆盖在原生浏览器上；父层用真实贝塞尔屏幕路径裁剪两者。当前桥接最低 macOS 27，使用公开 NSGlassEffectView 的 clear/tint。较旧系统 NSVisualEffectView 分支需要降低部署目标重编并单独验证，尚未完成。

Tint alpha、glass blend、阴影和半径可调，但 AppKit 未暴露 iOS Safari 的折射/模糊内核。折叠模式通过公开 obscuredContentInsets 保留较小页面 viewport，并让普通流内容实际延伸到地址栏后方/下方；展开仍预留较小原生内容框，仅使用衍生颜色近似，不声称顶部精确延伸。

桌面 JS 观察滚动、真实视口、主机名、theme-color 和背景。theme-color 优先，否则 macOS 采样 WKWebView 边缘实际快照，computed background 标为回退。异步快照可能滞后移动帧。注入观察器与标定页分离，采集时原始页面源码快照保留在本地证据。

Windows 保留 WebView2、共享布局、原生裁剪、滚动/主题观察和边缘取色；浮层使用 WinForms/GDI 着色与 alpha 合成，**尚无原生 backdrop blur**。Windows 目前仅交叉编译，DPI、输入、透明度和运行效果待验收。

Back/Reload 使用原生浏览器，其它图示控件只是有限演示，并不构成完整 Safari 标签/书签应用。两种模式都不伪造 iPhone UA、DPR、safe area 或键盘。

## 复现与诊断

按 [README](README.md) 启动接收器和应用。实际上下滚动触发 chrome 转换；Color demo 使用独立夹具测试 theme-color 和内容取色。材质参数在展开面板中。

Save state 输出目标/实际视口、scrollY、chrome、可见/点击框、颜色与后端参数。源码检出写入 captures 下，独立安装使用本地应用数据，DEVDUO_OUTPUT_DIR 可覆盖。`window.devduoContainerTelemetry` 暴露桌面原生状态，独立于真实 Safari 参考记录。

work 中保存本地构建和日志，captures 保存 JPEG 窗口观测和状态。JPEG 不是无损模拟器 framebuffer；像素比较应使用明确 display 的原始 PNG。折叠延伸细节见[组件下方绘制](006-UNDER-CHROME-RENDERING.md)。

[English](../en/004-CONTAINER-MODES.md) | [中文](004-CONTAINER-MODES.md)
