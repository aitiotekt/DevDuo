# 测量约定

浏览器记录采用 JSON `schemaVersion: 1`。POST 接收器在输出目录写入单独的 UUID.json 并追加 records.jsonl，增加 UTC 的 id/receivedAt；浏览器提供 clientTimestamp/reason，事件记录以 350 ms 防抖。设备、姿态、浏览器及手动状态是 URL/UI 标签，不构成自动证明；observedState 来自实际 scrollY。截图元数据关联记录 UUID 与 PNG，必须同时保留，不能未经检查只按最近时间配对。

| 字段 | 语义 |
| --- | --- |
| screen | 浏览器报告的 CSS 屏幕坐标、方向及色深；DPR 不是物理密度 |
| viewport | inner/client/outer、文档范围、滚动和 visual viewport 尺寸/缩放；会受浏览器栏、键盘和缩放影响 |
| safeAreaCssPx | 带 viewport-fit=cover 的页面实际计算到的 env 值；零不证明硬件方角或没有系统 inset |
| components | DOM getBoundingClientRect 与文档坐标；浏览器原生组件需另行 XCUI/截图测量 |
| environment | UA、平台、URL、媒体偏好、语言和时区；桌面 WKWebView 不得标为 iPhone Safari |
| unknown | 物理毫米、硬件圆角及 Liquid Glass 参数保持 null，除非有独立可靠来源 |

标定页本身不绘制 Safari 工具栏。桌面宿主的 Safari 近似外壳与页面 DOM 分离，不计为真实 iPhone Safari。应分别观察真实 Safari 初始、下滚、上滚和键盘状态。材质动态需要多帧/视频；单张截图只证明一次采样外观，模拟器也不证明物理设备行为。

## 坐标与曲线来源

本地模拟器能力包含 1398×2034、2007×2853 framebuffer，scale 为 3；显示器关联必须在 Device Hub 确认。第一屏幕遮罩 canvas 为 466×678，第二个为 1252×1780，需要单独缩放，不能当作屏幕 CSS 坐标。圆角元数据 8/59、55 的单位未确定，也不等于连续贝塞尔过渡范围，应保留原值，不直接用作 border-radius。

外壳使用独立提取的外边界，不能套用“外圆角等于内圆角加边框”。phone15 composite 是 514×706，屏幕为 466×678、偏移 (25,14)，含非对称铰链；phone14 composite 是 660×924，屏幕 626×890、偏移 (17,17)，外形由来源曲线与 34 单位描边构成。历史提取器 `scripts/archive/2026-10-21/extract_geometry.py` 可从本地原件重建该批配置，见[参考数据](007-REFERENCE-DATA.md)。macOS 裁剪实际 WKWebView backing layer，Windows 按同一路径裁剪原生父区域；仅裁剪 Avalonia 绘制层不足以约束原生子视图。

profile 中 460 hdpi/vdpi 是模拟器元数据，不能用截图像素除以 460 来发布毫米尺寸。图形和 framebuffer 不能证明显示器对角线、机身尺寸、折叠角、玻璃曲率或真实圆角，这些参数继续未知。外壳图形单位仅用于保持模拟器比例。

## 桌面容器标记

桌面记录保留 `macOS-WKWebView` 或 `Windows-WebView2` 浏览器标签；displayMode 区分 `safari-shell-approx` 与 `app-webview`，notes 包含请求的机身/场景旋转和预览比例。screen API 保持真实桌面值，不注入 iPhone DPR 或硬件 safe area。

观察器提供 `window.devduoContainerTelemetry`，Save state 记录目标场景、实际页面视口、原生框、chrome 状态、scrollY、可见地址表面及独立点击框、颜色来源、材质后端与可调参数。源码检出输出位于被忽略的 captures，独立安装输出及覆盖变量见 [README](README.md)。这些桌面复现记录与真实 Safari 遥测分开。theme-color 优先；macOS 否则采样原生 WK 快照边缘，computed style 只是标明的回退。DOM 观察和快照异步，不可声称原子同步。

AX 点击框、文字框和可见玻璃边界是不同测量。展开紧凑地址 AX 为 252×30.8，而本地页面 PNG 估计可见表面约 69.3×20.7 CSS px，软边误差约 ±2 图像像素；主机名文本会改变宽度。折叠紧凑态没有暴露对应 AX 地址字段，可调点击目标不能标成测得值。

[English](../en/002-MEASUREMENT-CONTRACT.md) | [中文](002-MEASUREMENT-CONTRACT.md)
