# Windows 交接

Windows 通过 Avalonia 使用固定版本的 WebKit Windows Port，macOS 保留系统 WKWebView。Safari 外壳和沉浸式 App WebView 均遵守[实测场景约束](005-SAFARI-SCENES.md)。本文统一记录当前 Windows 验收及回归范围；[WebKit 实现](014-WINDOWS-WEBKIT.md)、[发布交付](013-RELEASE.md)和[平台差异](015-PLATFORM-PARITY.md)分别维护。

## 当前状态

里程碑 **`windows-desktop/2026-10-29`** 是本地限定基线。旋转 select 定位、真实微软拼音预编辑及 WebCore 网页无障碍已有本地修复与原生证据。0.1.0 通过 metadata 选择匹配引擎，引擎发布和重打包应用验收仍是独立交付步骤。

基线包含未提交的宿主／测试改动和原生补丁 `0004`，精确输入由各证据会话冻结；它不是 Git tag 或稳定版批准。未受影响的结果记为**沿用基线**，不能写成后续二进制已经重跑。没有私有证据的公开 checkout 可以引用本记录，但不能声称独立验证通过。

| 当前基线范围 | 结果与限定 |
| --- | --- |
| 场景与绘制 | 实际 192 DPI 下 1501 项断言，覆盖 24 个 Safari 状态、八种映射、48 个静态边界场景及六种窗口大小；运行时缺失处理单独通过。该批次早于最终仅宿主 AX 启动／缓存保护。 |
| 呈现 | 同一引擎通过加速／软件动画、滚动损伤、小于／大于 1 的缩放、最大化／Inspector／恢复、静态稳定及文档保持。源栅格与最终 DWM 呈现分别记录。 |
| 输入与菜单 | 组合引擎在 144 DPI 通过微软拼音 47 项，在 192 DPI 通过八姿态 select 56 项；较早输入／菜单／对话框批次覆盖 96／144／192 DPI，各自绑定原二进制。 |
| 最终无障碍宿主 | 实际 96／144／192 DPI 各 24 项原生检查，包含公开树发现、原生画布像素、名称／值／动作、物理命中、模态恢复、过期文档拒绝及旋转关窗。较早混合显示器证据属于其独立宿主版本。 |
| 实际窗口与讲述人 | 在最终 192 DPI、约 100% Fit 下检查 Closed 0°／90°／180°。Closed 180° 的 Tab／Shift+Tab 及讲述人语音回顾确认了文档／编辑框名称，只证明限定导航／朗读，不代表完整文本导航或 Safari 光学等价。 |
| 冷导航 | 最终 96 DPI 的 HTTP／内联／Network 恢复通过原有 1.5 秒门槛。首个文档观测分别为 176.83／184.51ms，HTTP 匹配源像素在 261.94ms 内观察到；这不是 DWM 或外站基准。 |
| 可移植集成 | `just verify` 及双语文档检查／构建通过：Core 370、本地化 1769／每语言 200 键、JS 50 及一项 macOS 跳过、Python 32。Windows 编译零警告／错误；文档已有的 ES2024 警告保留。 |

152 组普通页面几何比较仅证明布局，保留所记录的引擎版本。旧版完整 DPI、遮罩或光学通过不自动认证替换后的绘制实现。

剩余边界：

- 尚未实现 UIA TextPattern／IAccessible2、完整周边文本及再转换。讲述人名称／值检查不等于广泛辅助技术兼容。
- 文件／颜色／上下文弹窗、广泛网站／压力滚动和设备丢失／WARP 故障注入仍未验收。
- 玻璃是桌面近似；物理硬件尺寸、圆角及 Apple 光学内核仍未知。
- 匹配依赖／应用包交付及再分发审查仍待完成。完整运行时验收保持 false，不代表每次修改都要重启整轮审计。

## Windows 里程碑与增量回归

默认只在**当前可用的单一实际 DPI** 检查受影响功能。不能因为改了 Windows 文件就切换系统缩放、要求另接显示器、重建 WebKit 或跑全部原生 recipe；只有引擎输入变化才重建引擎，日常检查保留缓存。

| 修改范围 | 选择的验证 |
| --- | --- |
| 文档 | `just docs-check`、`just build-docs`、公开信息审计及 diff 审查，不构建或运行原生程序。 |
| 共享状态或本地化 | 对应 `just test-core`／`just test-localization`；运行时／UI 行为改变时补平台构建和限定原生观察。 |
| 无障碍 | `just test-windows-accessibility`；发现入口、焦点或朗读语义变化时补短时讲述人观察。 |
| IME、select 或 JavaScript 对话框 | 对应 `just test-windows-ime`、`just test-windows-popups` 或 `just test-windows-page-dialogs`。 |
| 材质、绘制或内容延伸 | 对应呈现／inset／固定遮罩检查和实际窗口；栅格边界变化时在同一 DPI 加分数 Fit。 |
| 启动／导航或开发服务 | 对应导航延迟、开发工具或调试检查。 |
| 共享视口／遮罩／旋转或广泛引擎集成 | Core／构建、单 DPI 的 `just test-windows` 及相关原生检查；仅布局／inset 语义变化时加普通页面几何比较。 |
| 依赖选择或打包 | 发布／构建／打包检查及单 DPI 的包启动／运行时加载观察，沿用未变引擎的行为证据。 |

表格用于选择，不能逐行全部执行。`just verify` 是可移植检查／构建集成，不包装 Windows 原生验收。修复后重跑受影响检查，只有另一受影响边界、无法解释的失败或明确扩大验收请求才增加覆盖。

额外 DPI 仅针对缩放换算、弹窗／caret／命中的物理坐标、像素取整／裁剪、跨屏资源生命周期变化或缩放专属故障。先说明原因，再选最小有效对比；只有分数 DPI 相关才补 150%。Fit 与 DPI 不同。环境不可用时记录缺口，不反复要求用户调整桌面。

无障碍 recipe 默认不跨屏；`DEVDUO_TEST_MONITOR_TRANSITIONS=1` 显式开启该用例，并要求不同实际缩放的显示器。`DEVDUO_TEST_SCREEN_SCALE` 可选一个匹配且唯一的真实显示器，两者均不修改系统缩放，日常不设置即可。`DEVDUO_TEST_FOREGROUND_GATE=1` 等待原生 Start 按钮后才注入输入。`DEVDUO_TEST_IME_PROFILE=microsoft-pinyin` 仅为测试进程选择已启用的输入法，结束后恢复原 profile，不注册系统键盘。

## 初始化与平台代码

mise、真实符号链接和日常命令见[贡献指南](CONTRIBUTING.md)。引擎构建一次：`mise --env windows-webkit exec -- just build-windows-webkit-runtime`。Visual Studio C++／Clang 与 Windows SDK 是系统依赖；运行时发现、必需 API 及隔离 profile 见 [WebKit 初始化](014-WINDOWS-WEBKIT.md#avalonia-后端)。

| 职责 | 源码；未注明时相对于 `src/DevDuo/` |
| --- | --- |
| 运行时 ABI 与浏览器生命周期 | `windows/WindowsWebKitApi.cs`、`WindowsWebKitView*.cs` |
| 原生场景、输入映射及呈现 | `windows/WindowsWebViewPanel.cs`、`WindowsScreenPresenter.cs` |
| 实时合成及材质 | `windows/WindowsComposition.cs`、`HostCompositor.cpp`、`HostCompositor.h` |
| 共享宿主／浏览器／场景状态 | `Host/`、`Browser/`、`Device/` |
| 生产引擎输入 | 仓库根目录 `native/webkit/windows/` |
| 原生检查、支持及证据 | 仓库根目录 `tests/DevDuo.Windows.Tests/{Checks,Harness,Evidence}/` |

只有独立开发工具前端使用系统 WebView2，被测 Windows 页面没有替代后端。

## 原生验收清单

按变化选择相关项，不是每次编辑必须执行的全量流程。

1. 独立记录真实加载的运行时、DPI、Fit 和原生框，不把请求设置当观测结果；验证实际方向，保留 Closed 180° 旋转历史限定。
2. 呈现变化保持真实视口、fixed／sticky 框、视口单位和滚动范围；区分四向有效正文、文档外背景与固定颜色延伸。
3. 同时检查实际窗口像素／交互及 DOM／遥测。有内容的树、快照、GPU 提交或源栅格不能替代实际窗口证据。
4. 同时使用无插入标记的静态页和动画；覆盖相关滚动／文档末端／缩放／导航变化，不通过插入 fixed 元素、强制重绘循环或伪造 DOM 尺寸绕过问题。
5. 输入路径变化时检查焦点、键盘／IME、旋转菜单／caret／命中及公开应用树；提交了一个中文字符不等于预编辑通过。
6. 验证导航／销毁拒绝旧回调和迟到 HWND／DPI 操作；纯呈现设置不能导航或重置文档。

## 证据与交付

里程碑汇总为 `captures/desktop-validation/pre-0.1.0/20261028T165509769Z-desktop-followup/final-combined/final-results.json`，同目录 sidecar 标识基线。逐次源码、二进制、原生输入和观测保留于忽略的 `captures/desktop-validation/windows-runtime/`。原始字节与失败尝试保持不变，不将原始日志、设备标识或浏览记录复制到公开文档。

后续报告只需写基线 ID、受影响行为、实际执行检查、实际 DPI／Fit、结果及未验证边界。哈希是来源输出，不能代替验收。关键实现裁决及失败机制见 [WebKit 指南](014-WINDOWS-WEBKIT.md#关键裁决与经验)，发布步骤见[发布指南](013-RELEASE.md)。

[English](../en/009-WINDOWS-HANDOFF.md) | [中文](009-WINDOWS-HANDOFF.md)
