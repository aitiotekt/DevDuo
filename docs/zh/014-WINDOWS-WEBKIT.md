# Windows WebKit 后端

被测 Windows 页面唯一使用固定版本的 WebKit Windows Port 及正式宿主扩展。Avalonia 管理工作台，WebKit 管理文档／输入，DirectComposition 呈现实时页面及 Safari 近似外壳。当前运行时验收由 [Windows 交接](009-WINDOWS-HANDOFF.md)维护，本文说明实现与复现。

## 宿主 API 源码构建

`native/webkit/windows/` 管理源码固定版本、四份补丁、便携构建环境、缓存和运行时打包；`tests/DevDuo.WebKitProbe/` 管理独立诊断及夹具。参见[运行时职责](../../native/webkit/windows/README.md)和[运行时发布策略](013-RELEASE.md#windows-webkit-运行时发布)。

当前上游固定为 `324423@main`，revision 为 `6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d`。诊断归档锁与发布 metadata 的上游 revision 必须一致。生产使用打补丁后的源码构建，不使用官方测试 DLL 替代。

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
mise exec -- just dev
```

mise 管理便携 CMake／Ninja／Ruby／Perl／gperf／pkgconf／vcpkg 和常规 Python 环境；VS C++／Clang 与 Windows SDK 保留系统依赖。自定义 vcpkg triplet 维持解释器／生成器归属。源码／依赖缓存位于 `temp/cache/windows-webkit/`，构建输出位于 `temp/build/windows-webkit-host/`，按工具链保留的构建树位于 `temp/build/windows-webkit-trees/`，增量构建之间不要清理。构建选择 `ENABLE_BACK_FORWARD_LIST_SWIFT=OFF`，不能混用官方启用 Swift 的归档文件。

| 补丁 | 职责 |
| --- | --- |
| `0001-windows-host-viewport-api.patch` | 正式 inset 及逻辑视图／backing scale 独立 API |
| `0002-windows-content-painting.patch` | inset 内绘制／损伤、画布边界、取色／外观／指示器及原生传输支持 |
| `0003-windows-developer-api.patch` | 用户脚本移除、原生 Inspector listener 和代理 API |
| `0004-windows-desktop-integration.patch` | 物理桌面映射、TSF／IMM、AX 传输及当前软件帧／字形处理 |

工作区宿主要求 viewport v2、painting v2、content color v1、appearance v1、developer v1、fixed-container color v1、scroll-indicator v1 和 desktop v1。应用自带的合成器另要求 composition v4、presentation v1。启动检查版本及必需导出；清单哈希是来源输出，不是运行时通过门槛。缺少 desktop v1 的运行时不兼容，应用交付须使用 metadata 选择的匹配引擎发行版。

## Avalonia 后端

checkout 自动发现 `temp/build/windows-webkit-host/bin`。安装版使用可执行文件旁的 `webkit/bin` 和 `webkit/runtime-manifest.json`。绝对路径 `DEVDUO_WEBKIT_RUNTIME` 覆盖具有权威性：缺失／不兼容能力明确报错，不选择其他引擎。测试专用 IPC 保持关闭。

`WindowsWebKitApi.cs` 绑定 C ABI，`WindowsWebKitView*.cs` 管理导航、存储、输入、对话框和无障碍，`WindowsWebViewPanel.cs` 配置场景几何。持久网站存储位于本地应用数据的 `DevDuo/WebKit`；绝对路径 `DEVDUO_WEBKIT_PROFILE` 可隔离 Windows 测试 profile。原生视图共享同一 store session，持久 cookie、localStorage、IndexedDB 和缓存跨重启保留，sessionStorage 沿用文档会话语义。重置宿主偏好不会清除网站数据，也不迁移／删除旧引擎 profile。

旋转、Fit、外框、语言、外观和外壳设置更新已有原生视图；形态／容器模式变化有意替换视图。导航／文档／几何代次及销毁保护拒绝过期异步结果。自有 HWND、回调、COM 资源和 handler 按生命周期释放，源窗口销毁在托管分离前使待处理工作失效。

## 绘制与文档边界

`WKViewSetObscuredContentInsets` 使用普通正式 IPC；`WKViewSetUnobscuredSizeRange` 提供视口单位所需的真实最小／最大／默认尺寸。`WKViewSetHostPresentationGeometry` 分离逻辑场景大小与物理 backing scale，Fit 保持布局视口且 page zoom 固定为 1，不修改 CSS 或 JavaScript 几何 getter。

绘制框包含真实上／左／右／下 inset。引擎绘制裁剪、原点、滚动复制范围和损伤覆盖完整绘制域，fixed／sticky 位置及滚动范围仍使用无遮挡视口。即使逻辑大小未变，inset／几何变化也触发事件驱动的完整损伤更新。分数设备像素滚动改为正常损伤绘制，不截断 backing-store 复制位移；整数位移保留复制优化。

Windows 非合成帧绘制到引擎自有共享 CPU bitmap，再经 WC 提交、保留式原生绘制和 DirectComposition。非加速快照也使用本地共享 bitmap；加速 WC 帧保留 GPU 传输／fence，并读回既有共享 bitmap 链路，不是零拷贝渲染。提交几何代次拒绝旧帧，Windows 缓存字形显示列表在本地／远程上下文之间使用后端无关操作。

宿主保留二维有效正文，仅在文档范围外填充明确的不透明 html／body 背景回退，否则白色。顶部为零时，底部有效延伸为 `clamp(documentHeight - scrollY - viewportHeight, 0, bottomInset)`。工具栏主题着色不能替代画布规则，固定容器颜色是下文独立策略。不使用截图、重复行、DOM padding 或伪造视口制造延伸。

## 移动请求身份

`BrowserIdentity.MobileUserAgent` 在两平台首个原生请求前提供已观察的 Safari 兼容身份：

```text
Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Mobile/15E148 Safari/604.1
```

两种容器使用同一观测，没有独立测量的沉浸身份。它改变请求头和原生 `navigator.userAgent`，不修改 platform／DPR／视口属性或真实桌面引擎。冻结的 `OS 18_7` 不证明所观察运行时版本，该字符串也不能认证 Duo 实机。保留原观测及私有 collector 来源，不按模拟器 OS 推断替代值。

## 实时表面合成

`HostCompositor.cpp` 以 D3D11、Direct2D／DirectWrite 和 DirectComposition 实现 composition v4。`CreateSurfaceFromHwnd` 导入 WebKit 子窗口的实时 DWM 表面；首个导航前启用 WebKit offscreen 模式，使软件和加速帧均进入保留式 painter 与 layered HWND。被测页面无 WebView2 回退，仅独立开发工具前端使用系统 WebView2。

旋转场景将完整大小的隐藏 layered 源宿主保留在较小物理裁剪之外；presentation v1 旋转／平移完整树，`WindowsScreenPresenter` 反向映射输入和焦点。机身、机身内正文及场景到画布坐标遵守[场景约束](005-SAFARI-SCENES.md#机身与内容坐标系)。

合成顺序为页面／有效延伸、文档边界填充、局部模糊、圆角着色／边缘／阴影，最后清晰文字／图标。模糊读取未绘外壳的页面，钳制采样边缘，再圆角裁剪；预乘 alpha 和独立字形层保持前景清晰。命中框、光学框及文本裁剪保持独立，选中标签使用完整可视容器。

被动触摸圆点由场景顶层的独立 DirectComposition 设备管理。移动时只提交该设备，不为每条鼠标消息重新提交页面／材质树。圆点可见时，共用一个不推迟截止时间的 16ms 定时器，触发时读取最新系统位置；进入、离开、失焦和模式切换立即处理，销毁取消待处理工作。输入事件独立传递。这限制了圆点更新积压，不重绘网页，也不改变圆点大小、遮罩及其在设备截图中的可见性。参见[跨设备 visual 约束](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositionvisual-addvisual)。

物理 HWND 同步与资源代次处理 resize、祖先裁剪及设备重建，不重新导航。硬件初始化失败可沿同一 D3D11 路线尝试 WARP；不支持合成时明确报告能力错误，不把旧 GDI 材质回退算作修复。设备丢失／WARP 恢复仍缺故障注入验收。场景／资源计数不等于 WebKit 帧 ID、原子 DOM／帧时间戳或最终 DWM FPS。

## 分数边界栅格化

正文裁剪采用 half-up 最近像素边界，与 Skia hard clip 一致；损伤矩形保守包围变化像素。混用两套取整规则会在分数 Fit 留下一像素缝隙。Backing 尺寸包围绘制域，原点对齐物理像素。

GDI 上传前，透明预乘软件像素先合成到明确的文档外背景，再铺白色，因为 `SetDIBitsToDevice` 不处理 alpha。`WM_PAINT` 与诊断 `WM_PRINTCLIENT` 一致地把物理损伤转换为逻辑坐标，不通过拉伸正文或复制边缘像素补缝。

无插入标记的静态边界夹具检查四边，其源绘制证据与最终 DWM 输出分别记录；[增量验收](009-WINDOWS-HANDOFF.md#windows-里程碑与增量回归)仅选择受影响的 Fit／DPI 用例。

## 外壳材质与动态状态

`config/safari-shell.json` 管理 Windows 可调光学参数及实测场景参考。材质角色分离顶部背景、浮动表面、紧凑地址栏和标签轨道；饱和度／亮度／对比度采用 ColorMatrix，字形按外壳外观独立于背景取色处理，明暗背景配方平滑插值。这是桌面艺术近似，不是物理折射或 Apple Liquid Glass 内核。

当前原生渲染边缘决定背景色，`theme-color` 只作诊断。Content color API v1 使用普通快照 IPC，仅返回请求／状态／RGB：Closed 取无遮挡视口底边，Open 取顶边；最多 18 个逻辑像素行按原尺寸绘制，再缩到宽 64，按非预乘 sRGB 求平均并忽略低 alpha 像素。快照图像不进入页面呈现。取色防抖 180ms，每视图最多一个请求在途并拒绝旧代次；失败时保留当前文档上次原生色，首次成功前使用标记清楚的 DOM 回退，导航重置归属。

Appearance API v1 通知 WebKit 和页面媒体查询，不导航。原生输入意图与同向正文移动相关联：累计向下超过 24 CSS px 收起，向上超过 8 展开，意图 350ms 过期；零／反向运动、视口 clamp 和纯脚本滚动不切状态，并发脚本／原生移动仍有限定。

地址主体／菜单／书签／概览展开外壳，只有重新加载按钮 reload，加号保留返回顶部示意。DirectWrite 使用同一字体测量和绘制。被动摄像头／时钟／Wi-Fi 按场景显隐：Closed 竖向全显示，横向仅摄像头，Open 展开显示时钟／Wi-Fi，Open 紧凑与沉浸均隐藏。本地 `HH:mm` 在静态页也更新；本地化占位不拦截网页输入。

## Safari 滚动指示器

Windows Safari 抑制根垂直滚动条原生绘制／命中，保留布局与滚动。被动屏幕外缘 DirectComposition thumb 随场景遮罩／变换，独立于玻璃树淡出。嵌套／水平滚动条和沉浸模式沿用原生行为，不注入隐藏滚动条的页面 CSS。

Scroll-indicator API v1 提供文档 epoch、根范围／位置、CSS 抑制及原生移动。必须同时有近期滚轮／键盘意图和实际移动；脚本滚动、零范围、CSS `none` 或隐藏／零宽自定义条不显示 thumb。导航和布局变化清除意图。时序和厚度是工程近似，不是 Safari 测量；其他 scroller 及触摸减速需独立验收，macOS 没有对应宿主策略。

## 半透明固定遮罩

视口内 alpha 与视口外颜色延伸分别处理，fixed mask 仍止于真实布局视口。Fixed-container color API v1 通过正式 IPC 读取 WebCore 四边识别／混色及 dialog backdrop，返回边状态和不透明 RGB。宿主仅填实际遮挡条带，空／不支持结果清除旧颜色，旧 DOM footer 字段不能覆盖原生结果。

Windows 将 renderer 自身背景 opacity 纳入边缘混色，不推广到祖先组 opacity、滤镜、渐变或任意 stacking context。侧边与水平条带相交时，侧边延伸拥有角部。采样有超时边界、单请求及代次保护，在首观测／load／后续绘制变化后给有限重试，静态页无需绘制循环。macOS 系统支持和有限回退见[平台对齐](015-PLATFORM-PARITY.md)。

## 桌面输入、对话框与无障碍

Desktop API v1 通过借用的 presenter HWND、四分之一转及偏移映射物理弹窗／caret 坐标。Select 锚点、显示器边界、窗口归属及激活沿可见根所有者处理，原 WebKit HWND 仍是真实输入源。

TSF `ITextStoreACP` 与 IMM 路径驱动真实 WebCore composition。Store 只保存待组合事务，不替代文档；焦点消息 hook 在 Avalonia 翻译前只向文本服务交付一次。UTF-16 预编辑、选区替换、提交／取消及候选窗定位使用真实 editor state。Frame／文档／元素归属拒绝旧操作，焦点变化结束旧组合而不重选旧范围；WebCore 绘制可见 caret，不可见 OS caret 支持 IME 查询。失焦／导航／销毁释放 hook、上下文和 COM，完整周边文本／再转换尚不支持。

普通 `WKPageUIClientV6` listener 提供本地化 owned alert／confirm／prompt，包含请求 frame 来源、串行完成及导航／销毁取消。

`WKViewCopyHostAccessibilityTree`／`WKViewPerformHostAccessibilityAction` 经有界正式 IPC 暴露文档限定的 WebCore AX 语义，密码字段不导出值。唯一 UIA fragment 归属可见 presenter，源／中间 HWND 薄桥和 Windows 专用 Avalonia peer 避免重复树；MSAA、Value／Invoke／Focus、名称及物理命中框使用同一模型。查询等待文档提交，同步 IPC 期间收到的新失效在回复后仍保留；更新由原生通知驱动，不周期轮询整棵树。UIA TextPattern／IAccessible2 尚不支持。测试选项及验收由 [Windows 交接](009-WINDOWS-HANDOFF.md)管理。

## Inspector、导航与首帧

本地 Inspector 独立显示，不占实测视口；原生远程检查是与本地 Inspector 互斥的独立、无鉴权 WebKit TCP 协议，页面 CDP／chii 使用独立 token 保护桥。停止／销毁释放 listener 和前端，原生代理支持 system／direct／custom。参见[开发工具架构](001-ARCHITECTURE.md#开发工具与页面代理)。

文档范围尚未验证时，仅未确认的 inset 延伸填回退背景，加载中的布局视口保持可见。单一观测验证 worker 消化新消息，不让新文档更新丢在旧回复之后。原生导航、commit、finish、观测和源像素耗时分别记录。

外部 Avalonia 消息泵通过一个最近期限 timer 和已有触发／取消队列处理 WTF deadline，不轮询或嵌套 drain。辅助进程句柄传输使用继承／注册进程 capability、明确继承白名单、对端校验及 processed／accepted 确认；正确接纳或释放目标进程拥有的句柄并拒绝旧连接尝试。Registry lookup 保留强引用，不复制进程句柄；注册操作失败不静默重试 `OpenProcess`。这是 IPC 优化，不是新的沙箱边界。

Network bootstrap 保留请求／尝试顺序，在确认后采样偏好。Remote Canvas 在所属 dispatcher 完成 GPU bootstrap 后才进入隔离同步 stream；两阶段握手共用一个 deadline，可重入关闭使捕获的 stream 失效。不能为掩盖启动等待而削弱原生校验、stream 标识或资源归属。

## 验证与诊断

按 [Windows 验收](009-WINDOWS-HANDOFF.md)选择受影响 recipe。普通页面探针对比真实基准和 inset 视口，涵盖 standards／quirks、透明／渐变、高度变化和分数 Fit 动画；几何相等不能认证绘制。原生源像素、实际窗口及输入／AX 结果分别记录。

实际呈现 harness 支持原始颜色 HTML、指定 `DEVDUO_URL`、左 inset 场景、观测及真实窗口大小／Fit 变化。受审夹具可用以下命令提供：

```powershell
mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages
```

常规测试隔离网站／宿主偏好。跨进程存储检查要求相同绝对路径测试 profile 和唯一 origin／token，不能清理用户真实网站 store。`just package-local-windows` 使用已有源码运行时生成未签名本地体验包，正式发布使用已发布且 metadata 指定的依赖。

独立官方预编译探针可为对比启用私有测试 inset API。短期归档可能过期，准备步骤明确报告缺失；探针选项和产物是诊断，不是生产 API 兼容或原生验收证明。

## 关键裁决与经验

- **引擎与布局：** 保留 WebKit 所有权和真实 obscured inset。Page zoom、修改 DOM 尺寸、插入 fixed 标记或拼接截图不能证明延伸正确。
- **透明：** WinForms 透明背景重画父控件，不能透出 WebKit 兄弟窗口；应使用实时合成及独立材质／前景层。
- **绘制：** DOM／AX 有内容或提交成功时，像素仍可能全白；静态原生绘制与最终窗口分别验。旧远程白帧停滞未独立定位，不能编造单一根因。
- **后端转换：** 缓存的本地 Skia `DrawTextBlob` 被远程 recorder 重放时发生崩溃。图层提升之间使用后端无关字形操作，并保留旧帧代次保护。
- **就绪与可重入：** 过早同步 AX 发现阻塞启动，回复后清失效会丢新事件；须等待已提交文档并保留泵消息期间的新通知。
- **坐标与参照：** 保留完整标签容器、独立机身／正文变换及 Closed 180° 历史。原生弹窗／caret 必须沿可见 presenter 映射，不能只用离屏源的 `ClientToScreen`。

原始诊断截图、失败尝试及对应构建输入保持私有且不改写。当前限制和指标集中在里程碑，本文不累积逐轮总数。

[English](../en/014-WINDOWS-WEBKIT.md) | [中文](014-WINDOWS-WEBKIT.md)
