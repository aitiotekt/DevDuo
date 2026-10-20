# 测量约定与参考数据

修改标定几何或采集参考前阅读本页。公开检出使用已提交配置，不依赖私有截图；原生编译仍需要平台 SDK。模拟器测量不构成物理硬件规格。

## 浏览器与桌面记录

浏览器记录采用 `schemaVersion: 1`。接收器写入独立 `UUID.json` 并追加 `records.jsonl`，分配 ID 和 UTC `receivedAt`；浏览器提供 `clientTimestamp`、`reason`，事件防抖 350 ms。设备/姿态/浏览器/手动状态属于标签，`observedState` 才来自实际 scrollY。必须保留截图明确关联的记录 UUID/PNG，不能只按最近时间配对。

| 字段 | 语义 |
| --- | --- |
| `screen` | 浏览器真实 CSS 屏幕坐标、方向和色深；DPR 不是物理密度 |
| `viewport` | inner/client/outer、文档范围、滚动及 visual viewport 边界/缩放 |
| `safeAreaCssPx` | viewport-fit=cover 下实际计算的 `env(safe-area-inset-*)`；零不证明硬件方角或没有系统 inset |
| `components` | DOM 布局视口和文档坐标；原生浏览器组件须另测 XCUI 框或图像边界 |
| `environment` | 真实 UA、平台、URL、偏好、语言和时区 |
| `unknown` | 未有独立可靠来源时，硬件毫米/半径和 Liquid Glass 参数保持 null |

标定页不绘制 Safari 组件。桌面记录保留 `macOS-WKWebView` / `Windows-WebView2` 和 `safari-shell-approx` / `app-webview` 标签；screen API、DPR 和 safe area 均为原生桌面值，不得标成真实 iPhone Safari。桌面专用 `window.devduoContainerTelemetry` 与**保存状态**记录请求场景、真实视口/原生框、滚动/chrome 状态、独立可见/点击边界、取色来源及材质参数。DOM 观察和原生快照异步，不是原子采样。绘制及输出行为见[架构](001-ARCHITECTURE.md)。

## 坐标、曲线与物理未知值

机身角度、场景角度、CSS 像素、原生点、呈现 DIP 和 framebuffer 像素分别记录。已有模拟器能力包含 1398 × 2034、2007 × 2853 framebuffer，scale 为 3；显示屏关联须在 Device Hub 核实。屏幕遮罩 canvas 分别为 466 × 678、1252 × 1780，后者独立缩放，不是屏幕 CSS 坐标。

外壳与屏幕路径独立，不使用 `outerRadius = innerRadius + bezel`：

| 来源图形 | 画布 | 图形单位中的屏幕及偏移 |
| --- | --- | --- |
| phone15 composite | 514 × 706 | 466 × 678，偏移 (25, 14)，含非对称铰链 |
| phone14 composite | 660 × 924 | 626 × 890，偏移 (17, 17)，采用来源曲线与 34 单位描边 |

圆角元数据 8/59、55 的单位未定，也不等于连续贝塞尔过渡范围；应保留原值，不直接替换为 CSS border-radius。同样，模拟器的 460 hdpi/vdpi 不能将截图像素换算为可信毫米。屏幕对角线、物理机身/边框尺寸、折叠角、玻璃曲率和真实硬件半径继续作为可配置未知值。

AX 点击框、文字框和可见玻璃分别测量。展开紧凑地址点击框为 252 × 30.8；已有 2853 × 2007 localhost PNG 的可见边缘估计 x1323…1531 / y71…133，约 69.3 × 20.7 CSS px，软边误差 ±2 图像像素，主机名长度会改变宽度。折叠紧凑态没有对应 AX 地址字段，其可调点击目标不能标成实测原生框。完整标签与标题点击框的区别见[场景矩阵](005-SAFARI-SCENES.md)。

## 公开配置与来源

| 文件 | 职责 |
| --- | --- |
| `config/device-profiles.json` | 独立屏幕/外壳路径与画布到屏幕映射 |
| `config/safari-scenes.json` | 八个机身/折叠映射与 24 个初始/滚动 Safari 状态，含展开单/多标签 |
| `config/safari-shell.json` | 可调布局/材质默认值，区分估计与交互框 |
| `config/physical-parameters.json` | 物理尺寸和硬件半径明确为 null |
| `config/measurement-matrix.json` | 采集计划，不代表已完成覆盖 |

当前参考集来自 Xcode 27.1 / iOS 27.1 iPhone Duo 模拟器。runtime、Safari 版本、标签数量和旋转历史均可能改变结果。逻辑纵向屏幕为折叠 466 × 678、展开 669 × 951；实际机身对应见[场景矩阵](005-SAFARI-SCENES.md)。

公开记录保留源相对名称及 SHA-256，原件不在 Git。哈希只标识文件，不使其公开可用，也不证明硬件规格。原始字节、哈希、冻结源码及失败尝试都在本地证据中保留。分享使用另行审查的衍生物，公开文件排除个人路径、模拟器标识和浏览内容，见[隐私](PRIVACY.md)与[第三方说明](THIRD_PARTY_NOTICES.md)。

历史提取和完整桌面/标签验证需要原始本地参考集。缺少它们是证据边界，不是应用依赖失败。[归档指南](../../scripts/archive/README.md)统一说明复现参数和各工具假设；先生成到 `temp/`，对照原始框和遥测后再替换确认的配置。新观测遵循[标定与验证](003-CALIBRATION-WORKFLOW.md)。

[English](../en/002-MEASUREMENT-CONTRACT.md) | [中文](002-MEASUREMENT-CONTRACT.md)
