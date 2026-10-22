# 标定与验证

修改参考前先运行已提交基线。[测量约定](002-MEASUREMENT-CONTRACT.md)定义坐标与来源规则，[场景矩阵](005-SAFARI-SCENES.md)是视口和标签几何的统一参考。

## 采集新的 Safari 会话

1. 运行 `mise exec -- just serve`，在本地模拟器中打开标定页。物理设备联网尚不属于已验证流程。
2. 启动目标 Xcode Device Hub runtime，选择 Closed/Partially Open/Open 与机身旋转；每次采集前核对实际姿态。使用 `xcrun simctl list devices booted` 获取本地标识，公开示例始终使用占位符。

```sh
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

模式为 `closed`、`partial`、`open`；方向为 `portrait`、`portraitUpsideDown`、`landscapeLeft`、`landscapeRight`。runner 验证实际姿态，保留原生组件 JSON、明确显示屏的原始 PNG、浏览器记录、源哈希及可选视频。命令成功不证明姿态改变；支持部分展开采集也不代表存在标定后的部分展开模型。

3. 加载与材质运动稳定后采集初始/滚动态，检查实际 scrollY 和 viewport；按需增加上滚/键盘状态。发出 swipe 不等于滚动态已经验证。
4. 展开单/多标签分别采集。对于已加载且带唯一标记的页面，`--tab-mode single --reuse-current-url URL` 避免再建标签。主动保留并恢复此前页面。
5. 每次在忽略的 `captures/` / `measurements/` 下创建新会话目录。记录 runtime 版本和时间，失败尝试独立保留；检查非空内容、尺寸、明确显示屏、时间戳和哈希后才接受图像。默认 XCTest 截图可能来自错误显示屏。

## 更新参考与比较

历史提取器/验证器依赖原参考集布局，不是任意新会话的通用导入器。具体输入和命令统一见[归档指南与审查索引](../../scripts/archive/README.md)。先输出到新的 `temp/` 路径，对照原生框和浏览器记录，保留原始字节后再更新确认的配置。缺失原件必须明确失败。

桌面验收覆盖两种容器、折叠/展开与四个机身角度。Safari 增加初始/滚动和展开标签栏两种设置，共 24 个参考状态。保存应用状态和原始窗口截图，分别比较真实 DOM 视口、原生绘制框、遮挡 inset、组件位置、完整标签边界与碰撞，不能只看请求选项。沉浸模式应清空预留，Fit 保持逻辑视口，语言切换保留页面、滚动和实测几何。

同尺度无损 PNG 使用 `mise exec -- uv run --locked devduo-tools png-compare`，裁剪与排除区域必须显式指定。`contact-sheet --pair REFERENCE CANDIDATE --output NEW.png` 仅供展示，会缩放，不能证明光学保真。参数详见 [Python 指南](../../scripts/python/README.md)，视频时序/哈希检查见 [Swift 指南](../../scripts/swift/README.md)。

比较材质时保留多帧/视频、页面/主题颜色、滚动方向和时序。检查真实窗口，因为 WK 快照可能遗漏可见的 inset 下方延伸。窗口 JPEG 观测及明确标为转换的 PNG 都不是原始无损模拟器 framebuffer。像素差异只描述匹配图对，不代表硬件保真度或 iOS 玻璃内核。

## 文档末端与原生位置回归

运行 `just serve`，在 DevDuo 和 Device Hub Safari 中加载 `/document-end.html?fixed=full`。维护中的 2400 CSS 像素夹具提供通栏、窄标记、无 fixed 三种底栏、滚动控件及 **Save observation**。测试窄标记前先清除底栏，因为 Safari 可能保留此前的延伸颜色。保存真实 DOM 数据和原始窗口图片；页面的 `devduoDocumentEndProbe` API 也支持可复现的程序滚动。程序滚动结果不代表滚轮输入已经验收。

2026-10-22 macOS 会话位于 `captures/desktop-validation/document-end/20261022T071643Z-fix/`，三种场景均与 iOS 27.1 Safari 对照。文档末端的通栏不透明底栏向下延伸紫色；窄标记和无 fixed 使用青绿色 canvas 底色，没有重复行号。DevDuo 宽度保持 382、文档高度保持 2400：展开底部 inset 98 对应视口高 580 / 最大 scrollY 1820；收起 inset 80 对应 598 / 1802。fixed 底栏始终高 28，底边等于视口高度。程序对照期间 Safari 保持展开 inset，DevDuo 收起态另行检查。这些是 CSS/原生场景单位，不是物理尺寸。

WKWebView 负责 canvas 绘制。原先的被动底栏层只近似可命中测试、纯色不透明的通栏 fixed 底栏，不符合条件时清除，不改变 DOM、输入或滚动范围。缺少原生固定容器延伸能力时仍保留该有限回退，不能用它证明半透明遮罩兼容。当前原生路径见[架构说明](001-ARCHITECTURE.md#悬浮组件下方绘制)。须检查真实窗口，因为 WK 快照可能遗漏遮挡 inset 下方的像素。

位置回归应在冷启动、最大化/还原、侧栏切换及 Fit/1:1 切换后，对照原生屏幕与外壳，并单独记录 `nativeClipGeometry` 和 DOM 数据。原生附件位置与显式裁剪配置须完成同一轮布局。运行 `just test-macos` 检查真实 AppKit 裁剪/resize/Fit bounds、inset 和被动颜色层；它补充实际窗口验收，不代表 Windows 已通过。

## 半透明弹窗背景

2026-10-24 的 iOS 27.1 Device Hub 会话使用折叠竖屏和 `/document-end.html?fixed=none`。**Fixed scrim** 创建 `position: fixed; inset: 0; background: rgb(0 0 0 / 55%)`；**Dialog backdrop** 使用 `showModal()` 和相同的 `::backdrop` 颜色。切换前先关闭当前遮罩。`overlay=fixed|dialog` 可指定初始条件，切换自动保存接收器记录。夹具不会扩大遮罩、给文档补高度或添加滚动锁。

| 条件 | 实际延伸表现 |
| --- | --- |
| 无遮罩 | 带编号的文档内容继续绘制到地址栏后方 |
| 普通 fixed 遮罩或原生 dialog，无 fixed 底栏 | 底部和侧边预留区变为变暗后的纯色 canvas，编号条纹不再穿过该填充区 |
| 原生滚动后的收窄态／文档末端 | 同样出现深色填充；显示或关闭遮罩不改变 382 × 598 viewport、Y=505 或最大 Y=1802，以及 2400 文档高度 |
| 不透明通栏底栏＋遮罩 | 全新加载时，变暗的底栏下方出现深色 canvas；关闭再打开后，fixed 和 dialog 都保留了先前未变暗的紫色底部延伸，稍后截图仍存在，侧边保持深色 |

导航展开时仍为 382 × 580、最大 Y=1820。fixed 遮罩的 DOM 底边仍止于 viewport 边缘：580，或收窄态小数高度 597.65625，被 `innerHeight` 取整为 598。浏览器延伸的是合成后的**颜色**，并没有扩大 fixed DOM 框，也不是给视口外每个流式内容像素盖黑色。这与 [WebKit backdrop 修复](https://github.com/WebKit/WebKit/commit/2ae949b78743) 的机制一致：遮罩颜色与页面背景混合后，向被组件遮挡的 inset 区延伸。[Bug 300965](https://bugs.webkit.org/show_bug.cgi?id=300965) 将此与滤镜、渐变变体区分。本次没有验收模糊、渐变、其它机身方向和其它运行时版本。

原始窗口 JPEG、明确关联的浏览器记录、哈希、原生滑动回执及缩放对照图保留在本地 `captures/safari/overlay/20261023T194024Z/`，与公开的 48 状态图库分开。底栏颜色历史问题是已观察的 Safari 边界，不能当作通用目标行为。Windows 现使用原生固定容器颜色，并已单独通过遮罩验收，见 [Windows 交接](009-WINDOWS-HANDOFF.md#默认页面文档标题与弹窗-inset2026-10-24)。变更后的 macOS 桥接仍需原生验收。本 Windows 宿主没有检查原始 macOS 采集会话；用户随后也确认测试过黑色背景加 `opacity: .5` 的写法。

桌面回归使用 `tests/DevDuo.WebKitProbe/pages/fixed-overlay.html` 和 `just test-windows-fixed-overlay`。分别采集真实视口、原生 source 和最终场景：视口像素验证 alpha 合成，inset 像素验证纯色延伸。打开前、显示中和关闭后记录文档标识、滚动、文档尺寸、fixed 矩形及视口单位，覆盖普通 fixed 遮罩、dialog 背景、底层不透明底栏、展开/收起的底部预留及侧边 inset。测试夹具或编译通过本身不代表 Safari 视觉对齐或 macOS 原生验收。

不要通过撑大 DOM 遮罩或统一压暗所有原生 Safari 控件模拟；延伸合成颜色时，保留真实 viewport、fixed 位置和滚动范围。

## Safari 滚动指示器

2026-10-24 的折叠竖屏 iOS 27.1 会话使用 `/document-end.html?fixed=none&trace=1`，记录 document/client/visual 宽度、CSS 滚动条设置及有数量上限的滚动／resize 样本。**Middle** 使用瞬时 `scrollTo`，**Smooth to 1000** 使用平滑滚动；`content=short` 移除文档溢出，`overflow=scroll` 强制对应 CSS，`scrollbars=none|auto` 选择根元素的 `scrollbar-width`。这些仅是夹具控制，不修改 DevDuo 渲染器或任意开发者页面。

| 条件 | 实际指示器表现 |
| --- | --- |
| 全新长页面静止 | 本次首次呈现序列中隐藏；内容可滚动不代表初始必须显示 |
| 原生滑动及惯性滚动 | 细覆盖条出现并跟随位置，运动结束后短暂保持，再淡出；停在非零 scrollY 不会让它常驻 |
| 干净标签中滚到文档末端 | 到达末端后仍短暂可见，再淡出；不能写成 `scrollY < maxScrollY` 才显示 |
| 瞬时／平滑程序滚动 | 实际 scrollY 已改变，本次均未显示指示器；仅凭 DOM scroll 事件不能判断用户交互 |
| 短页面，包括 `overflow-y: scroll` | 静止及手势序列均未见指示器，最大 scrollY 保持零 |
| 长页面设置 `scrollbar-width: none` | 原生滚动达到 Y=516，指示器仍隐藏 |

所有已记录宽度始终为 `innerWidth = documentElement.clientWidth = body.clientWidth = visualViewport.width = 382`，显示或隐藏时沟槽宽度均为零。原生滑块位于**整个屏幕的外侧右缘**，覆盖右侧 Safari 组件预留区，并不位于 382 像素网页 viewport 的右边缘。它有绘制厚度，但不占布局宽度。[WebKit 滚动条说明](https://webkit.org/blog/16301/webkit-features-in-safari-18-2/#scrollbars) 解释了覆盖式滚动条、iOS 细样式、`none`，以及覆盖模式下 `scrollbar-gutter` 不会预留空间。macOS 系统“始终显示”设置可能产生占宽滚动条，不能将桌面设置直接当作 iOS 目标。

阳性对照应使用独立新标签：测试 `none` 后，复用标签中的后续 `auto` 文档即使真实滚动也仍隐藏，而新建 `auto` 标签恢复显示。成因尚未定位；受影响的末端尝试不能证明正常末端行为。原始 JPEG 序列、带时间戳的原生回执、浏览器记录关联及已检查的裁剪对照保存在 `captures/safari/scrollbars/20261023T195821Z/`。原生工具的 `--gesture-delay 4` 便于在滑动前启动窗口采样，默认仍为零。截图间隔约 0.2–0.5 秒，不能排除帧间短闪，也不认证统一的精确淡出延迟。其它机身方向、嵌套／横向滚动容器、导航位置恢复、辅助功能和桌面宿主效果尚未验收。

[Windows Safari 指示器实现](014-WINDOWS-WEBKIT.md#safari-滚动指示器)默认隐藏，保留覆盖式几何和页面 CSS 隐藏设置，区分原生意图／位移与文档位置。不要强制常驻 `overflow:scroll`、从实测 viewport 扣除桌面滚动条宽度，或仅按 DOM scroll 位移决定显隐；页面内容、fixed 位置和滚动范围保持原样。桌面验收与这批 Device Hub 观察分别记录。

以上 Device Hub 结果来自 dev 提交 `55e1c353ff5ee1b8dc6e8ea796f228b00964dfb4`。原始 macOS 采集不在本 Windows 宿主上，桌面实现及验收需单独记录。

## 已有基线与待验收范围

2026-10-21 本地基线使用真实 macOS 窗口、无障碍、保存状态和独立 Device Hub 参考建立。以下是已有记录，不表示每次编辑文档都会重跑测试。

| 范围 | 已建立的观测 | 剩余边界 |
| --- | --- | --- |
| 参考几何 | 原始来源重新提取与公开配置一致；核对八映射、24 个 Safari 状态及四个完整选中标签框 | runtime/标签数量/旋转历史变化需新证据；无部分展开几何 |
| macOS WKWebView | 检查 Safari/沉浸、Fit、独立遮罩、标签居中及折叠组件下方普通流 | 展开顶部延伸为近似，旧系统材质分支未验证 |
| 语言切换 | 英中切换保留 URL、scrollY 580、382 × 598 视口、原生框/inset，并检查主题和偏好持久化 | Windows 原生语言/无障碍/存储待验收 |
| 宿主界面（SukiUI/Avalonia 12） | 在真实 WKWebView 上脚本操作控件：折叠/展开多标签/单标签/应用模式及折叠四个角度均与场景矩阵一致；切换外框和语言保持 scrollY 400；适应预览 0.746 时保持 382 × 580 CSS px；展开 1:1 预览滚动后原生视图仍限制在预览区内，CSS 视口保持 951 × 543；检查了标定标签、query/fragment、历史、受限端口错误和颜色演示重建；查看了浅色/深色宿主渲染 | WKWebView 持有焦点时的快捷键、VoiceOver、原生视图上方弹出层和实际标题栏尚未交互观察；Windows 待验收 |
| 便携工具 | 已检查仅公开文件的 setup、源/链接检查、图片/broker 测试及严格双语站构建 | 私有证据验证需要原件，不代表远程 CI 已运行 |
| Windows | 原生 Avalonia WebKit 24 状态视口/fixed/单位/滚动检查及 39 组独立普通页面比较 | 更广泛的输入、DPI/显示器和光学覆盖仍需[原生验收](009-WINDOWS-HANDOFF.md)；WebKit 范围及复现步骤见[后端指南](014-WINDOWS-WEBKIT.md) |

原始本地证据包括 `captures/desktop-validation/container-modes/`、`under-chrome/`、`selected-tab/` 和 `host-ui/`。非对称 90° 遮罩暴露并修复了额外 Y 翻转，单看对称纵向不足以发现；选中标签修正使用完整标题容器，而非较窄的标题点击框；Closed 180° 保留顺时针序列限定。修改渲染时保留这些回归情形。

当前自动检查见[贡献指南](CONTRIBUTING.md)，平台清单见 [Windows 交接](009-WINDOWS-HANDOFF.md)。无论构建是否成功，物理尺寸、真实硬件半径和 iOS 光学参数仍未知。

[已入库场景图集](005-SAFARI-SCENES.md#已入库的-device-hub-参考图)提供经审查的 WebP 画布图和完整 48 项索引。原生滑动会话、本地索引/审查字段和可复现导出命令见 [Python 指南](../../scripts/python/README.md#safari-canvas-reference-library)。动画等待失败、跨折叠 trait/inset 残留单独保留。若可见组件与真实视口不符，应先恢复干净场景再接受；重启可能重置机身方向，必须重新核对。

[English](../en/003-CALIBRATION-WORKFLOW.md) | [中文](003-CALIBRATION-WORKFLOW.md)
