# 平台能力对照与 macOS 原生交接

本文供 macOS Agent 执行交接，依据当前源码、[Windows 交接](009-WINDOWS-HANDOFF.md)与[校准流程](003-CALIBRATION-WORKFLOW.md)中的限定记录编写，不新增任何原生验收通过结论。对照目标是[已接受的场景矩阵](005-SAFARI-SCENES.md)，不是 Windows 桌面呈现。保留 macOS 系统 WKWebView、生产 Windows WebKit Port，以及 Safari/沉浸容器。

分别记录四种状态：**已实现**（存在源码）、**已编译**（该平台构建了精确版本）、**已原生检查**（对该二进制执行了限定交互）及**待验证/不支持**。共享测试、Windows 通过、旧 macOS 构建或模拟器观察不能填入当前 macOS 原生通过栏。校准文档中较早的 macOS 布局、材质和语言观察仍是历史记录；后续开发工具、身份/标题及 fixed-container Swift 修改需要当前 macOS 编译与呈现检查。

## 当前实现与差异

| 领域与源码 | Windows | macOS 及验收边界 |
| --- | --- | --- |
| 被测页面引擎：`src/DevDuo/` 下的 `windows/WindowsWebKitApi.cs`、`macos/WebViewBridge.swift` | 固定源码运行时、必需宿主 ABI 与完整性清单 | 系统 WKWebView，不需要 Windows 引擎构建/归档。记录实际系统、WebKit 与 SDK；系统能力可能不同。 |
| 存储 | 持久 `DevDuo/WebKit`，绝对路径 `DEVDUO_WEBKIT_PROFILE` 可隔离测试 | `WKWebViewConfiguration` 保留默认持久网站数据存储，没有对应的 macOS profile 覆盖实现。偏好隔离不隔离网站数据。跨进程检查使用独立测试 origin/token 和相同应用身份。 |
| 移动身份：`Browser/BrowserIdentity.cs` | 首次请求前设置原生自定义 UA，已有 Windows 请求头/重定向检查记录 | 加载前设置 `customUserAgent`；须在 macOS 核实首次请求头与 `navigator.userAgent`。观察到的精确 iPhone Safari 字符串含冻结的 `OS 18_7` 与 `Version/27.0`，属于兼容身份，不证明实际桌面引擎、iOS 版本或物理硬件。两种容器共用它；没有单独测量 App WebView 身份。 |
| 标题/引导页：`Browser/PageTitleState.cs`、`Browser/WelcomePage.cs`、`macos/WebViewBridge.swift` | 按文档确认门控选中示意标签标题，提供离线引导页与应用自有文本本地化 | Swift 独立按 committed/已观察文档身份门控标题。引导页更新自有文本节点而不导航。检查动态/空/长标题及延迟 provisional 加载；共享标题测试不验证原生 KVO 顺序。 |
| 呈现：`Browser/NativeWebView.cs`、`Host/MainWindow.cs` | 旋转/配置更新现有浏览器 | `devduo_update_scene`/`devduo_configure` 更新现有容器。旋转、语言、Fit、轮廓、外观必须保持文档；新合法范围可能夹紧滚动。折叠和容器模式切换会有意重建容器及历史。 |
| 延伸绘制 | 生产四侧 inset/绘制路径，Windows 限定结果见 009/014 | 支持的系统在 Closed 侧栏布局使用完整视图 obscured inset；Open 顶栏仍是预留 frame 近似，不是相同四侧引擎路径。侧边/根背景填充与系统材质必须保留近似标签。 |
| 本地 Inspector | 独立原生前端，已有 Windows F12/Ctrl+Shift+I 窗口观察 | 使用运行时检查的私有 `_inspector`/developer-extras selector，先 detach 再 show，并限制连接重试。缺少能力应显示已有明确错误。WKWebView 持有焦点时通过原生菜单提供 F12、Ctrl+Shift+I，实际路由待验收。 |
| 原生远程检查（WIP） | 显式、无认证的 WebKit Inspector TCP listener，与 CDP 分开 | `isInspectable`（macOS 13.3+）提供 Safari Develop 发现。宿主不创建 Windows TCP listener；地址/端口/LAN 字段不配置 Safari 发现。分别验证本地 Inspector 冲突与停止后的发现移除。 |
| CDP/chii：`DeveloperTools/`、`macos/DeveloperWebViewBridge.swift` | 页面插桩加独立系统 WebView2 前端 | 相同的 token 保护页面 CDP 服务；前端使用独立、非持久系统 WKWebView 和自有工具 UA，不得替换被测页面或共享其存储。实际 chii 兼容性待验证。JavaScript CDP 子集不等于引擎调试、性能分析或完整网络覆盖。 |
| 代理 | system、direct、无认证自定义 HTTP/HTTPS/SOCKS5 | macOS 14+ 通过 `WKWebsiteDataStore.proxyConfigurations` 提供 system/custom；UI 禁用 direct，桥接明确拒绝。没有认证代理支持。首次加载前应用，后续请求验证路由，不重载当前文档。默认存储在被测视图之间共享。 |
| fixed/container 颜色 | 原生 fixed-container color API 加宿主合成，已有 alpha/dialog 限定检查 | 能力检查通过时启用系统 fixed-container 扩展，并移除竞争的宿主侧边/页脚填充；否则旧 fallback 仅近似符合条件的不透明全宽页脚/根背景。fallback 不能认证半透明 fixed 与 dialog backdrop。 |
| 滚动指示器 | 原生资格/CSS/输入状态，加宿主绘制的临时屏幕边缘滑块 | 遥测明确报告 `host-scene-policy-unavailable; native-acceptance-pending`。保留系统 WKWebView 滚动条与用户系统偏好，没有对应的宿主屏幕边缘策略；仅能滚动不能认定对齐。 |

表内源码名相对 `src/DevDuo/`，其余路径相对仓库根目录。测量布局、系统能力和实际观察像素是不同输入。

## 准备 macOS 运行

使用已解锁的真实 macOS 桌面、仓库 mise 环境，以及[贡献指南](CONTRIBUTING.md)要求的 Xcode/macOS 27 SDK。核实真实文件/目录符号链接及 `core.symlinks=true`。不要向 macOS 应用复制 Windows 运行时或改写 Windows 锁。

```sh
mise install
mise exec -- just setup
mise exec -- just doctor
sw_vers
xcodebuild -version
xcrun --sdk macosx --show-sdk-version
mise exec -- just verify
mise exec -- just test-macos
```

把退出码和完整日志保存在新本地会话。macOS 上 `verify` 会编译应用桥接。`build-native-tests` 编译包含两份 Swift 桥接文件的原生 harness；CI 编译不执行其断言，也不建立桌面验收结论。`test-macos` 依赖此构建并运行针对 AppKit 几何/输入策略的聚焦测试，不是浏览器端到端验收，不覆盖所有开发工具、首次请求 UA、持久存储、遮罩或最终显示。目前没有与 Windows 专项原生浏览器 harness 模式对应的 macOS 命令；执行下列交互案例，再按需要添加维护的自动化。

`NativeLayoutTests.swift` 现在先按实际系统能力检查旧页脚 surface 是否让位，再仅在测试内临时关闭能力 flag，独立检查 fallback 几何、被动命中与移除，最后恢复 flag，替代原先无条件要求 fallback 可见的预期。变更后的测试与构建 recipe 仍待 macOS 编译/执行；强制测试分支不认证系统扩展像素。呈现应用必须另行记录实际能力分支。

在新终端建立独立宿主偏好/证据会话：

```sh
session_dir="$PWD/captures/desktop-validation/platform-parity/$(date -u +%Y%m%dT%H%M%SZ)-macos"
mkdir -p "$session_dir"
export DEVDUO_PREFERENCES_PATH="$session_dir/preferences.json"
export DEVDUO_OUTPUT_DIR="$session_dir/states"
unset DEVDUO_URL
mise exec -- just dev
```

正常偏好不会被覆盖，但被测页面仍使用默认网站存储。不要清除无关网站数据。分别在独立终端启动夹具服务：

```sh
mise exec -- just serve
mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages
```

两条是独立长期运行的服务：8765 提供校准，8878 提供审核过的 HTML 夹具。通过真实地址栏打开 `http://127.0.0.1:8878/fixed-overlay.html` 或 `scroll-indicators.html`。若需要自定义请求头/重定向/代理记录器，放在系统临时目录，并将源码/哈希冻结进证据会话。静态服务本身不验证 UA 请求头、重定向或代理路由。

## 原生验收矩阵

对精确构建候选执行。除指定 Safari chrome 的案例外，覆盖 Safari 与沉浸模式。在每个折叠形态内保持模式不变，连续旋转四个 body 角度；切换模式/折叠形态属于独立生命周期案例。Safari 扩展覆盖全部 24 个初始/滚动状态，包括 Open 单/多示意标签。保留 Closed 180 度既有旋转顺序，独立确认实际内容方向，不能只看命令。

| ID | 操作 | 必需观察与证据 |
| --- | --- | --- |
| M01 启动与应用页面 | 使用全新隔离偏好且不设启动 URL，离线启动；执行 Home、Color demo、Calibration，再重启。在引导页切 English/简体中文/系统语言。 | 无 receiver 也能显示离线引导页，仓库/文档链接正确。只有校准依赖其服务并接收测量标签。引导页文本/标题原地更新，URL、文档身份/time origin 和滚动保持。另验记忆网站启动。每个新窗口 Developer tools 默认收起，同窗展开状态跨语言保持，颜色/校准操作位于组末尾。 |
| M02 原生 UA 与标题 | 记录首次 HTTP、重定向、子资源、fetch 请求头；将 `navigator.userAgent` 与 `BrowserIdentity.MobileUserAgent` 比对。测试延迟加载、动态/空/长标题及历史。 | 两种容器首次请求前即使用相同观察移动 UA，旋转后保持；不改实际 platform/DPR。provisional 期间选中示意标签不得泄露上个文档标题；提交后更新和原生省略保持完整标签/文本测量 frame。保留 HTTP 回执与呈现文本，不能只比较常量。 |
| M03 持久存储 | 在独立且稳定的 HTTP origin 写带 token 的 localStorage、IndexedDB 和持久 Cookie；正常退出，再以相同应用身份启动读取。分别测两个容器和工具前端。 | 被测存储跨应用进程保持，sessionStorage 不作为跨进程持久项。工具非持久存储独立。写入/读取值与 origin/token 留在本地。`DEVDUO_WEBKIT_PROFILE` 是 Windows 能力，不能声称隔离此测试。 |
| M04 文档保持 | 输入表单、建立历史并滚动；覆盖八个 fold/body 映射，切语言、主题、轮廓、Fit、Open 标签条设置。 | 此类呈现变化不导航/重建。比对 URL、time origin/文档 ID、表单/历史、实际 viewport、文档尺寸、范围和 fixed 矩形。旋转/viewport 增长可能夹紧滚动，记录原因而非脚本恢复。折叠/模式切换有意重建。 |
| M05 Inspector 与快捷键 | 分别让地址栏、原生页面、输入框、Inspector 持焦点，使用侧栏按钮、F12、Ctrl+Shift+I；需要 Fn 的键盘记录实际组合。关闭/重开并 resize。 | 独立 detach 窗口显示真实 DOM/Console，不占设备 viewport，无旧 Inspector 状态或重复前端。页面持焦点时通过原生菜单检查 ⌘L、⌘R、⌘[/⌘]、⌘S、⌘\\。缺少 selector 时明确报错。仅注册菜单不算通过。 |
| M06 原生远程检查 | 启停并在 Safari Develop 发现目标；远程开启时尝试本地 Inspector，反向也测；启动中销毁容器。 | 正确目标/文档、明确冲突处理、停止/销毁后移除。记录实际遇到的 Safari 发现配置要求。不能声称 macOS 存在宿主控制的 Windows TCP 端口或 token 保护的原生发现。 |
| M07 CDP 与 chii | 启用 CDP，打开打包前端，查看真实 DOM/styles 并计算无害表达式；导航、历史返回、旋转、关闭/重开前端、快速启停、销毁容器，含 HTTPS/CSP 页。 | 独立工具 WKWebView、正确目标、导航后重连；旋转保持文档。token discovery 和 WebSocket 路由连接真实页面。停止关闭前端/listener、移除未来注入；现有页面 hook 可能保留到刷新。旧回调不得访问已销毁视图。不要绕过上游 Console 粘贴保护。 |
| M08 代理 | 使用有回执的本地 HTTP、受信 HTTPS、SOCKS5 代理，应用后请求主文档/资源/fetch，再切 system；检查 direct/认证/非法输入被拒绝。 | 应用设置保持现有文档/time origin/滚动，后续请求经过所选代理且无静默直连 fallback。TLS ClientHello 后不受信证书失败仅证明握手尝试，不等于受信 HTTPS 会话。记录默认存储共享影响。 |
| M09 fixed/dialog alpha | 按下述夹具步骤覆盖 Closed 展开/紧凑及左右侧，含 Open 顶部与沉浸对照；在根背景/不透明页脚上显示、隐藏 fixed、元素 opacity、dialog backdrop。 | viewport 内 authored alpha 只合成一次，inset 使用对应原生 fixed-container 扩展色。记录实际能力分支、上下左右区域及前/中/后像素；无竞争宿主填充、残留变暗、文档尺寸变化或额外 padding。不支持的 fallback 案例须明确。 |
| M10 延伸与边界 | 使用 static-gradient、static-quirks、transparent、dynamic-height、live-glass 夹具，滚到 near/end、缩短/恢复文档、回顶并切紧凑态。 | 真正文档范围内保持实时 flow，范围外从根/背景开始，无重复行、黑条、旧像素。Closed 底部/侧边与 Open 已知预留 frame 近似分别观察。既采 DOM/原生状态，也采真实窗口。 |
| M11 滚动指示器 | 对 auto/短文档/强制 scroll/none/自定义宽度分别用全新 `scroll-indicators.html`；测滚轮、惯性、键盘、脚本、取消 wheel、端点/淡出。 | 记录实际系统偏好、原生滚动条位置/gutter，检查 CSS 抑制与 viewport/fixed/range 不变量。与 Device Hub 目标比对，但保留当前 macOS 宿主策略缺口。不得强改系统偏好或 CSS 制造屏幕边缘通过。 |
| M12 比例与原生输入 | 检查 1:1 与可达的约 100/94/75/50% Fit、窄窗口裁剪、resize/最小化/恢复、真实显示 backing scale/monitor 迁移。点击变换后的控件/链接，选字、拖动、使用输入法。 | 分开记录实际 NSWindow/NSView 点、backing 像素/比例、CSS viewport、scene 变换。无命中偏移、一像素缝、覆盖宿主控件或预览外原生子视图。请求 Fit 不等于实际 backing scale，macOS 点也不等于 Windows DPI。 |
| M13 生命周期与无障碍 | 导航/reload/stop/retry，快速切 fold/mode，在 Inspector/CDP/代理/导航工作未完成时关闭，重启重测；检查键盘遍历、VoiceOver、弹出层。 | timer/KVO/消息 handler/原生引用和服务可预测释放；无旧容器回调、崩溃或误删 profile。分别观察地址/菜单/reload/标签操作、被动 clock/Wi-Fi/camera，以及静态页跨分钟更新时间。不可用的无障碍/弹窗/显示器场景仍待验收。 |

M09 在 Inspector Console 使用夹具已有操作；这是 authored 测试输入，不是页面几何 workaround：

```js
setFixedOverlay(true, false, 'fixed');
setFixedOverlay(true, true, 'opacity');
setFixedOverlay(true, true, 'dialog');
setFixedOverlay(false);
fixedOverlayState();
```

逐条执行，每条等待 `fixedOverlaySettled === fixedOverlayRevision`，各自保存状态/窗口图。使用夹具暴露的 `fixedOverlayRootRgb`、`fixedOverlayRgb`、`fixedOverlayAlpha` 重测彩色背景/遮罩与部分透明 alpha。在无遮挡 viewport 采样点，按已声明 RGB 容差核对 authored 前景/背景 alpha 方程；inset 则按对应根/页脚/背景案例核对 resolved extension 色，不要求重复页面纹理。玻璃/chrome 遮住的像素不能声称直接采到页面，须单独保留最终窗口观察。祖先 group opacity、任意 filter 和所有网站 stacking context 不能由此夹具泛化。

M11 夹具提供 `indicatorFixture.configure(short, forceScroll, suppress, authorStyle)`、`trapWheel(enabled)`、`snapshot()`。覆盖 `authorStyle` 的 `''`、`display-none`、`zero-width`、`subpixel-width`。抑制后须用全新文档恢复正对照；复用页历史曾污染 Device Hub 会话。稀疏采图不证明精确通用淡出时间或物理厚度。

## 证据与验收报告

原始图与失败尝试留在新 ignored 会话。每次变换前后 Save state，实时内容/滚动期间采实际呈现窗口（例如 macOS 交互窗口截图）。WK snapshot 可能遗漏 obscured inset 下延伸。原生 snapshot/source 像素、GPU 提交、最终窗口像素是不同观察；动画 callback 时间不是桌面 FPS。

每个案例记录 ID/结果、精确源码 revision 与 dirty diff/hash、应用/原生桥接/资源哈希、构建命令和退出码、实际 macOS/SDK/WebKit 版本、原生显示/backing scale 与本地/远程环境、夹具 URL/源码哈希、fold/body/scene 角、模式、标签变体、语言/外观、时间戳、DOM 文档身份/time origin/URL/滚动/尺寸/viewport/fixed 矩形、实际原生 frame/inset、图名/哈希、预期/实测差异及限定。可用时从已安装系统 framework 取得实际 WebKit 构建元数据，被覆盖的 UA 不能提供此信息。冻结临时记录器源码。token 和原始浏览记录保密；只发布审核后的相对路径派生物，不含账号、主机名、私有绝对路径或模拟器 UDID。

可在 Save state 之外附上只读 Inspector 样本，每次操作前后各采一次；夹具专用 state 函数补充 authored 回执与 fixed 矩形：

```js
JSON.stringify({
  url: location.href, title: document.title, timeOrigin: performance.timeOrigin,
  documentId: window.devduoShellObservation?.documentId,
  userAgent: navigator.userAgent, dpr: devicePixelRatio,
  inner: [innerWidth, innerHeight],
  visual: [visualViewport.width, visualViewport.height, visualViewport.scale],
  scroll: [scrollX, scrollY],
  document: [document.scrollingElement.scrollWidth, document.scrollingElement.scrollHeight],
  fixed: Array.from(document.querySelectorAll('*')).filter(element =>
    ['fixed', 'sticky'].includes(getComputedStyle(element).position)
  ).map(element => ({ id: element.id, rect: element.getBoundingClientRect().toJSON() }))
});
```

逐项把 M01–M13 标为通过、失败、不支持或未运行，并给具体证据引用。没有新审核证据时，不能把已知实现差异直接记作已接受的 Safari 偏差。保留八个映射、24 状态及 Closed 180 度限定。物理毫米、真实硬件圆角、iOS Liquid Glass 参数继续未知/`null`，桌面玻璃调参和模拟器像素不能提供这些值。

当前 macOS 待办是精确版本构建/聚焦测试、原生存储/身份/标题/引导页、当前场景/本地化变更的保持行为、Inspector/远程/CDP/chii/代理生命周期与路由、系统 fixed-container alpha/dialog 呈现、滚动条策略对齐、backing scale/输入/无障碍和销毁。Open 延伸与缺失的宿主屏幕边缘指示器属于待研究的实现差异，不只是没跑的测试格。[Windows 原生结果及剩余门禁](009-WINDOWS-HANDOFF.md)独立保留。

[English](../en/015-PLATFORM-PARITY.md) | [中文](015-PLATFORM-PARITY.md)
