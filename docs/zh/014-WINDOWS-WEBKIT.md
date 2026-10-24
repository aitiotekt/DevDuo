# Windows WebKit 内容延伸后端

独立 C++/Win32 调研使用官方 Windows WebKit 构建；Avalonia 应用唯一使用 Windows WebKit Port，基于同一固定源码及维护中的非测试宿主 API，不再提供后端选择或其它引擎。Safari 对照及实现目标统一见 [Windows 交接](009-WINDOWS-HANDOFF.md#文档末端延伸调研2026-10-22)。

## 运行时与复现

生产引擎输入位于 `native/webkit/windows/`：源码 pin、三个宿主补丁、triplet、构建/缓存管理与归档工具。`tests/DevDuo.WebKitProbe/` 仅负责独立诊断程序、测试页面和验收入口。维护中的 `BuildRuntime.ps1` 是生产构建入口，不属于测试基础设施，见[目录职责说明](../../native/webkit/windows/README.md)。已有冻结输入保留原路径和哈希；目录迁移不代表引擎重新编译或重新通过验收。

`native/webkit/windows/runtime.lock.json` 固定官方 release `324423@main`、提交 `6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d`、归档长度 589,666,207 和 SHA-256 `b3a06dc5b5461976090fe889acb1022f28ad0ae1f27f40eef83f2916f4c77d4f`。原始 ZIP 留在 `temp/cache/windows-webkit/`，可运行文件留在 `temp/windows-webkit-probe/`。归档提供 DLL、辅助程序和资源，并非嵌入 SDK。探针按照同一源码提交声明最少 C 接口并检查导出。该 port 默认使用 Skia 和 ANGLE。

官方归档报告于 **2026-11-07** 到期。准备步骤在归档不可用时明确失败；需保留缓存，或有意更新固定版本。不进行全局安装、Playwright 替代或发行包捆绑。参见[第三方声明](THIRD_PARTY_NOTICES.md)。

在已解锁桌面、安装 Visual Studio C++ 和 v143 toolset 的 Windows 环境运行：

```powershell
mise exec -- just probe-windows-webkit
```

此命令准备运行时，编译到 `temp/build/windows-webkit-probe/`，执行导航前应用 inset 的矩阵，再停留在实时用例。网页提供 footer、接近/到达末端、短文档/增长文档控件；原生 **Inset 98/80**、**Save observation**、**Native repaint** 为独立控件。右侧测量只在保存观察时更新。结束后关闭窗口。

也可分别运行 `just prepare-windows-webkit-probe`、`just build-windows-webkit-probe`，随后：

```powershell
& ./tests/DevDuo.WebKitProbe/RunProbe.ps1 -StartInset
& ./tests/DevDuo.WebKitProbe/RunProbe.ps1 -StartInset -Hold -Raw
```

第一条在自动布局诊断后退出；第二条关闭原生颜色补偿，供对照观察。`-ForceCompositing`、`-SiteIsolation`、`-ExtendBackground` 保留先前实验；偏好读回不能证明实际 GPU 路径。每次启动均在新的、被忽略的 `captures/desktop-validation/windows-webkit/` 会话中冻结源码、运行时/程序哈希、选项及 DOM/原生记录，失败尝试单独保留。退出码为零只表示诊断执行完毕，并非完整窗口验收通过。

## 真实延伸与原生边界补偿

本节的兄弟窗口补偿属于独立诊断。应用使用遵循相同文档规则的 GPU 边条，并配合下文的引擎绘制补丁和实时表面合成器。

实测 192 DPI 下，`WKViewCreate` 保持 764 × 1356 物理像素子窗口，对应 382 × 678 DIP。真实底部 inset 98 得到 inner/client/visual 382 × 580、fixed 底部 580；inset 80 得到 382 × 598、fixed 底部 598。首次导航前即应用 inset，编号正文及独立动画计数器确实绘制在较小视口下方。没有渲染截图，没有替换 DOM 尺寸 getter，实际 UA、DPR 和 screen 报告保持桌面原生值。

未经补偿的 port 在文档末端进入该区域后保留旧行号。2400 像素页面从 scrollY 1722 之后开始，最大 Y 1820 时最明显。原生强制重绘、背景延伸及偏好实验没有消除它；直接在绘制后使用 GDI 补画也未建立可靠的呈现修正。这些失败尝试均有保留，不将其当作已确认的引擎根因。

`ContinuationSurface.h` 现增加一个位于不透明浏览器上方、地址栏下方的被动原生**实色兄弟窗口**。共享浏览器 observer 的只读消息提供真实文档高度 D、滚动 Y 和视口 V。底部 inset 为 B 时，实际有效正文延伸长度为 `L = clamp(D - Y - V, 0, B)`。颜色层只覆盖 `[V+L, V+B)`，WebKit 继续绘制 `[V,V+L)`。Y 1770 时保留 50 像素真实正文，仅填剩余 48；Y 1820 时全部 98 为背景。CSS 坐标按实际原生客户区及视口/inset 尺寸转换，不假定 DPI。颜色层不会激活窗口，命中测试透明。

这里使用的 C 接口尚未提供经过验证的 resolved under-page 颜色 getter，因此填色明确报告为**不透明 html/body CSS 回退，否则白色**，独立于工具栏 theme tint。对于不透明、实色、可见且通栏的 fixed footer，复用共享 observer 的资格判定，可为整个 inset 延伸颜色；窄标记或移除 footer 会清除此独立近似。没有泛化渐变、alpha 背景或 Safari 颜色历史启发式。两种补偿均不改变视口预留、fixed 框、文档尺寸或滚动范围；夹具改变行数是明确的测试输入，从未充当实现规避手段。

矩阵记录两种 inset、接近/到达末端、通栏/窄/移除 footer、短文档、增长/缩短及滚离/返回。原生记录区分有效正文长度、填色坐标/颜色、可见性、窗口框和绘制次数；实际窗口滚轮及实时动画观察补充这些记录。兄弟颜色层修正最终宿主场景，**没有**修复 WebKit 内部保留的 backing 像素。

## 普通 HTTP 页面与四边 inset

源码构建后，`just test-windows-webkit` 通过正式宿主 API 比较 152 组，即八个页面各 19 种情况，包含原先 39 组。新增静态渐变、透明根背景、静态 quirks、文档高度及实时动画页面，覆盖 HTTP/HTML 字符串加载和 100%/94%/75%/50% 呈现。静态夹具不含 fixed 标记或注入布局元素。预编译诊断仅使用其支持的 1:1 情况；`RunAcceptance.ps1 -Hold` 保留最终窗口供输入检查。`acceptance-observer.js` 读取真实 inner/client/visual 尺寸、文档范围、滚动范围/位置、fixed/sticky 框、页面自有视口单位、文档标识及原生 UA/DPR/screen。比较通过只证明几何，不证明像素正确。

每组先使用没有 inset、真实逻辑尺寸为 382 × 580（compact 为 598）的原生视图，再放大绘制窗口并应用右/下、左/下、顶部或四边组合 inset，保持布局视口完全一致。实际窗口补充检查左侧/顶部切换、滚轮、普通链接导航、文本输入和视口下方动画。这是本地 HTTP 页面验收样本，不代表普遍兼容性、IME 或无障碍验收。

被动颜色兄弟窗口现使用原生 region，围绕二维有效文档矩形填充。以 `(left, top)` 为视口原点，上/左方向按当前滚动距离保留真实正文，下/右方向按文档剩余范围保留真实正文，仅填充超出文档的边条。通栏 fixed footer 的颜色延伸仍是独立的、仅限底部的近似。共享 observer 使用 `document.scrollingElement`，支持 quirks 模式的 body。Inset 换边可能完全不改变 DOM 尺寸，因此宿主在配置后显式请求一次只读观测；仅监听 resize 会留下旧的侧边像素。观测与原生绘制仍为异步。

## 宿主 API 源码构建

[独立手动运行时发布](013-RELEASE.md#windows-webkit-运行时发布)复用自托管 runner 的持久构建树。应用发布消费 metadata 固定的归档，不重新编译 WebKit；本地源码构建继续可用。发布工具检查与原生验收分别记录。

官方预编译 DLL 仅导出测试 inset setter。维护的 `patches/0001-windows-host-viewport-api.patch` 增加带版本标识的 Windows 宿主 C API：`WKViewSetObscuredContentInsets` 检查 inset 为有限非负值且不超出原生逻辑视图，再调用 `WebPageProxy::setObscuredContentInsets`，更新 proxy 持续配置并使用普通 `WebPage::SetObscuredContentInsets` IPC。`WKViewSetUnobscuredSizeRange` 将真实默认/最小/最大 unobscured 尺寸传入引擎的视口单位实现。补丁不开启测试 IPC，也不修改 JavaScript 几何；这是 DevDuo 的 port 扩展，尚不是上游产物已有的 API。

宿主 API **v2** 另增 `WKViewSetHostPresentationGeometry`，设置真正的逻辑场景尺寸及物理 backing 比例，原生绘制和鼠标/滚轮坐标转换共用该比例，从而分开呈现与布局，无需修改 DOM 样式或 getter。先前 page zoom 实验失败：50% 使小/大视口单位翻倍，75% 暴露 port 的整数 DIP 取整，实际 visual 宽度变成 381.33 像素。失败记录保持原样。v2 将 page zoom 保持为 1，提供真实引擎视图尺寸，在非整数 Fit 下仍保持 382 × 580/598、fixed 框及滚动范围；DPR 报告实际选定的 backing 比例。

同一呈现补丁还对物理失效矩形向外取整、backing 尺寸向上取整，并将原生栅格原点对齐设备像素。分数缩放后截断逻辑矩形，会让文档缩短后的最后一行/列保留旧像素。这些修改只作用于 Windows 的栅格分配/裁剪，不改变布局或文档尺寸。

`patches/0002-windows-content-painting.patch` 增加**正文绘制 v1**。此前非合成路径的 `ScrollView::paint` 和 `repaintContentRectangle` 裁剪到未遮挡布局视口，使未经修改的静态颜色页下方出现黑块。Windows 专项绘制补丁将画布裁剪扩至 obscured inset，并在绘制原点包含顶部/左侧 inset。画布绘制必须越过缩短文档的精确边界，才能更新分数边缘像素；宿主独立裁剪有效文档流，只填充文档外区域。布局、命中测试、fixed/sticky 位置、视口单位和滚动范围不变，不依赖 fixed 标记、页面样式修改、动画或反复强制重绘。冻结构建输入记录全部维护补丁；启动要求 viewport API v2、正文绘制 v2 和内容取色 API v1；旧运行时必须重建。

同一补丁还修正保留像素的滚动拷贝区域。普通绘制修正后，部分水平滚动仍使左 inset 暴露黑块，因为 `windowClipRect` 和拷贝矩形依然排除了遮挡区域。拷贝现覆盖完整原生绘制范围，并显式失效软件 overlay 滚动条的原位和拷贝后位置，避免排除条带留下黑块或移动的滚动条轨迹。这是实际滚动时的引擎失效处理，不是重绘定时器或强制重绘循环。左 inset 84、scrollX 40 的回归保留 40 CSS 像素真实正文和 44 像素根背景。

原生构建 `mise.windows-webkit.toml` 环境及锁文件选择便携 CMake、Ninja、Ruby、Strawberry Perl、gperf、pkgconf 和 vcpkg。Python 仍以正常 mise 环境选择的版本线为准；自定义 vcpkg 宿主/目标 triplet 显式传递这些解释器与生成器，防止另一个嵌入式 Python 接管。Visual Studio C++/Clang 和 Windows SDK 仍作为系统依赖检测。vcpkg 管理上游锁定的库清单/registry 及依赖内部构建辅助工具。GnuWin32 gperf 没有更新的便携 Windows 发行版，因此使用固定、校验过的归档。

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
& ./tests/DevDuo.WebKitProbe/RunAcceptance.ps1 -HostApi -RuntimeDirectory ./temp/build/windows-webkit-host/bin -Hold
```

源码/依赖缓存位于 `temp/cache/windows-webkit/`，构建位于 `temp/build/windows-webkit-host/`。构建记录输入，显式选择 C++ 前进/后退列表实现（`ENABLE_BACK_FORWARD_LIST_SWIFT=OFF`），避免另行要求 Windows Swift SDK；不得将这些 DLL/辅助进程与官方启用 Swift 的运行时混用。`-HostApi` 强制要求带版本的导出，并保持 test-only IPC **关闭**；缺少导出时直接失败，不会静默使用诊断 setter。补丁及构建成功均不足以代表验收：源码构建运行时必须先通过矩阵和实际呈现检查，才能接入 Avalonia。

## Avalonia 后端

独立探针通过后，`WindowsWebKitApi.cs` 和 `WindowsWebKitView.cs` 将真正的子 WKView 嵌入原有原生场景面板。首次构建必需运行时，再启动应用：

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
mise exec -- just dev
```

应用自动查找源码检出中的 `temp/build/windows-webkit-host/bin`。独立安装可在可执行文件旁提供完整 `webkit/bin` 与 `webkit/runtime-manifest.json`，或将 `DEVDUO_WEBKIT_RUNTIME` 设为绝对 bin 目录。显式覆盖具有优先权，路径无效时不会选择其它运行时。WebKit 要求清单中的 API 版本与全部绑定导出，启动时不校验文件哈希；缺失能力时明确报错，test-only IPC 始终关闭。Windows 使用当前用户本地应用数据下 `DevDuo/WebKit` 的持久网站存储；若设置 `DEVDUO_WEBKIT_PROFILE`，必须使用绝对目录。多个原生视图共用同一存储会话。localStorage、IndexedDB、持久 Cookie 和网络/网站缓存跨应用重启保留；sessionStorage 维持正常的文档会话语义。旧浏览器引擎的 profile 不会被迁移或删除，重置宿主设置不会清除网站数据。

`WindowsWebViewPanel.cs` 继续管理同一套外壳、遮罩和场景几何。WebKit 使用完整原生绘制框，接收真实四边 inset 及视口单位范围；Fit 只改变原生 backing 比例，保持引擎逻辑尺寸不变。普通 C 导航回调驱动 URL、标题、加载、错误及历史状态，原生焦点快捷键转发给宿主。脚本操作及导航代次/文档检查拒绝陈旧或已销毁的回调。JSON 字符串消息传输保留了 C 字典转换会丢弃的 null 字段。

Windows 顶层宿主保持不透明。文档末端因视口增高发生的滚动夹紧不会被当成工具栏收起手势。Safari 控件使用下面的实时表面合成路线，已移除透明 WinForms 控件窗口和整控件 alpha。

## 移动页面请求身份

被测页面使用以下实测 iPhone Safari 兼容身份：Windows 在首次导航前通过原生 `WKPageSetCustomUserAgent` 设置，macOS 在首次 load 前设置 `WKWebView.customUserAgent`：

```text
Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Mobile/15E148 Safari/604.1
```

`BrowserIdentity.MobileUserAgent` 统一管理该值，现有 options JSON 将其传到 macOS，不重复维护版本常量。Safari 外壳和沉浸式 App WebView 使用相同移动身份，因为此批次没有独立观察到的 App WebView UA。独立系统开发工具窗口保留自己的前端身份。这会改变 HTTP 与原生 `navigator.userAgent`，不通过 JavaScript 覆写属性，不伪造 viewport/DPR/platform，也不改真实桌面引擎标签。

该值来自既有 Device Hub Safari document-end 对照会话，采集页直接记录 `navigator.userAgent`。样本 JSON 的 SHA-256 为 `7973871380c93060737c1f73d174f86e29758ca7c1e4aa73ba494b7cd816f47d`，冻结采集页的 SHA-256 为 `13d0289bc1b971c36360c54612d8de39c6db94c0922068bb052c84f967deecf5`。仅 UA 文本、不带末尾换行的 UTF-8 字节哈希为 `f7b0b345ad5744babf09b23d04ecf7dde6dfb8129743cfdbddd254e4ac4c282d`。会话注明是 iOS 27.1 Device Hub 对照，但未独立记录浏览器启动入口，也未认证物理硬件；该 UA 不是 Duo 特有机型标识。应保留观察到的 `Version/27.0`，不能根据模拟器 runtime 推断新值。WebKit [将 iOS UA 版本冻结在 18.7](https://bugs.webkit.org/show_bug.cgi?id=298673)，因此 `18_7` 不代表观察环境实际运行 iOS 18.7。

## 实时表面合成

Compositor presentation API v1 对完整树执行最终四分之一圈旋转/平移，与实测场景布局分离。旋转场景将原始 WebKit HWND 作为 [cloak 的 layered 子窗口](https://learn.microsoft.com/en-us/windows/win32/directcomp/how-to--animate-the-bitmap-of-a-layered-child-window)，置于应用拥有、完整尺寸且无 redirection bitmap 的源宿主中；物理屏幕接收旋转后的实时树。将源保留在较小的物理裁剪窗口内，会在合成前丢失普通绘制像素。`WindowsScreenPresenter` 对指针坐标做逆变换，并把焦点委托给真实源 HWND；屏幕遮罩和外壳无障碍矩形跟随呈现变换。此应用 DLL 能力需要重建应用，不改变 WebKit 运行时哈希或 viewport API。[场景约定](005-SAFARI-SCENES.md#机身与内容坐标系)定义机身零度基准；[交接文档](009-WINDOWS-HANDOFF.md#原生验收清单)记录验收及分数 Fit/DPI 的剩余限制。

`windows/HostCompositor.cpp` 向 `WindowsComposition.cs` 提供合成 API v3，创建 BGRA D3D11 设备、Direct2D 设备及 WebKit 子窗口上方的 DirectComposition target。`IDCompositionDesktopDevice::CreateSurfaceFromHwnd` 导入该子窗口的 **DWM 实时重定向表面**。这条路线无需给 WebKit 另增 ANGLE 共享纹理导出。宿主在首次导航前启用 WebKit 现有离屏渲染模式，使软件和加速页面帧进入相同的保留式 backing store 与分层窗口。加速 WC 路径会将 GPU 像素回读至 WebKit 的共享位图传输，这是性能成本，不能称为零复制呈现。页面不由截图 API、图像拼接或 DOM 尺寸覆盖提供，WebKit 仍是唯一网页渲染及输入拥有者。浏览器分层窗口 alpha 固定为 255，仅用于暴露源表面；材质强度不会降低前景字形透明度。

绘制补丁将加速帧元数据规范为提交时的逻辑视图尺寸和 backing 比例，与非加速路径一致；位图仍保留物理像素尺寸。既有 backing-store 状态校验拒绝过期缩放回调，避免用新比例解释旧帧。物理 HWND 同步与事件驱动的完整重绘处理窗口尺寸和祖先裁剪变化，不触发导航。宿主观测更新每次布局只发布一份遥测，并复用文本、字号完全相同的测量，直至语言或原生资源变化。绘制和观测计数用于诊断链路，不代表最终桌面 FPS。

遮蔽 inset 变化时，外部视图尺寸可能不变，但绘制原点已经移动。Windows WC 在 inset 实际变化后请求一次完整绘制区域更新，并清除排队的滚动复制损伤；仅使 HWND 失效会重画旧 backing store。独立的 `just test-windows-presentation-insets` 检查未经修改颜色页新露出的条带；源栅格结果与最终 DWM 可视验收仍须分开。

Avalonia 会分派 Windows 消息，但不运行 WTF 自有的 `run/cycle` 循环。因此 Windows RunLoop 补丁在既有 message-only 窗口上，按最近的单调时钟截止时间设置一个原生定时器。到期任务仍经过 WTF 原有的 `FireTimerMessage` 队列及取消校验。相同截止时间不会被反复推迟，取消、重复回调与窗口销毁会重排或释放定时器；WTF 自有循环仍可使用同一队列。这是在驱动原生定时器，不是轮询、强制重画或嵌套循环抽取 Avalonia 消息。

合成顺序为实时页面/有效延伸、文档外边条、局部高斯模糊、圆角着色/边框/阴影、清晰文字/图标。各模糊层都读取外壳装饰前的同一页面/边界源，避免递归采样自身；先模糊再圆角裁剪，边缘采样夹紧。表面使用预乘 alpha，Direct2D/DirectWrite 单独绘制前景字形。命中框和光学框继续分开，完整选中标签容器、暴露文字裁剪、本地化名称、原生点击/快捷键转发保留场景坐标。没有横向正文的侧边条使用根背景，与主题着色独立。

桥接记录物理尺寸、资源代次、已提交场景代次和硬件/WARP 选择。源帧生命周期归 DWM 管理；这些计数器**不是** WebKit 合成器帧 ID，不能证明异步布局/DOM 观察原子到达。不变场景不会为了动画刷新反复重建。设备移除时重建呈现资源，不导航网页。构造失败会释放资源并恢复原生样式；硬件初始化失败后用相同 D3D11 路线尝试 WARP。合成不受支持时显示带原生详情的本地化能力错误，不回退至 GDI 材质。

普通 Windows 构建使用检测到的 Visual Studio C++ 工具/Windows SDK 编译 `DevDuo.Windows.Compositor.dll`。每次应用证据会话同时冻结其构建清单/原生源码哈希、C# 编译器校验和、资源、运行时清单及冻结构建输入。便携工具继续由 mise 管理；模拟器测量配置不变。

```powershell
mise exec -- just test-windows
# 先以 RunAcceptance.ps1 -Hold 在 8877 端口提供已审查页面：
mise exec -- just probe-windows-app
```

应用套件覆盖 24 个 Safari 状态、精确 Fit、fixed 底框、视口单位/范围、语言/文档/滚动保持、沉浸模式、外观/背景分离、销毁及运行时缺失。这些几何测试有意使用 fixed 标记，不能证明未经修改的绘制正确。`--presented-color` 加载不含该标记的原始应用 HTML 颜色夹具。`--presented` 接受 `DEVDUO_URL`；`--presented-left` 还会启动 Closed 左侧 inset 场景。呈现工具栏提供显式采集/结束、下一页面/场景、真实窗口 resize Fit、玻璃贡献控件；额外页面需用 `mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages` 提供。这些控件选择已有测量场景，不改变其配置。自动记录最多 300 份、间隔两秒，之后仍可显式采集。测试偏好通过 `DEVDUO_PREFERENCES_PATH` 隔离。

## 分数边界栅格化

当前运行时要求**正文绘制 v2**，同时保持 viewport API v2 和内容取色 API v1。`WKViewSetHostUnderPageBackgroundColor(view, rgb)` 使用与宿主边界条带相同、明确标注的非透明根背景回退值。Setter 更新原生文档外背景，仅在颜色改变时产生绘制失效；导航时先恢复白色，等待当前文档的有效观测。它不修改网页 CSS 或几何。运行时清单版本及必需导出区分此版本与 painting v1；旧运行时需要重建。

约 94% Fit 的细线包含两项栅格问题。宿主用失效区域的 floor/ceil 外包规则计算正文裁剪，但 Skia 硬裁剪采用最近像素边界。实测 594 像素页面在 backing scale 0.939914 下的边界为 558.31 像素：引擎在第 558 列之前停止绘制，旧宿主却从第 559 列才开始填充。边界和 footer 的四条裁剪边现统一采用半值向上取最近像素；失效矩形继续向外保守取整。独立诊断也采用相同裁剪规则。

其他分数情况又暴露了软件 backing 边缘的透明像素。`SetDIBitsToDevice` 忽略 alpha，使透明预乘像素变成不透明黑色。原生软件绘制现先叠到明确的文档外背景上，再铺白色，之后才上传 GDI，无需拉伸正文或复制边缘像素。`WM_PRINTCLIENT` 同时修正为与 `WM_PAINT` 一样，将物理客户区失效转换为逻辑失效，避免诊断绘制再次被分数缩放裁短。

`BoundaryChecks.cs` 使用未经修改的纯色静态夹具，覆盖八种映射和六种窗口尺寸。单次 `WM_PRINTCLIENT` 证据测量 DWM/外壳之前的软件绘制，既不代表最终 GPU 输出，也不作为渲染输入。呈现套件另外记录页面自有 fixed/sticky 框与视口单位，验证跨 DPI 保持。显式 **Capture observation** 也保存软件绘制证据，自动观测仍只记录元数据。真实 DPI 和实际窗口覆盖见[原生验收记录](009-WINDOWS-HANDOFF.md#原生验收清单)。

## 验证限制

### Safari 滚动指示器

从 dev `55e1c35` 取得的 [Device Hub 滚动指示器调研](003-CALIBRATION-WORKFLOW.md#safari-滚动指示器)定义行为目标。Windows Safari 呈现关闭主文档原生纵向滚动条的绘制和命中测试，保留覆盖式布局。嵌套与横向滚动条、沉浸呈现、页面样式和视口预留保持现有行为；不会注入 CSS 隐藏原生滚动条。

必需的 **host scroll-indicator API v1** 通过 WebPage 创建参数传递策略，异步提供文档 epoch、根滚动位置／范围、CSS 隐藏状态和原生用户位移序列。WebCore 内单一的弱引用状态存储区分文档生命周期。原生滚轮／键盘路径建立意图，程序滚动请求清除意图，且必须发生真实纵向位移；布局范围／视口变化会使意图失效。宿主还要求近期原生输入，拒绝过期导航／几何回调，并默认隐藏。零滚动范围、`scrollbar-width: none` 及原生自定义滚动条的隐藏／零宽样式抑制指示器，仅改变 DOM scrollY 不会显示。它是有时间边界的原生意图／位移关联，不能泛化成页面变更与输入并发时的精确因果证明。

被动的 DirectComposition visual 位于整个场景的外侧右缘，包括 Safari 侧边预留，并沿用现有呈现变换和屏幕遮罩，不增加宿主输入或无障碍矩形。局部滑块表面独立于玻璃树移动；淡出只改变透明度，不重绘网页。当前近似参数为黑色滑块、最大不透明度 0.55、宽度 3、外缘间距 3、轨道上下各预留 4、最短滑块长 24（均为场景单位），停留 700 ms、淡出 300 ms。它们是工程参数，不是物理设备测量或 Safari 精确时序。macOS 系统 WebKit 与其它滚动容器不属于这次 Windows 修正。

在解锁桌面上运行 `just test-windows-scroll-indicators`，执行折叠竖屏专项检查，分别验证真实可信输入、原生位移遥测、保持不变的 DOM 几何、源栅格中不存在第二条滚动条，以及最终桌面滑块像素。覆盖新页面静止、原生滚轮／键盘、末端淡出、瞬时／平滑脚本滚动、取消滚轮后的脚本滚动、短 `overflow:scroll` 文档、CSS `none` 及自定义 `display:none`／`width:0`。淡出后的稳定区间还检查原生更新、指示器查询和完整场景代次均停止增长。其它姿态、触摸惯性、嵌套／横向容器及 macOS 仍需单独原生验收；编译和纯状态测试不能证明这些结果。

### 半透明 fixed 遮罩

布局视口内的 alpha 合成与视口外的颜色延伸是两项能力。全屏 `position: fixed; inset: 0` 遮罩仍止于真实布局视口。按照 [WebKit 的 backdrop 实现](https://github.com/WebKit/WebKit/commit/2ae949b78743)，遮挡 inset 应使用与页面背景混合后的纯色，不再延伸原条纹内容。用户提供的 Safari 观测及其范围记录在[标定文档](003-CALIBRATION-WORKFLOW.md#半透明弹窗背景)中。

新增必需的**固定容器颜色 API v1**（`WKViewGetFixedContainerColorAPIVersion`、`WKViewRequestFixedContainerColors`）通过普通 IPC 异步查询 WebCore 现有的 `fixedContainerEdges`，返回四边状态和不透明 RGB，复用引擎对 dialog 背景及 dimming layer 的识别。它不改变布局，也不写入页面的固定颜色历史缓存。Windows 只在真实遮挡条带内填色，包括条带下仍有有效普通流的部分；成功返回空边或不支持状态时清除旧色。旧 DOM 底栏字段仅供诊断，不能覆盖原生结果。运行时清单要求 `hostFixedContainerColorApiVersion: 1`，旧引擎需重建。

Windows 边缘元数据路径还会在原生混色前，将背景自身 alpha 与该 renderer 的 computed `opacity` 相乘，修复纯黑背景加 `opacity: .5` 时视口正常半透明、边缘却返回纯黑的缺陷。opacity 为一时保留原颜色路径。该变更不改变页面真实绘制，也不推断祖先 group opacity 或任意滤镜。macOS 系统引擎未包含这一局部修正。

现有 200 ms 原生计时器在绘制更新变化时发起采样，每个视图至多一个请求在途。导航和几何变化使待处理结果失效；文档、导航、几何及请求代次共同拒绝过期回调。超时清色并允许重试。`webKit.continuation.fixedContainerColorExtension` 记录状态、代次、各边颜色与来源。采样可能滞后动态绘制；无法归为纯色的边缘明确保持不支持，不将任意视觉内容伪装成一种颜色。

`just test-windows-fixed-overlay` 使用独立的 `fixed-overlay.html` 夹具，分别检查视口 alpha 与 inset 纯色，同时保持文档标识、视口、滚动、fixed 矩形及视口单位。它覆盖普通遮罩、原生 dialog 背景、展开/收起预留和侧边 inset。源栅格、最终桌面像素和原始 Safari 对照属于不同证据，不得扩大 viewport 或添加文档 padding 来模拟延伸。macOS 桥接在系统能力可用时启用系统实现；该变更路径仍需 macOS 原生验收。

### 外壳材质与动态状态

合成 API v3 分开绘制类型与点击动作，并用绘制所用的 DirectWrite 字体测量文本。地址栏主体、菜单、书签和概览展开外壳；仅重新加载图标触发 reload。加号仍为返回顶部的示意。`SafariChromeState` 将原生滚轮/键盘意图与同向正文位移关联：向下累计 >24、向上 >8 切换，意图 350 ms 失效；视口夹紧、零/反向位移保持状态，单独脚本滚动不收起。导航重置展开及输入。该时限关联不能证明并发脚本/原生输入的精确因果。

`config/safari-shell.json.windowsMaterial` 管理 Windows 艺术参数：模糊 14 场景单位、饱和度 1.8、亮度 1.05、对比度 1.02、顶部高光 0.35、边缘高光 0.30、sheen 0.08、阴影模糊 10、向下偏移 3。既有 Tint/Glass/Shadow/Radius 偏好继续生效。饱和度、亮度和对比度使用 ColorMatrix，因为 DirectComposition 饱和度效果仅接受 0–1。圆角着色、定向高光、边缘和阴影近似 [design.dev](https://design.dev/tools/liquid-glass-generator/) 与 [Hire Digital](https://hiredigital.com/tools/liquid-glass-css-generator) 的分层配方，不代表物理折射或 Apple 私有 Liquid Glass 内核。顶部背景、浮动表面和标签轨道分别处理；玻璃内字形跟随独立外壳外观，材质厚度适应背景明暗；无玻璃托底的 Closed 时钟/Wi-Fi 按根背景保持对比度，文档边界保持独立。两个桌面平台的浮动和收窄玻璃现按 sRGB 加权亮度 0.24–0.48 的 smoothstep 连续混合明暗背景配方，移除原先在 0.36 处的跳变；相邻取样曾可使默认浅色浮动表面的着色 alpha 从 0.176 跳至 0.64。此区间是桌面光学调节，不是新的 Safari 测量；字形颜色、实测组件框和视口预留均不改变。

被动摄像头、Wi-Fi 及本地 `HH:mm` 示意沿用 macOS 场景坐标。Closed 竖向两种状态均显示三者，Closed 横向只在对应侧显示摄像头。Open 展开显示时钟/Wi-Fi，Open 紧凑及沉浸模式均隐藏。分钟变化会刷新时钟，静态页也可更新。名称本地化，示意元素不消耗网页点击。

独立的**内容取色 API v1**（`WKViewGetContentColorAPIVersion`、`WKViewRequestContentEdgeColor`）在内部使用普通快照 IPC，仅返回请求 ID、状态和 RGB。Closed 采样无遮挡底边，Open 采样顶边，条带不超过 18 逻辑行，忽略设备比例后缩放到宽 64。原生代码平均取消预乘后的 sRGB 通道，忽略 alpha <= 0.1。采样图像不进入呈现，也不跨宿主 ABI。状态 0 表示成功，1 为输入无效，2 为不可用/无有效像素，3 为不支持的格式。启动要求完整性校验清单中的 `contentColorApiVersion: 1`；旧运行时需按源码构建命令重建。

相关文档、滚动、视口或布局变化后防抖 180 ms，每个视图仅一个请求在途，并拒绝过期导航、文档、几何或请求代次。meta 提示只作诊断，真实正文决定背景，外壳外观决定字形/材质。宿主外观 API v1（`WKViewGetHostAppearanceAPIVersion`、`WKViewSetHostDarkAppearance`）持久保留视图外观，通过普通 PageClient 通知更新页面媒体查询，不使用测试 IPC；运行时清单要求 `hostAppearanceApiVersion: 1`。失败记录原因，并保留当前文档最后一次成功的原生渲染取色；只有该文档尚无成功原生取样时，才使用已标明的 DOM 背景近似。导航开始会重置这份颜色归属，包括加载重叠的情况；之后成功的原生取样会替换保留色。采样可能滞后动画，不改变文档外画布颜色。viewport API v2 和 painting v2 保留先前的布局语义，测试 IPC 仍关闭。诊断提供绘制类型、动作、场景框、状态显隐、取色来源、采样框与代次。`content-color.html` 新增无标记的纯色、渐变、SVG 图片和透明画布输入，RGB 各通道容差为 3。实际外观、原生取色、几何和输入分别验收。

冷导航区分 DOM 就绪与实际绘制就绪。既有 200 ms 元数据计时器仅在原生绘制计数变化时唤醒工具栏取色；加载完成也会唤醒，并对现有实时 backing store 做一次失效重绘，不改变尺寸或重新加载。固定边缘采样在首份有效观测和加载完成时独立唤醒，即使绘制计数不变；不可用结果最多尝试三次后进入空闲，后续就绪/绘制变化可重新开启，旧回调不能消耗新唤醒或其它文档的工作。`just test-windows-fixed-overlay` 现先执行六次未缓存的 1:1 折叠竖屏加载，分别展示 absolute 白色底栏/背景图和初始黑色 fixed 遮罩，以延迟的本地 SVG 延后 load 完成。用例在任何刷新前检查源栅格最后两行、DOM 几何、工具栏取色、固定边缘元数据及空闲行为。这些新增原生用例仍须在解锁的 Windows 桌面执行，编译不能证明报告中的 1 像素缝隙已经消失。

快照先按条带逻辑尺寸完整绘制，再在原生内部按面积重采样至宽 64 后取平均。直接向 WebKit 等比快照绘制器传入取整后的缩略尺寸，可能在宽而浅的条带中裁掉某一轴。原生取色夹具覆盖 Open 两个方向，确保保留完整请求边缘。这种仅用于元数据的回读不影响实时 DWM 呈现链。

当前材质/状态运行时在真实 96 DPI 下通过覆盖 24 状态的 **1009 项原生应用断言**，并单独通过运行时缺失检查及匹配运行时的 152 组页面比较。纯色、横向渐变、SVG 图片和透明画布均满足各 RGB 通道容差 3，Open 两个方向得到一致的整宽渐变平均值。无标记平滑滚动用例在三个窗口尺寸下补验文档、视口及范围保持。自动测试覆盖原生命中/动作路由和程序激活，实际鼠标事件送达另见下文。静态页遥测确认时钟跨分钟变化。合成器与源码运行时构建成功，本地化每语言 131 个键，`just verify` 通过。完整 150%/200%、本地桌面及跨 DPI 呈现验收仍待完成。

### 实际窗口滚动回归

实际窗口复验又发现分数滚动问题：平滑返回 scrollY=0 后，静态页面仍可能保留错位行及窄条旧像素。此固定版本实际启用的 Windows 绘制区为 `DrawingAreaWC`；此前其非合成更新把逻辑滚动位移交给 Skia backing store，缩放后位移被截断成整数像素。Windows 专项宿主补丁现在遇到无法整设备像素表示的位移时，清除已排队复制，走正常滚动区域失效绘制；可整像素表示的位移仍保留复制路径。修正只随原生滚动更新发生，没有重绘计时器、强制重绘循环、网页修改或视口改变。分数滚动可能比原复制优化绘制更多像素。

96 DPI 远程桌面实际窗口已覆盖原生 100% 和分数 94%/75%/50% 呈现、圆角玻璃、状态显隐、Open 单/多标签、紧凑展开、独立鼠标动作、HTTP 重载/历史及底部/顶部实时延伸。原始无标记颜色页平滑返回前后的正文区域，在保留的 JPEG 截图中，94%、75%、50% 各通道差分别为 0、最多 2、0；94% 的整窗截图字节一致。页面自有动画改变栏下模糊源时，外壳场景代次保持不变；暂停后静态整窗截图字节一致。这些限定观察不能证明压力滚动性能或与 macOS 光学效果完全一致。原始失败及一次落在未启用绘制区的无效修正均保留本地证据，维护中的补丁不包含那处未启用路径的改动。

### 先前绘制基线

新合成器已成功构建。最终绘制运行时通过 152 组几何比较，保留先前 39 组，并加入 94% Fit、HTML 字符串加载、静态渐变、透明根背景、静态 quirks、自有高度控件和独立动画；静态夹具不插入 fixed 标记。应用在真实 192 DPI 下通过覆盖全部 24 个 Safari 状态的 499 项断言，包含原生表面尺寸/代次；运行时缺失检查也通过。几何测试仍使用已注明限制的 fixed 标记。

实际窗口观察现已覆盖远程桌面的 96 和 192 DPI。原始颜色页延伸到地址栏下，旧黑块及控件矩形背景消失。独立方块/计数器在已提交场景代次不变时持续更新；全玻璃贡献时实时源明显模糊，外壳字形仍清晰。暂停页面自有动画后，静态输出仍正确。192 DPI 下检查了 Open 单/多标签和顶部延伸；左侧 inset 84、scrollX 40 的情况先后暴露两个保留式滚动复制问题，最终修正复制范围和 overlay 失效区域后复验通过。页面自有按钮缩短/恢复文档、滚轮到达文档末端时，根背景边界仍正确。最终末端记录为 inner 382 × 598、文档高度 2400、scrollY/范围 1802，无 fixed 元素。鼠标自动化未触发页面高度按钮，原生 Tab/Enter 可触发，因此这次观察不能证明鼠标激活通过。失败尝试和原始截图按各自哈希保留。

随后在 75%/50% Fit 下发现文档缩短会留下约一像素旧颜色边线，物理失效区域向外取整并保持完整画布绘制后消失。完全可见的测试窗口中，页面自有高度按钮原生鼠标激活通过；上文先前窗口被屏幕裁切时的自动化失败仍单独保留。最终运行时也通过左侧 inset 84、scrollX 40 的实际呈现回归。

先前 19 用例/预编译及 437 项断言记录仍属历史基线，不能证明新渲染器通过。真实 144 DPI（150%）、本地桌面及完整跨 DPI 保持验收仍不可用，因此 100%/150%/200% DPI 及本地/远程桌面修复门槛尚未完成。WARP 回退和设备移除恢复已实现，但尚未进行故障注入验证。几何、GPU 提交成功和实际窗口验收是不同结论；全部呈现门槛具备对应证据后才能标记完整修复通过。

预编译诊断仍使用**私有测试接口** `WKPageSetObscuredContentInsetsForTesting` 并显式开启 test-only IPC，只作为独立对照；Avalonia 后端绑定普通 C API 及维护中的宿主扩展。原生观察/绘制是异步的，不能证明帧级原子更新或压力滚动不存在瞬态瑕疵。其他 DPI/显示器组合、确证的进程切换、IME、无障碍、弹出窗口及广泛网站兼容性仍待验证。引擎 resolved canvas 底色、渐变/alpha 背景及 Safari 颜色历史的限制如前文所述。应用报告真实 WebKit inset 策略。

销毁浏览器 HWND 后再释放 WKView，移除消息处理器并释放原生引用。早期探针的销毁顺序错误已用匹配符号定位并修正，与文档末端绘制问题分开记录。

一手参考：[官方 Windows 构建产物](https://docs.webkit.org/Ports/WindowsPort.html#downloading-build-artifacts-from-buildbot)、[固定版本 WKView API](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebKit/UIProcess/API/C/win/WKView.h)、[原生绘制实现](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebKit/UIProcess/win/WebView.cpp)、[脚本消息 C API](https://github.com/WebKit/WebKit/blob/6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d/Source/WebKit/UIProcess/API/C/WKUserContentControllerRef.h)。

## Inspector、导航与首帧

`patches/0003-windows-developer-api.patch` 增加宿主开发 API v1：选择性移除专用调试用户脚本、显式启动/停止原生 Inspector TCP，以及每个存储的代理配置。运行时清单和启动现在要求 `hostDeveloperApiVersion: 1`，并保留 viewport/painting/content-color/appearance 版本要求。旧两补丁运行时必须重建以提供必需导出；补丁哈希保留为来源记录，打包策略见[发布指南](013-RELEASE.md#windows-webkit-运行时发布)；本次不改变逻辑屏幕或视口预留。

开发工具分组分别提供本地 Inspector、原生远程检查及页内 CDP/chii。原生远程使用无认证的 WebKit `webkit-inspector-socket` TCP 协议，与本地 Inspector 互斥；默认回环，仅显式选择时开放局域网，停止/销毁时释放监听。页面 CDP 使用独立的 token 保护原生消息桥和包内 chii 前端，不转换 WebKit Inspector 流量。Windows 代理支持系统/直连/自定义原生网络配置。详见[开发工具架构](001-ARCHITECTURE.md#开发工具与页面代理)及[验收范围](009-WINDOWS-HANDOFF.md#开发工具验收)。

开发工具分组中的本地 Inspector 按钮、**F12** 和 **Ctrl+Shift+I** 控制当前页面的原生 Web Inspector。Debug 和 Release 均启用开发者功能。宿主先连接 Inspector，再在显示前分离窗口，避免停靠占用设备视口。关闭 Inspector 窗口会更新宿主状态，释放视图时关闭前端。打开本地 Inspector 不会启动远程监听或启用测试 IPC。Elements、Console、Network、Storage 和 Timelines 来自固定 WebKit 前端；另一个 chii 选项为有明确范围的页面 CDP 子集提供 Chrome DevTools。

旋转会更新现有 WKView 的几何、遮罩、视口预留和实时合成/输入变换，不触发导航、不替换文档，也不清除 JavaScript、表单或历史状态。有效范围变化时滚动位置可能被限制。应用自有标定页通过 `history.replaceState` 原地更新查询标签；任意开发者页面不会被重写标签。切换设备形态或容器模式仍重建视图。

收到首个通过校验的文档范围观测前，只有尚未确认的 inset 延伸区使用兜底填色，真实布局视口保持实时显示。若等到 DOMContentLoaded 才取消整个画面的覆盖，会将 WebKit 已画出的早期内容挡在白色宿主表面后。延迟脚本回归会验证文档仍在加载时布局视口已露出。这项修复不会加速外部 CSS、脚本、DNS 或 TLS。

保存状态中的 `webKit.timings` 分开记录一次性的运行时校验、原生初始化、请求到导航开始、导航提交、完成及首次文档观测；这些是宿主里程碑，不是实际呈现帧时间。结合 Inspector 的 **Network**、**Timelines** 以及 Navigation/Resource Timing，可区分连接/响应延迟、解析、脚本执行和绘制。性能运行的原始细节只写入被忽略的 captures，不构成 WebKit 与 Chromium 的通用基准比较。

Windows IPC 传输此前在导入跨进程句柄前打开来源进程。本地 HTTP 与无外部资源的内联页面均复现了约 2.5 秒的延迟，原生计量将其定位到 `OpenProcess(PROCESS_DUP_HANDLE)` 内部，而非托管导航调用。对已注册进程能力的保护性复制还复现了 2.84 秒的 `DuplicateHandle` 停顿。这些 Windows 调用本身为何延迟尚未证实，单靠 Release 编译优化没有消除间隙。

源码补丁现为每个辅助进程继承父进程句柄能力。GPU 初始化前，UI 使用启动器已有句柄，将 GPU 进程能力推入 Web，并将 Web 进程能力推入 GPU。启动器明确只允许两个句柄被继承；子进程验证管道服务端身份，并清除继承标志。接收方先接管属于本进程的句柄，再检查连接代次或管道，因此拒绝也会释放句柄。回复分别记录 processed/accepted，以区分拒绝和取消投递。未确认的转移持有确切目标进程句柄，始终未收到接管确认时终止该目标；不会回收子进程可能已经复用的远程句柄数值。普通 IPC 载荷保留原有转移及关闭来源句柄的所有权规则。查询在锁内取得线程安全强引用，不复制进程句柄；最后一个连接持有者退出后撤销注册，进行中的查询继续保活能力。已注册操作失败不会静默重试 `OpenProcess`。子进程能力权限仅覆盖复制操作，但这属于 IPC 优化，不构成新的进程沙箱边界。能力转移保护没有宣称修复普通 IPC 管道发送失败的既有限制。

Network 到 Web 的资源句柄使用同一能力注册表。UI 先向 Web 推入 Network 能力，再回复同步建连请求。请求与尝试代次覆盖 Web 侧重试及数据存储内部的 Network 重启；更高代次只替换同一请求的待移交来源。有效连接信息到达后，先将来源移入连接，再打开 IPC。连接关闭及失败尝试会及时清理。只有这条 bootstrap 显式携带安全派发标志，因为内部异步重试已离开原同步处理函数，而 Web 仍在等待其回复；普通消息派发策略保持原样。

连接偏好与权限在 bootstrap 确认及对端身份检查后、发送创建请求前立即取样。若在异步 bootstrap 前取样，可能保留最初关闭的 Service Worker 偏好，而 provisional 页面初始化已在 Web 进程启用它。连接尚未存在时发送的 Network 偏好更新不会保留，旧快照因此让生成的 IPC 校验拒绝合法 Service Worker 消息并结束新页面进程。修复保留该校验，恢复配置更新顺序。

Windows 远程 Canvas 后端在进入隔离的同步 stream 前，先在 main dispatcher 完成 GPU 两阶段初始化。该 stream 有意不处理其他父连接消息，因此在父进程 bootstrap 确认前进入，会形成互相等待。GPU/后端强引用与 stream 身份检查保护重入关闭或替换；初始化失败时，在其所属 dispatcher 失效本次 stream，避免进入长时间读回等待。

GPU 握手的两个阶段都在同一个截止时间内检查真实的来源注册、初始化和连接有效状态。按消息名等待可能返回“已由共享同步队列派发”，也可能先消耗旧 token；仅凭这些结果都不能证明成功或失败。Windows 路径重新检查状态，不延长截止时间。其他平台保留原等待行为。

未知文档观测由单个验证任务读取渲染器已有的观测快照。读取期间到达的消息会标记需要再次读取，已接受文档的消息也一样；不会与第二个写入者竞争，也不会在旧文档验证期间被静默丢弃。导航代际和释放状态拒绝过期结果。这不会重跑页面观察器或 Canvas 取样。确定性的旧/新/旧消息序列在修改前失败，使用该排空流程后通过。

`just test-windows-navigation-latency` 在独立进程中分别冷启动 HTTP 和内联页面，再交替导航相同的无资源文档。检查覆盖真实完成、首次文档观测和页面动画推进，避免只将等待移到导航开始回调之前。HTTP 冷启动还会从原始请求起，计量首次匹配源光栅像素的时间，复用被动的 `GetDC`/`BitBlt` 读取，不请求绘制；该结果是观测上界，不是最终 DWM 呈现时间戳。同一配方还会在独立进程运行 `--navigation-latency-network-retry`，终止本测试的首个 Network 子进程，检查同一 Web 进程在有界期限内恢复文档；仅观察进程替换不能判定具体命中了哪层重试。`just test-windows-browser-identity` 检查真实请求头、页面 UA、重定向、后续导航、两种容器及旋转。这些有界本地主机回归不代表外部网站速度或最终桌面帧率。

原生测试程序的 `--browser-tools` 模式覆盖两种设备形态、八个旋转姿态、JavaScript/表单/历史身份、延迟脚本的早期绘制及 Inspector 按钮。`--browser-performance` 是主动选择的网络诊断，使用颜色实验室和 MDN 首页并开启 Inspector。两个独立进程依次运行 `--storage-write`、`--storage-read`，可检查 localStorage、IndexedDB 和持久 Cookie；两者需共用绝对路径的 `DEVDUO_WEBKIT_PROFILE` 和唯一的 `DEVDUO_STORAGE_TEST_TOKEN`。普通原生测试使用隔离的临时 profile，不读写用户网站数据。Windows 执行不能验证对应的 macOS 场景更新桥接。

`mise exec -- just package-local-windows` 在 `temp/build/local-release/win-x64/DevDuo` 生成自包含未签名体验包，并归档到 `temp/local-release/`。它复用已有源码构建的 runtime、冻结输入及许可，不需要已发布的运行时 Release。原始原生验收标记保持不变，不宣称已用最新构建编排重新构建引擎。正式发行打包仍遵循独立的已发布 runtime 要求。

侧边与水平 obscured 条带的交角由**侧边延伸优先**。顶/底边 fixed 颜色填充止于视口左右边界，因此侧边即使没有已接受的 fixed 元素颜色，也保留其普通背景。侧边 fixed 颜色仍覆盖侧条带全高。修改仅影响合成区域，不改变 DOM 视口、fixed 框或滚动范围。原生 fixed-overlay 矩阵采样右下/左下交角，并包含颜色不同的不透明 footer。

[English](../en/014-WINDOWS-WEBKIT.md) | [中文](014-WINDOWS-WEBKIT.md)
