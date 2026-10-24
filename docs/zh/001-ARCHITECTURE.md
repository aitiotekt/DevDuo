# 架构与浏览器容器

Avalonia 管理宿主窗口和设备/场景选择。`NativeWebView` 在 macOS 中嵌入系统 WKWebView，在 Windows 中嵌入固定的 Windows WebKit Port。平台渲染器将页面与 Safari 示意组件组合在独立屏幕遮罩内；关闭外壳图形不会移除该遮罩。

## 代码分工

| 层 | 源码 | 职责 |
| --- | --- | --- |
| 宿主 | `src/DevDuo/Host/`、`src/DevDuo/App.axaml`、`src/DevDuo/Styles/` | 主窗口、地址/设置/状态控件、本地化、偏好和预览缩放 |
| 浏览器 | `src/DevDuo/Browser/` | 原生视图生命周期、共享导航/标题状态、内置页选择、标定标签及滚动/外壳策略 |
| 开发工具 | `src/DevDuo/DeveloperTools/` | 原生 Inspector 协调、CDP 传输、代理约定及独立系统 WebView 工具窗 |
| 场景模型 | `config/`、`src/DevDuo/Device/` | 路径、独立的机身/画布和内容/机身变换、实测场景映射、组件/标签框与呈现选项 |
| macOS | `src/DevDuo/macos/WebViewBridge.swift`、`src/DevDuo/macos/DeveloperWebViewBridge.swift` | 系统 WKWebView、原生玻璃/裁剪/inset/取色及独立开发工具 WebView |
| Windows | `src/DevDuo/windows/` | WebKit C 宿主绑定、物理屏幕/输入变换、裁剪、逻辑场景/backing 比例及原生 DirectComposition 合成器 |
| WebKit 运行时 | `native/webkit/windows/` | 固定引擎锁、生产宿主补丁、源码构建与运行时归档支持 |
| 应用网页资源 | `src/DevDuo/WebAssets/Pages/`、`src/DevDuo/WebAssets/Scripts/` | 随应用复制的离线欢迎/颜色页、被动浏览器观察器及页面调试引导脚本 |
| 标定 | `web/`、`scripts/python/`、`scripts/swift/`、`scripts/DeviceHubUITests/` | 浏览器记录、真实 Safari 组件框、采集和比较 |

接收器仅提供固定标定资源并保存本地 JSON，不控制应用或执行页面提供的命令。环境与命令职责统一见[贡献指南](CONTRIBUTING.md)。

可移植测试按职责分开：`tests/DevDuo.Core.Tests` 检查共享状态和实测不变量，`DevDuo.Localization.Tests` 检查资源与语言行为，`DevDuo.Debugging.Tests` 检查真实回环 CDP 传输。`DevDuo.Windows.Tests` 负责原生应用验收，场景放在 `Checks/`，生命周期/服务辅助代码放在 `Harness/`，证据采集放在 `Evidence/`；`DevDuo.WebKitProbe` 负责独立引擎诊断。编译和可移植测试不能证明原生验收通过，参见[平台对齐与macOS验收](015-PLATFORM-PARITY.md)。

## 模式与交互

- **Safari 近似模拟**添加页面衍生颜色的悬浮组件，随实际滚动改变内容视口。展开态提供单/多标签变体；开关表示外壳几何，不管理真实标签页。
- **应用 WebView 沉浸模式**移除浏览器组件，使用完整逻辑场景。外壳开关不改变原生屏幕裁剪。

全新配置默认 Safari、折叠、机身 0°、1:1；已保存的宿主设置会恢复其它选择，但每次启动机身角度均为 0°。八个测量映射决定实际场景，包括 Closed 180° 的旋转历史限定。适应预览跟随可用预览区域，且不超过 1:1。macOS Fit 缩放原生坐标空间并保留 CSS 尺寸，Windows Fit 调整原生 backing 比例、page zoom 固定为 1，并保留逻辑 WebKit 视口。视口、标签栏预留与选中标签几何统一查阅[场景矩阵](005-SAFARI-SCENES.md)。部分展开透视模型尚不可用。

Back/Reload 使用原生导航，其它图示组件仅有有限演示交互。两种容器使用已记录的移动 Safari 请求身份；真实引擎、platform、DPR、方向、safe area 和键盘仍保持原生行为。Safari 外观不代表引擎变成 iPhone Safari。

机身角度在预览画布中旋转外框；内容相对机身采用独立变换。实测场景保留其原始逻辑尺寸、Safari 组件框和视口预留。`NativeWebView` 接收物理屏幕尺寸、裁剪偏移和最终的场景到画布四分之一圈旋转。Windows 在 composition API v3 旁增加 presentation API v1，旋转完整的实时页面/材质/字形树。旋转场景将完整尺寸、cloak 的 WebKit 子窗口放在应用拥有的 `WS_EX_NOREDIRECTIONBITMAP` 源宿主中，防止较小物理屏幕的 HWND 祖先在旋转前裁掉原生绘制。源宿主不产生桌面像素，随合成器释放。指针坐标和无障碍矩形使用逆向/正向变换；Avalonia 逻辑焦点委托给真实 WebKit HWND。macOS 旋转原生 NSView frame，由 AppKit 完成输入坐标转换。参见[双坐标系约定](005-SAFARI-SCENES.md#机身与内容坐标系)。

## 宿主控件与导航

宿主控件主题由 Avalonia 12 上的 SukiUI 7 提供；`Styles/DevDuoStyles.axaml` 补充浅色/深色 token、间距、字体层级、图标和组件样式类。这些样式只作用于 Avalonia 宿主控件；设备外框、屏幕遮罩、Safari 组件、标签文字和视口预留仍由平台渲染器和测量配置负责。宿主界面使用不透明背景并位于独立的网格单元。不使用 SukiUI toast、对话框及其它窗口内覆盖层，因为 Avalonia 内容无法绘制在原生浏览器子视图之上。Avalonia 只移动原生子视图，不会按滚动祖先裁剪，因此原生视图不放在预览滚动区内：透明占位随外框滚动，原生视图位于其上方的独立层，尺寸为占位的可见部分（不遮挡滚动条）。原生裁剪视图（macOS 的 `devduo_set_viewport`、Windows 的父窗口）在内部偏移完整尺寸的容器，因此滚动时容器框、场景比例、CSS viewport 和遥测均不变。

宿主地址栏绑定当前原生视图。macOS 观察 WKWebView 的 `url`、`title`、`isLoading`、`estimatedProgress`、历史标志和导航失败，并通过 `devduo_set_navigation_callback` 回调；Windows 使用普通 WebKit C 导航回调，并在 UI 线程轮询 URL、标题、进度与历史元数据。回调投递到 UI 线程，原生容器销毁后即被忽略；被取消或被后续导航取代的加载不算失败。地址必须为 http/https：缺少协议时，回环、`.local` 和私有网络主机补 `http://`，其它主机补 `https://`；其它协议直接拒绝。query 和 fragment 原样保留。测量标签（`device`、`pose`、`browser`、`state`、`displayMode`、`notes`）只附加到本地接收器页面（回环地址、端口 8765、路径 `/`），会替换旧标签，且不在地址栏显示。

| 变化 | 原生视图 |
| --- | --- |
| 语言、主题、多标签页栏、材质调节、外框 | 原地重新配置或移动，保留页面、历史和滚动位置 |
| 预览缩放 | 原地调整尺寸；标定标签通过历史状态更新，不重新加载 |
| 机身角度 | 在同一原生视图内更新遮罩、逻辑几何和实时呈现，保留文档、表单/JavaScript 状态及历史；新视口可能将滚动位置限制到有效范围 |
| 设备形态、容器模式 | 在当前网站、欢迎页或颜色实验室重新创建；滚动和历史重置，状态栏给出说明 |

快捷键：⌘L/Ctrl+L 聚焦地址栏，⌘R/Ctrl+R 或 F5 重新加载，Esc 停止加载，⌘[ ⌘] 或 Alt+←/→ 浏览历史，⌘S/Ctrl+S 保存状态，⌘\\/Ctrl+\\ 显示或隐藏设置。macOS 同时在原生**导航**菜单中列出这些命令，因此 WKWebView 持有焦点时仍可使用。Windows 通过原生 WKView 键盘消息和面板命令键链转发快捷键；IME 与完整键盘遍历仍待验收。宿主控件具有本地化的无障碍名称、带快捷键的提示，以及禁用项的说明文字；状态消息为 polite live region。

`Host/PreferenceStore.cs` 将宿主设置保存在 `DevDuo/preferences.json` 的可选 `host` 对象中：`theme`、`containerMode`、`fold`、`previewScale`、`deviceOutline`、`showTabStrip`、四个材质值（null 表示沿用 `config/safari-shell.json`）、面板展开状态和 `lastUrl`（仅 http/https）。各字段独立校验，无效字段使用默认值并给出说明。机身角度从不保存，因此折叠 180° 只能经由记录的顺时针序列到达。有效的 `DEVDUO_URL` 优先于记住的网站；两者都没有时直接加载内置离线欢迎页，没有先行网络导航。主页和颜色实验室是独立的内置页面，不覆盖记住的网站；链接来自项目元数据。标定是开发者工具区的显式操作，需要接收器服务。**重置设置**恢复默认值和机身 0°，不改变语言和当前页面。文件格式与语言行为见[本地化](011-LOCALIZATION.md)。

活动示意标签显示当前已验证文档的真实标题，并跟随动态标题更新。空白或尚未确认的标题回退为本地化的通用页面标签，非活动槽位仍为示意。原生文字排版在现有宽度内使用尾部省略，模型和 Windows 无障碍名称保留完整标题，不改变标签容器、关闭控件或字体几何。

## 开发工具与页面代理

开发工具是设置侧栏最顶部的折叠区，每次启动默认折叠。区内依次提供原生 Inspector、原生远程检查、CDP/chii 和代理控件，最后放置颜色演示、标定操作及标定服务提示。展开状态属于当前窗口会话，切换语言或重新应用设置时保留，不恢复也不保存；旧 JSON 的 `developerExpanded` 作为未知字段兼容忽略。侧栏可见性、材质和诊断的展开状态继续保存。原生本地检查使用独立窗口，避免停靠改变实测视口。本地与原生远程 Inspector 互斥，页面 CDP 独立运行。两项远程服务每次启动均关闭；监听/局域网偏好、代理配置和明确填写的 CDP 请求头令牌会保存，启用状态和临时前端凭据不保存。

| 路径 | 传输与范围 |
| --- | --- |
| 原生本地 Inspector | 独立平台 WebKit Inspector 窗口，引擎能力取决于构建。 |
| Windows 原生远程 | `hostDeveloperApiVersion: 1` 可启动/停止 WebKit Inspector TCP 服务。`webkit-inspector-socket` 地址需要兼容 WebKit 客户端，不是 Chrome CDP/WebSocket。原生 TCP 不带 DevDuo token 认证。 |
| macOS 原生远程 | 通过公开 `WKWebView.isInspectable` 启用 Safari 开发菜单发现，不创建自定义端口或局域网监听。 |
| 页面 CDP | `DeveloperTools/CdpDebuggingService.cs` 提供 HTTP/前端/发现及单页面 WebSocket。`WebAssets/Scripts/remote-debugging.js` 包装 chobitsu 1.8.6，`scripts/package-debugging.mts` 打包 chii 1.15.5；安装后不需要 Node 进程或 CDN。 |

CDP 默认只绑定回环，显式选择局域网后才开放活动 IPv4 接口。基础地址及路由固定；回环监听可留空可选请求头令牌，局域网 CDP 必须设置。配置凭据后，发现接口和页面 socket 要求 Authorization bearer 请求头。公开前端静态资源不含目标页数据，内置前端另获每次启动独立的同源 socket 访问能力，不使用持久令牌作为 URL 参数。相对于显示的基础地址，`json/list` 和 `json/version` 提供带范围说明的发现，`devtools/page/devduo` 接收原始 CDP WebSocket 消息，`front_end/chii_app.html` 提供真实 chii 前端。未知 Host 和跨源浏览器 WebSocket 会被拒绝；第二个调试器会被拒绝，避免共享 ID。报告的协议版本只是传输约定，不代表完整 CDP 支持。

完整离线脚本在原生 document start 安装，并立即在当前页执行。响应/事件走专用原生 script-message handler，命令由原生求值投递到消息桥。目标页不创建调试 WebSocket，也不加载远程调试脚本，因此不依赖 HTTPS 混合内容或 `connect-src` 放行；单项操作仍可能受 CSP、跨源资源及上游实现限制。这是 page world 插桩，不是浏览器安全边界。

chobitsu 在其支持范围内提供 DOM/CSS、Console/Runtime、Storage 及观测到的 Network 操作，不能提供原生 JavaScript 断点、CPU/堆分析、完整导航/资源流量、浏览器级目标或等价引擎协议；部分上游兼容命令为空操作。中途启用无法补回此前的 Console/Network 观测。新文档会重置传输，只刷新已有 chii 工具窗口以重连；外部客户端自行重连。同文档重复就绪不会刷新工具。停止时关闭 socket/监听、拒绝过期回调并移除专用后续脚本，不移除被动测量观察器；当前桥会静音，已安装的 JavaScript hook 需用户明确刷新目标页才会消失。

代理更改保留文档、视口和滚动，为后续请求配置原生网站数据存储。Windows 支持系统、直连和自定义。macOS 通过公开 `WKWebsiteDataStore.proxyConfigurations` 支持系统/自定义，空列表恢复系统行为，不能强制直连，因此直连明确报告不支持。自定义 `http`、`https` 或 `socks5` 端点带端口，不含凭据、query 或 fragment；认证/PAC 不在此 UI 范围。已有连接可能按原生网络规则继续使用。macOS Inspector、注入及代理更改仍需 macOS 原生验收。

## 悬浮组件下方绘制

已验证的 macOS 27 折叠模式保留完整 WKWebView，通过公开 `obscuredContentInsets` 调整页面视口。466 × 678 原生框设置侧边 84、底部 98，得到 382 × 580 页面视口；紧凑底部 80 得到 382 × 598。普通流继续绘制到地址栏下方，fixed-bottom 保持在较小视口内；270° 场景将侧边 inset 移到左侧。

`minimumViewportInset` / `maximumViewportInset` 表示底部 80…98 范围。独立探针测得 100svh = 580、100lvh = 598、100dvh = 580/598。沉浸模式清空 inset。Fit 只缩放呈现，不改变上述 CSS 尺寸；状态区分真实视口、绘制框、DIP 与遮挡 inset。

macOS 展开仍采用较小原生内容框，顶部延伸为衍生颜色近似。[Windows WebKit 后端](014-WINDOWS-WEBKIT.md) 使用完整原生绘制框、真实四边 obscured inset 及独立的逻辑视图/backing 比例，Fit 时 page zoom 保持为 1。当前 WK runtime 的 `takeSnapshot` 会遗漏真实窗口可见的延伸，因此必须同时检查实际窗口。归档中的 [inset 探针](../../scripts/archive/README.md)及本地原始 `captures/desktop-validation/under-chrome/` 记录保留了这一差别。

API 参考：[WKWebView obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets)、[WebKit viewport 支持](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/)。

WKWebView 保留原生 canvas/页面边界外背景绘制。系统能力可用时，macOS 桥接启用系统 content-inset 背景填充实现，并在该路径移除宿主自己的不透明底部/侧边表面，使用引擎的固定容器和 backdrop 颜色延伸，不改变 DOM 样式、视口预留、fixed 框或滚动范围。变更后的 macOS 路径仍需原生验收。私有能力缺失时，诊断明确记录限制，保留旧的只读 DOM 不透明通栏 fixed 底栏近似；该回退不能证明半透明遮罩兼容。Windows 使用明确的[原生四边颜色 API](014-WINDOWS-WEBKIT.md#半透明-fixed-遮罩)。[标定指南](003-CALIBRATION-WORKFLOW.md#半透明弹窗背景)分别记录 Safari 目标行为和桌面验收。

原生位置在 Avalonia 布局后的 `Loaded` 调度优先级统一同步，包括裁剪/内容配置。AppKit 容器关闭隐式子视图缩放，并在自身框尺寸变化时重新布局，使冷启动、resize 和 Fit 时屏幕内容与外框保持对齐；`nativeClipGeometry` 记录完整容器、逻辑 bounds 和裁剪偏移。详见[验收记录](003-CALIBRATION-WORKFLOW.md)。

## 材质与诊断

macOS 通过同一贝塞尔遮罩裁剪 WKWebView 和兄弟 AppKit 视图，避免普通 Avalonia 控件覆盖原生浏览器。当前 macOS 27 包使用 `NSGlassEffectView` clear/tint；较旧 `NSVisualEffectView` 分支需要降低部署目标重编并另行验证。着色、材质贡献、阴影和半径可调，iOS Liquid Glass 私有模糊/折射内核仍未知。

外壳外观独立决定 Light/Dark 字形、材质及原生页面配色；即使存在相反 theme-color 提示，两端也采样 WebKit 实际边缘条带，meta 只作诊断，computed background 仅作标明的回退。Windows 内容取色 API v1 通过普通快照 IPC 仅返回 RGB/状态，图像不进入页面渲染。采样异步防抖，可能滞后移动内容；导航、文档和几何代次拒绝过期结果。D3D11/DirectComposition 桥接 WebKit HWND 的实时重定向表面，先高斯模糊和色彩处理再圆角裁剪，并用 Direct2D/DirectWrite 分层绘制着色、定向高光、边框、阴影和清晰字形。被动摄像头/Wi-Fi/时钟示意及独立外壳动作遵循 macOS 场景规则。文档外条带遵循根背景规则，与工具栏着色独立；有效横向正文继续可见。更广泛的输入、DPI 和实际呈现仍是独立门槛，见 [Windows 实现及限制](014-WINDOWS-WEBKIT.md#实时表面合成)。

**颜色演示**是用于主题色及内容取色的独立夹具。**保存状态**和 `window.devduoContainerTelemetry` 提供请求/实际框、滚动/chrome 状态、可见与点击边界、颜色来源/RGB 和材质参数。`previewScale` 记录实际呈现比例。源码检出写入 `captures/desktop-validation/container-modes/`，独立安装使用本地应用数据的 `DevDuo/captures`；`DEVDUO_OUTPUT_DIR` 可覆盖位置。

测量语义与来源统一见[测量约定](002-MEASUREMENT-CONTRACT.md)，验收和已有平台边界见[标定与验证](003-CALIBRATION-WORKFLOW.md)。后续宿主控件与实测 Safari 几何分开推进，见[路线图](100-ROADMAP.md)。

宿主窗口拥有的 `ControlApiService` 使用 Kestrel 提供独立、默认关闭的本机 REST 监听。校验后的命令在 Avalonia UI 调度器上调用相同的导航/设置操作，不持有原生句柄，窗口关闭后不再执行。切换设备/容器保留监听。离线 Swagger UI 和 OpenAPI JSON 约定见[使用指南](004-USAGE.md#rest-控制-api)。

[English](../en/001-ARCHITECTURE.md) | [中文](001-ARCHITECTURE.md)
