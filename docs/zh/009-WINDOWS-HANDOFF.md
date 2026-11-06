# Windows 交接

Windows 通过 Avalonia 使用固定版本的 WebKit Windows Port，macOS 保留系统 WKWebView。Safari 外壳和沉浸式 App WebView 均遵守[实测场景约束](005-SAFARI-SCENES.md)。本文统一记录当前 Windows 验收及回归范围；[WebKit 实现](014-WINDOWS-WEBKIT.md)、[发布交付](013-RELEASE.md)和[平台差异](015-PLATFORM-PARITY.md)分别维护。

## 当前状态

里程碑 **`windows-desktop/2026-10-29`** 是本地限定基线，后续版本在其上增加限定复验。基线包含当时未提交的宿主／测试改动和原生补丁 `0004`，精确输入由各证据会话冻结；它不是 Git tag 或稳定版批准。未受影响的结果记为**沿用基线**，不能写成后续二进制已经重跑。没有私有证据的公开 checkout 可以引用本记录，但不能声称独立验证通过。

metadata 选择匹配的引擎版本。默认引擎构建启用异步滚动和原生触摸；发布工具拒绝缺少能力或元数据不匹配的运行时（见[运行时发布](013-RELEASE.md#windows-webkit-运行时发布)）。

下表每个领域只保留最新结果，更早的批次保留在证据中。

| 领域 | 最新结果及限定 |
| --- | --- |
| 场景与绘制 | 基线：实际 192 DPI 下 1501 项断言，覆盖 24 个 Safari 状态、八种映射、48 个静态边界场景及六种窗口大小；运行时缺失处理单独通过。152 组普通页面几何比较（`20261104T222354596Z-ordinary`）仅证明布局／视口语义。 |
| 呈现与内容延伸 | 基线：加速／软件动画、滚动损伤、小于／大于 1 的缩放、最大化／Inspector／恢复及文档保持。120 DPI 下：336 项同帧延伸检查，覆盖 Closed 0°、Open 90°／270° 的 1:1 与分数 Fit，GPU 与 CPU 路径均无 GDI 上传（`20261105T091427386Z`）；101 项弹窗／材质检查（`20261105T053733174Z`）；30 项外壳过渡检查（`20261105T172340844Z`）。 |
| fixed 遮罩与模态暗化 | 呈现稳定性：120 DPI、1:1 下 121 项，含 Closed 横屏两侧 inset（`20261111T124812261Z`）；96 DPI 下 99 项，覆盖 Closed 竖屏侧栏按钮、透明检查器画布、清空及换向帧（`20261105T172212152Z`）。42 项上游 fixed 取色检查（`20261105T091524392Z`）及 41 项页面自带外侧遮罩检查（`20261105T141508970Z`）。原页面 Closed 竖屏在 1:1 及 120 DPI Fit 下，所有稳定帧均显示白卡位于灰色底带之上，关闭弹窗后灰色释放。 |
| 原生触摸与异步滚动 | 120 DPI 下：272 项触摸检查（`20261105T061318817Z`）、58 项生命周期检查（`20261105T070852663Z`）、242 项主线程繁忙滚轮检查（`20261105T072748774Z`）、38 项输入／帧检查（`20261105T071206184Z`）及 fixed 页脚接缝配对采样（`20261105T072708075Z`）。覆盖可信事件、取消、`touch-action`、overflow 锁定、嵌套滚动、页面主线程阻塞以及 1:1 与 Fit。宿主时序测到提交为止，不代表 DWM 扫描输出。 |
| 触摸圆点 | 120 DPI 下 189 项（`20261105T071006059Z`）。基线：192 DPI、分数 Fit 下 187 项像素／合并／取消检查及 19 项交互检查。 |
| 输入、菜单与对话框 | 微软拼音：144 DPI 下 47 项。select：192 DPI 下八姿态 56 项。对话框批次覆盖 96／144／192 DPI，各自绑定原二进制。 |
| 无障碍 | 25 项 UIA 检查，覆盖异步快照、通知及旧文档拒绝（`20261105T030237464Z`）。基线：96／144／192 DPI 各 24 项，另有 Closed 180° Tab／Shift+Tab 及讲述人名称检查。 |
| 冷导航 | 96 DPI 本地夹具的 HTTP／内联／Network 恢复检查通过 1.5 秒门槛。 |

证据位于忽略的 `captures/desktop-validation/windows-runtime/` 和 `windows-live-edge/`。每行限定于所记录的二进制；早期运行时的结果属于沿用，不算重跑。渲染路径结论需要实际传输及 GPU 阶段证据，不能只依据 CSS 提升提示。

未完成项：

- 原始动态页面上触控手感仍落后于鼠标。高速惯性、动态结构变化及 GPU 生产者的 GL 完成等待仍需处理。
- 原页面 Closed 横屏弹窗场景尚未复验。`just test-windows-fixed-overlay` 停在第一个绝对定位边缘用例：原生材质红通道与白色相差四，夹具允许误差为三。
- 文件／颜色／上下文弹窗、广泛网站及压力滚动、多适配器共享和设备丢失／WARP 故障注入尚未验证。
- 尚未实现 UIA TextPattern／IAccessible2 及完整周边文本／再转换。讲述人名称／值检查不等于广泛辅助技术兼容。
- 共享的服务恢复逻辑尚无 macOS 原生验收。再分发审查仍待完成，运行时完整验收标记保持 false。
- 玻璃是桌面近似；物理硬件尺寸、圆角及 Apple 光学内核仍未知。

## Windows 里程碑与增量回归

默认只在**当前可用的单一实际 DPI** 检查受影响功能。不能因为改了 Windows 文件就切换系统缩放、要求另接显示器、重建 WebKit 或跑全部原生 recipe；只有引擎输入变化才重建引擎，日常检查保留缓存。

| 修改范围 | 选择的验证 |
| --- | --- |
| 文档 | `just docs-check`、`just build-docs`、公开信息审计及 diff 审查，不构建或运行原生程序。 |
| 共享状态或本地化 | 对应 `just test-core`／`just test-localization`；运行时／UI 行为改变时补平台构建和限定原生观察。 |
| 无障碍 | `just test-windows-accessibility`；发现入口、焦点或朗读语义变化时补短时讲述人观察。 |
| IME、select 或 JavaScript 对话框 | 对应 `just test-windows-ime`、`just test-windows-popups` 或 `just test-windows-page-dialogs`。 |
| 材质、绘制或内容延伸 | 对应呈现／inset／固定遮罩／呈现稳定性检查和实际窗口；栅格边界变化时在同一 DPI 加分数 Fit。 |
| 触摸圆点呈现 | `just test-windows-touch-pointer`、相关交互检查及实际窗口移动观察；显隐标志不能替代旧位置像素检查。 |
| 启动／导航或开发服务 | 对应导航延迟、开发工具或调试检查。 |
| 共享视口／遮罩／旋转或广泛引擎集成 | Core／构建、单 DPI 的 `just test-windows` 及相关原生检查；仅布局／inset 语义变化时加普通页面几何比较。 |
| 依赖选择或打包 | 发布／构建／打包检查及单 DPI 的包启动／运行时加载观察，沿用未变引擎的行为证据。 |

表格用于选择，不能逐行全部执行。`just verify` 是可移植检查／构建集成，不运行 Windows 原生验收。修复后重跑受影响检查，只有另一受影响边界、无法解释的失败或明确扩大验收请求才增加覆盖。

额外 DPI 仅针对缩放换算、弹窗／caret／命中的物理坐标、像素取整／裁剪、跨屏资源生命周期变化或缩放专属故障。先说明原因，再选最小有效对比；只有分数 DPI 相关才补 150%。Fit 与 DPI 不同。环境不可用时记录缺口，不反复要求用户调整桌面。

无障碍 recipe 默认不跨屏；`DEVDUO_TEST_MONITOR_TRANSITIONS=1` 显式开启该用例，并要求不同实际缩放的显示器。`DEVDUO_TEST_SCREEN_SCALE` 可选一个匹配且唯一的真实显示器，两者均不修改系统缩放，日常不设置即可。`DEVDUO_TEST_FOREGROUND_GATE=1` 等待原生 Start 按钮后才注入输入。`DEVDUO_TEST_IME_PROFILE=microsoft-pinyin` 仅为测试进程选择已启用的输入法，结束后恢复原 profile，不注册系统键盘。

`just test-windows-modal-sequence` 检查嵌套弹窗边缘、颜色标量稳定、保留式合成和开发者服务开关。`DEVDUO_TEST_MODAL_BODY_ANGLE=0`、`90` 或 `270` 将诊断限定为 Closed 0°、Open 90° 或 Open 270°；不设置则执行受影响的三个姿态。`just test-windows-developer-restore` 使用隔离偏好和八次真实应用启动，验证监听冲突隔离、损坏配置阻止及下次启动恢复。

在 `just test-windows-native-touch` 中，`DEVDUO_TEST_TOUCH_LIFECYCLE_ONLY=1` 检查触点结束、惯性停止及动态滚动约束。`DEVDUO_TEST_TOUCH_TRACKING_ONLY=1` 检查后备缩放大于一且有小数时的单物理像素移动与反向。`DEVDUO_TEST_TOUCH_SEAM_ONLY=1` 检查源帧与实际窗口像素，覆盖快速视口切换和停稳后的分数 Fit；再加 `DEVDUO_TEST_TOUCH_SEAM_STATIC_ONLY=1` 只跑停稳用例。不设置选择器时执行手势规则、绘制路径和繁忙页面姿态批次。各项检查使用独立测试 profile。

## 平台代码

初始化见[贡献指南](CONTRIBUTING.md)；运行时发现、必需 API 及隔离 profile 见 [WebKit 指南](014-WINDOWS-WEBKIT.md#avalonia-后端)。

| 职责 | 源码；未注明时相对于 `src/DevDuo/` |
| --- | --- |
| 运行时 ABI 与浏览器生命周期 | `windows/WindowsWebKitApi.cs`、`WindowsWebKitView*.cs` |
| 原生场景、输入映射及呈现 | `windows/WindowsWebViewPanel.cs`、`WindowsScreenPresenter.cs` |
| 实时合成及材质 | `windows/WindowsComposition.cs`、`HostCompositor.cpp`、`HostCompositor.h` |
| 共享宿主／浏览器／场景状态 | `Host/`、`Browser/`、`Device/` |
| 生产引擎输入 | 仓库根目录 `native/webkit/windows/` |
| 原生检查、支持及证据 | 仓库根目录 `tests/DevDuo.Windows.Tests/{Checks,Harness,Evidence}/` |

## 原生验收清单

按变化选择相关项。

1. 独立记录真实加载的运行时、DPI、Fit 和原生框，不把请求设置当观测结果；验证实际方向，保留 Closed 180° 旋转历史限定。
2. 呈现变化保持真实视口、fixed／sticky 框、视口单位和滚动范围；区分四向有效正文、文档外背景与固定颜色延伸。
3. 同时检查实际窗口像素／交互及 DOM／遥测。有内容的树、快照、GPU 提交或源栅格不能替代实际窗口证据。
4. 同时使用无插入标记的静态页和动画；覆盖相关滚动／文档末端／缩放／导航变化，不通过插入 fixed 元素、强制重绘循环或伪造 DOM 尺寸绕过问题。
5. 输入路径变化时检查焦点、键盘／IME、旋转菜单／caret／命中及公开应用树；提交了一个中文字符不等于预编辑通过。
6. 验证导航／销毁拒绝旧回调和迟到 HWND／DPI 操作；纯呈现设置不能导航或重置文档。

## 证据与报告

里程碑汇总为 `captures/desktop-validation/pre-0.1.0/20261028T165509769Z-desktop-followup/final-combined/final-results.json`，同目录 sidecar 标识基线。逐次源码、二进制、原生输入和观测保留于忽略的 `captures/desktop-validation/windows-runtime/`。原始字节与失败尝试保持不变，不将原始日志、设备标识或浏览记录复制到公开文档。

后续报告只需写基线 ID、受影响行为、实际执行检查、实际 DPI／Fit、结果及未验证边界，并更新上表对应行，不追加新的批次记录。哈希是来源输出，不能代替验收。实现裁决及失败机制见 [WebKit 指南](014-WINDOWS-WEBKIT.md#关键裁决与经验)，发布步骤见[发布指南](013-RELEASE.md)。

[English](../en/009-WINDOWS-HANDOFF.md) | [中文](009-WINDOWS-HANDOFF.md)
