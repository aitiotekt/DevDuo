# Windows WebKit 后端

被测 Windows 页面唯一使用固定版本的 WebKit Windows Port 及正式宿主扩展。Avalonia 管理工作台，WebKit 管理文档／输入，DirectComposition 呈现实时页面及 Safari 近似外壳。当前运行时验收由 [Windows 交接](009-WINDOWS-HANDOFF.md)维护，本文说明实现与复现。

## 宿主 API 源码构建

`native/webkit/windows/` 管理源码固定版本、正式／实验补丁、便携构建环境、缓存和运行时打包；`tests/DevDuo.WebKitProbe/` 管理独立诊断及夹具。参见[运行时职责](../../native/webkit/windows/README.md)和[运行时发布策略](013-RELEASE.md#windows-webkit-运行时发布)。

当前上游固定为 `324423@main`，revision 为 `6a67801395e5dfc1a7ac4ceb60ddd2ca9735aa2d`。诊断归档锁与发布 metadata 的上游 revision 必须一致。生产使用打补丁后的源码构建，不使用官方测试 DLL 替代。

```powershell
mise --env windows-webkit install
mise --env windows-webkit exec -- just build-windows-webkit-runtime
mise exec -- just dev
```

mise 管理便携 CMake／Ninja／Ruby／Perl／gperf／pkgconf／vcpkg 和常规 Python 环境；VS C++／Clang 与 Windows SDK 保留系统依赖。自定义 vcpkg triplet 维持解释器／生成器归属。源码／依赖缓存位于 `temp/cache/windows-webkit/`，构建输出位于 `temp/build/windows-webkit-host/`，按工具链保留的构建树位于 `temp/build/windows-webkit-trees/`，增量构建之间不要清理。构建选择 `ENABLE_BACK_FORWARD_LIST_SWIFT=OFF`，不能混用官方启用 Swift 的归档文件。

补丁同步保留字节完全相同文件的时间戳，包括补丁新增的头文件，避免替换一个补丁就使无关对象失效。内容变化仍正常触发重编译；未记录的源码修改会明确报错并保留。

| 补丁 | 职责 |
| --- | --- |
| `0001-windows-host-viewport-api.patch` | 正式 inset 及逻辑视图／backing scale 独立 API |
| `0002-windows-content-painting.patch` | inset 内绘制／损伤、画布边界、取色／外观／指示器及原生传输支持 |
| `0003-windows-developer-api.patch` | 用户脚本移除、原生 Inspector listener 和代理 API |
| `0004-windows-desktop-integration.patch` | 物理桌面映射、TSF／IMM、AX 传输及当前软件帧／字形处理 |
| `0005-windows-edge-color-v2.patch` | 当前容器覆盖及上游主色采样 |
| `0006-windows-frame-pipeline.patch` | 输入／帧关联时序和异步 AX 快照 |
| `0007-windows-atomic-continuation.patch` | 同帧延伸、来源几何及关联时钟 |
| `0008-windows-frame-surfaces.patch` | GPU 纹理／CPU 缓冲租借、呈现背压及帧确认 |
| `0009-windows-frame-clock.patch` | 跨进程 QPC 时间戳及滚轮合并时的原始输入时刻 |
| `0010-windows-scrolling-layers.patch` | 实验 WC 滚动图层桥，复用上游线程命中、滚动链及 fixed／sticky 约束 |
| `0011-windows-direct-frame-relay.patch` | 实验 GPU 到 UI 的帧传输及确认，绕过网页主线程回传 |
| `0012-windows-async-presentation.patch` | 实验性保留图层滚动帧、修订次序、独立时钟和原生滚动诊断 |
| `0013-windows-native-touch.patch` | 实验性可信单触点输入、原生手势许可及上游精确／惯性滚动，已有范围限定的原生验收 |
| `0014-windows-wc-raster-coverage.patch` | 统一 Windows WC 瓦片绘制、损伤区域与纹理映射的像素边界 |
| `0015-windows-async-viewport-publication.patch` | 将独立滚动提交排在匹配视口几何的场景之后 |
| `0016-windows-direct-pan-work.patch` | 直接使用 Windows 滚动节拍，每次原生拖动只清理一次键盘滚动 |
| `0017-windows-precise-touch-coordinates.patch` | 在原生触点和按住拖动的滚动处理中保留小数 CSS 坐标 |

工作区宿主要求 viewport v2、painting v2、content color v1、appearance v1、developer v1、fixed-container color v2、scroll-indicator v1、desktop v1、异步 accessibility v2、frame timing v2、frame presentation v1 和 frame surface v1。应用自带的合成器另要求 composition v6、page frame v2、presentation v1。启动检查版本及必需导出；清单哈希是来源输出，不是运行时通过门槛。缺失能力须重建匹配运行时，应用交付须使用 metadata 选择的匹配引擎发行版。

## Avalonia 后端

成功的源码构建会原子写入忽略目录中的 `temp/build/windows-webkit-runtime.json`，选择本次输出。checkout 在下次进程启动时读取此选择；没有选择记录时发现 `temp/build/windows-webkit-host/bin`。无效／过期选择明确报错，不静默加载旧运行时。安装版使用可执行文件旁的 `webkit/bin` 和 `webkit/runtime-manifest.json`。绝对路径 `DEVDUO_WEBKIT_RUNTIME` 覆盖具有权威性：缺失／不兼容能力明确报错，不选择其他引擎。测试专用 IPC 保持关闭。

`WindowsWebKitApi.cs` 绑定 C ABI，`WindowsWebKitView*.cs` 管理导航、存储、输入、对话框和无障碍，`WindowsWebViewPanel.cs` 配置场景几何。持久网站存储位于本地应用数据的 `DevDuo/WebKit`；绝对路径 `DEVDUO_WEBKIT_PROFILE` 可隔离 Windows 测试 profile。原生视图共享同一 store session，持久 cookie、localStorage、IndexedDB 和缓存跨重启保留，sessionStorage 沿用文档会话语义。重置宿主偏好不会清除网站数据，也不迁移／删除旧引擎 profile。

旋转、Fit、外框、语言、外观和外壳设置更新已有原生视图；形态／容器模式变化有意替换视图。导航／文档／几何代次及销毁保护拒绝过期异步结果。自有 HWND、回调、COM 资源和 handler 按生命周期释放，源窗口销毁在托管分离前使待处理工作失效。

## 绘制与文档边界

`WKViewSetObscuredContentInsets` 使用普通正式 IPC；`WKViewSetUnobscuredSizeRange` 提供视口单位所需的真实最小／最大／默认尺寸。`WKViewSetHostPresentationGeometry` 分离逻辑场景大小与物理 backing scale，Fit 保持布局视口且 page zoom 固定为 1，不修改 CSS 或 JavaScript 几何 getter。

绘制框包含真实上／左／右／下 inset。引擎绘制裁剪、原点、滚动复制范围和损伤覆盖完整绘制域，fixed／sticky 位置及滚动范围仍使用无遮挡视口。即使逻辑大小未变，inset／几何变化也触发事件驱动的完整损伤更新。分数设备像素滚动改为正常损伤绘制，不截断 backing-store 复制位移；整数位移保留复制优化。

加速 WC 帧绘入由自有 keyed D3D11 纹理支持的 ANGLE pbuffer。Frame surface API v1 将资源租借给宿主，宿主在 GPU 内复制到保留式原始／最终页面缓冲。非合成帧将引擎原生 CPU 位图上传至同一缓冲。应用的两条呈现路径均不使用 GDI 或页面读回；显式诊断捕获仍会读回实际呈现的帧缓冲。这是 GPU 间传输，并非零拷贝：生产者目前先完成 GL 工作再交还互斥锁，Direct2D 还会把最终页面复制到合成表面。非对称页面像素检查共享 pbuffer 的 Y 轴约定。

宿主确认传输前仅允许一帧在途。过期几何仍归还生产者锁，但不呈现；资源重放区分已消费的锁与新帧。原始正文和延伸分开保留，填色到期不会累积旧填色。失败明确报告能力／呈现错误。多适配器共享及设备丢失恢复仍未验收。

Windows 运行时默认启用异步滚动和原生触摸。补丁 `0010`–`0013` 将上游滚动树与可信触摸事件接至保留式 GPU 图层，只租借一帧并合并待处理位置。在创建 WebPage 前选择帧模式，滚动线程时钟由已处理输入唤醒，在近期活动和原生动画结束后停止。范围限定的原生验收覆盖页面 JavaScript 阻塞、触摸取消、touch-action、overflow 锁定及嵌套滚动。复杂页面流畅性仍需按场景验收，不再通过编译开关关闭已验证路径。`build-windows-webkit-runtime -RuntimeRoot <绝对构建目录> -KeepRuntimeSelection` 可隔离构建且不改变 checkout 运行时选择。提交几何代次拒绝旧帧，缓存字形显示列表在本地 Skia 与远端 GPU 记录上下文之间使用后端无关操作。

实验性 host scroll-frame API v1 返回源绘制标识、文档／几何代次、滚动修订、真实根滚动位置及输入／提交／GPU／宿主时序，独立合成的滚动帧不填写主线程渲染阶段。只读原生滚动树请求使用正式 IPC，不返回页面图像；现有 frame timing API v2 的 ABI 不变。两种接口均不能证明 DWM 扫描输出。

实验性 host touch API v1 提供模式设置及触点代次诊断，托管宿主按可选能力协商启用。原生路径的文档启动脚本仅报告模式／能力，不重放事件或写入滚动位置。引擎先通过事件区域与常规 WebCore 触摸处理分派触点，再决定是否允许原生平移；非 passive 取消及 `touch-action` 仍由网页控制，平移不会伪造 DOM wheel 事件。取消 pointerdown 抑制兼容鼠标 down／up，不取消原生平移及可信触摸 click。失焦、丢失捕获、导航和模式切换会取消触点，创建参数保证进程替换后保留模式。初始按下时间用于平移速度基准，窗口变化引起的同坐标鼠标消息不产生额外触摸移动。当前仅支持单触点，不支持双指缩放或多触点；限定范围的原生结果及剩余验收边界见 [Windows 交接](009-WINDOWS-HANDOFF.md)。

监听器与 touch-action 变化会调度正常的事件区域渲染步骤，包括没有像素损伤的变化；WC 将新区域提交至滚动图层镜像。仅在 WebProcess 中开启区域设置还不够，后续偏好更新可能覆盖它。缺失已提交区域时，必须等待主线程真实的触摸回复。没有异步根节点的文档保留原生同步默认／overflow 滚动，不将内部 wheel 载荷分派给网页监听器；这些路径继续遵守取消行为、overflow 锁定与嵌套滚动链。

Windows 触控仿真在 `touch-action: auto` 和 `manipulation` 下会转发两个允许轴的位移。原生平移和 DOM 回退都缺少按手势方向锁轴的逻辑。固定版本的引擎也默认关闭实验性 CSS `scroll-axis-lock` 偏好。该属性控制浏览器能否对特定滚动容器锁轴，单独启用解析不会改变手势行为。

WC 镜像命中边界使用从零开始的局部尺寸，与 `CoordinatedPlatformLayer::bounds()` 一致；遍历另行应用 `boundsOrigin`，重复计入原点会使已滚动的嵌套区域输入误投。零位移 pan 起始可能没有锁定节点，因此取消时按需解析前一手势的原始命中链，停止包含嵌套目标在内的原生动画。新接触、模式／焦点变化或导航会结束旧手势。只读滚动诊断分别暴露镜像节点标识／局部几何与主线程图层树。

Windows 宿主从按下到抬起保持按钮归属。网页拖动在 Safari 控件上松手时，抬起消息仍交给 WebKit。外壳按下并在同一控件上松手时才激活该控件。下一次鼠标移动报告左键已松开时，原生触摸会取消遗留触点。

源栅格范围由逻辑几何和引擎统一的 float backing scale 决定，分数缩放下可能与逐轴取整的 HWND 尺寸不同。宿主按该几何校验源尺寸，释放过期帧租约，并将有效像素按原始尺寸复制至独立大小的输出缓冲：超出部分裁剪，物理取整余量使用该帧背景。不会拉伸网页补偿，DOM 视口、fixed 几何及滚动范围保持不变。

引擎保留有效二维文档流，仅在文档范围之外使用不透明 html／body 背景回退，否则白色。正文边界、提交的 inset、根背景、fixed 边缘状态与页面像素一起传输；UI 进程将延伸绘入同一个原生页面缓冲，宿主控件／材质更新不再覆盖独立采样的边缘填色。工具栏着色与文档边界规则独立，不以截图、重复像素行、DOM padding 或伪造视口提供延伸。

## 移动请求身份

`BrowserIdentity.MobileUserAgent` 在两平台首个原生请求前提供已观察的 Safari 兼容身份：

```text
Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Mobile/15E148 Safari/604.1
```

两种容器使用同一观测，没有独立测量的沉浸身份。它改变请求头和原生 `navigator.userAgent`，不修改 platform／DPR／视口属性或真实桌面引擎。冻结的 `OS 18_7` 不证明所观察运行时版本，该字符串也不能认证 Duo 实机。保留原观测及私有 collector 来源，不按模拟器 OS 推断替代值。

## 实时表面合成

`HostCompositor.cpp` 以 D3D11、Direct2D／DirectWrite 和 DirectComposition 实现 composition v6。Page frame v2 将租借的 WebKit 帧及其延伸绘入一个保留式 GPU 帧缓冲，再统一提交合成。首个导航前启用 WebKit offscreen 模式；HWND 保留原生输入／弹窗职责，不再提供页面像素。被测页面无 WebView2 回退，仅独立开发工具前端使用系统 WebView2。

旋转场景将完整大小的隐藏 layered 源宿主保留在较小物理裁剪之外；presentation v1 旋转／平移完整树，`WindowsScreenPresenter` 反向映射输入和焦点。机身、机身内正文及场景到画布坐标遵守[场景约束](005-SAFARI-SCENES.md#机身与内容坐标系)。

合成顺序为页面／有效延伸、文档边界填充、局部模糊、圆角着色／边缘／阴影，最后清晰文字／图标。模糊读取未绘外壳的页面，钳制采样边缘，再圆角裁剪；预乘 alpha 和独立字形层保持前景清晰。命中框、光学框及文本裁剪保持独立，选中标签使用完整可视容器。

被动触摸圆点由场景顶层的独立 DirectComposition 设备管理。移动时只提交该设备。移动请求共用 16 ms 兜底定时器，触发时读取最新系统位置。距上一次圆点提交满 16 ms 时，已接受的滚动帧可提前提交待处理的圆点。进入、离开、失焦和模式切换立即处理，销毁取消待处理工作。圆点和页面提交使用同一 QPC 时钟。原生触控在按住拖动期间保留小数 CSS 坐标，鼠标命中和兼容点击继续使用原有整数坐标。参见[跨设备 visual 约束](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositionvisual-addvisual)。

物理 HWND 同步与资源代次处理 resize、祖先裁剪及设备重建，不重新导航。硬件初始化失败可沿同一 D3D11 路线尝试 WARP；不支持合成时明确报告能力错误，不把旧 GDI 材质回退算作修复。设备丢失／WARP 恢复仍缺故障注入验收。场景／资源计数不等于 WebKit 帧 ID、原子 DOM／帧时间戳或最终 DWM FPS。

## 分数边界栅格化

正文裁剪采用 half-up 最近像素边界，与 Skia hard clip 一致；损伤矩形保守包围变化像素。混用两套取整规则会在分数 Fit 留下一像素缝隙。Backing 尺寸包围绘制域，原点对齐物理像素。

WC 瓦片绘制、TextureMapper 与 CSS 背景像素吸附共用设备缩放和取整边界。相邻瓦片共用边界，各自将损伤区域裁剪到该边界内。根视口 fixed 图层按视口坐标吸附后备纹理，抵消文档滚动后，纹理原点与延伸落在同一物理像素网格。DOM 几何仍使用 CSS 坐标。

更改 inset 时，滚动树的 fixed 位置可能先于正在等待绘制的主场景更新。Web 进程在更改 inset 前暂缓发布独立帧。切换首帧从同一份新布局取得图层位置和延伸边界，并清除旧视口积存的位置。主场景读取图层位置时持有滚动树锁，确保一批位置不会被拆开。GPU 队列丢弃已被完整场景覆盖的待处理增量。匹配场景入队后恢复独立发布，期间保留上一帧完整画面。滚动输入与布局视口继续由引擎管理。

最终 GPU 页面缓冲把预乘正文像素合成到提交的文档外背景，再按取整后的物理边界绘制同帧延伸。引擎旧 GDI 诊断路径仍需铺平 alpha，因为 `SetDIBitsToDevice` 不处理它；应用呈现不使用这条路径。不通过拉伸正文或复制边缘像素补缝。

无插入标记的静态边界夹具检查四边，其源绘制证据与最终 DWM 输出分别记录；[增量验收](009-WINDOWS-HANDOFF.md#windows-里程碑与增量回归)仅选择受影响的 Fit／DPI 用例。

## 外壳材质与动态状态

`config/safari-shell.json` 管理 Windows 可调光学参数及实测场景参考。材质角色分离顶部背景、浮动表面、紧凑地址栏和标签轨道；饱和度／亮度／对比度采用 ColorMatrix，字形按外壳外观独立于背景取色处理，明暗背景配方平滑插值。这是桌面艺术近似，不是物理折射或 Apple Liquid Glass 内核。

当前原生渲染边缘决定背景色，`theme-color` 只作诊断。Content color API v1 使用普通快照 IPC，仅返回请求／状态／RGB。Closed 地址区取无遮挡视口底边，Open 外壳取顶边。页面帧最后一层侧带填色覆盖整个侧带高度及全部侧栏玻璃面时，Closed 侧栏控件使用该颜色，合成器在提交页面像素时一并更新按钮玻璃装饰。没有这种均匀填色时，控件使用所在左侧或右侧 inset 的取样，首次取样前回退到当前文档的不透明根背景。每条最多 18 个逻辑像素行，按原尺寸绘制后缩至宽 64，再按非预乘 sRGB 求平均并忽略低 alpha 像素。取色使用一个按 180ms 节流的周期，每区域最多一个在途请求，补采次数有界并拒绝旧代次。取样不可用时保留当前文档上次结果，导航重置归属。快照图像不进入页面呈现。

Direct2D 从已完成的页面帧缓冲生成一份可复用的 Closed 侧栏模糊背景。各按钮的现有视觉节点按自身形状裁剪这份背景，前景和玻璃装饰仍独立。即使 inset 填色均匀，滤镜也会读取相邻正文的像素。页面、模糊背景和更新后的玻璃装饰一并提交。Open 玻璃继续使用现有 DirectComposition 滤镜。


各取样区域独立做时间滤波。每通道不超过 3 的变化视为噪声，两次相差不超过 6 的采样确定目标，确认窗口最长 400ms。随后以 120ms 插值调整着色及派生参数，实时背景和前景字形各自呈现，稳定后停止动画。Composition v6 将展开／紧凑材质配方与几何一起插值，地址栏或控件变化时按角色保留仍在使用的玻璃节点。页面、玻璃、装饰和前景使用独立视觉节点，同尺寸表面通过 `EndDraw` 提交损伤并保留内容绑定。`just test-windows-render-stability` 持续采集合成窗口帧，并与原生边缘及材质观察对照。更新计数描述合成工作，捕获时间戳描述 WGC 实际采样。

Appearance API v1 通知 WebKit 和页面媒体查询，不导航。原生输入意图与同向正文移动相关联：累计向下超过 24 CSS px 收起，向上超过 8 展开，意图 350ms 过期；零／反向运动、视口 clamp 和纯脚本滚动不切状态，并发脚本／原生移动仍有限定。

Windows 外壳采用仅呈现的 220ms 三次过渡（`cubic-bezier(1/3,0,2/3,1)`，属于桌面近似，不是实测 Safari 时序）。`windowsMaterial.chromeTransitionMilliseconds` 控制时长；设为零或系统关闭客户区动画时禁用。反向切换从已显示帧衔接，导航、模式／场景及 backing scale 变化取消旧动画，实测展开／紧凑端点保持不变。WebKit 每次切换只接收一次目标 inset；中间控件帧更新保留式合成的裁剪／表面，不重复改变 viewport、执行 DOM 脚本或发布遥测。命中／无障碍框跟随插值后的原生条目，`chromeAnimation` 将呈现进度与 `chromeState` 分开报告。

地址主体／菜单／书签／概览展开外壳，只有重新加载按钮 reload，加号保留返回顶部示意。DirectWrite 使用同一字体测量和绘制。被动摄像头／时钟／Wi-Fi 按场景显隐：Closed 竖向全显示，横向仅摄像头，Open 展开显示时钟／Wi-Fi，Open 紧凑与沉浸均隐藏。本地 `HH:mm` 在静态页也更新；本地化占位不拦截网页输入。

没有 host touch API v1 的运行时保留旧触控仿真：将原生鼠标移动合并为每个动画帧一次合成移动／平移，抬起前处理末尾位移，失焦、页面退出或模式切换取消待处理工作。每个手势只解析一次滚动候选链，每帧写入前重新检查候选／根节点 overflow 及 containment，保留动态弹窗锁滚动。诊断分别计数接收／处理的移动和滚动写入。此分支仍是 DOM 仿真，不含原生惯性滚动。共享观察器复用有界颜色转换缓存；Windows 已有原生覆盖判断，跳过旧 DOM fixed 页脚扫描。宿主向页面发布诊断按 100ms 合并，原生输入／呈现与控制 API 读取仍实时。圆点位置未变时不额外提交合成；设备重建显式清除其缓存。

## Safari 滚动指示器

Windows Safari 抑制根垂直滚动条原生绘制／命中，保留布局与滚动。被动屏幕外缘 DirectComposition thumb 随场景遮罩／变换，独立于玻璃树淡出。嵌套／水平滚动条和沉浸模式沿用原生行为，不注入隐藏滚动条的页面 CSS。

Scroll-indicator API v1 提供文档 epoch、根范围／位置、CSS 抑制及原生移动。必须同时有近期滚轮／键盘意图和实际移动；脚本滚动、零范围、CSS `none` 或隐藏／零宽自定义条不显示 thumb。导航和布局变化清除意图。时序和厚度是工程近似，不是 Safari 测量；其他 scroller 及触摸减速需独立验收，macOS 没有对应宿主策略。

## 半透明固定遮罩

视口内 alpha 与视口外延伸分别处理，fixed 元素仍止于真实布局视口。Fixed-container color API v2 分别返回当前文档覆盖和颜色有效性。宿主检查五个边缘位置与定位包含块，再复用上游背景色及 `PageColorSampler::predominantColor` 规则，跳过前一容器颜色缓存。优先采用原生背景色；回退绘制两像素 fixed/sticky 专用条带，排除文字及 iframe 之外的替换内容，选择主色而非平均无关颜色。透明度／遮罩沿用上游与页面延伸背景混合的规则。普通文档图片不会污染透明 fixed 包装内普通子节点的背景色。多色或部分覆盖保持不确定。Windows 合并图层快照跨原有合成层边界保留祖先透明度，像素只用于计算颜色标量。

宿主预留 inset 时，Windows WC 将受视口约束的合成子树裁剪在真实布局视口内。若相对视口定位的 fixed 图层挂在定位堆叠上下文中，同一裁剪会映射到实际父合成层的坐标。该父层中的普通内容继续延伸，以祖先为包含块的 fixed 后代仍按原定位和绘制规则处理。WC 滚动直接存储图层左上角位置，TextureMapper 只将锚点用作变换原点，带尺寸的裁剪层因而不会在异步滚动时移动 fixed 或 sticky 图层。

采样命中的 fixed 或 sticky 绘制路径上有正在运行的 transform 动画时，延伸色视为不确定，页面元素仍在视口内实时绘制。脚本直接设置 transform 时，几何检查也能识别越过裁剪边的平移。若平移后边缘仍穿过图层内部，该边延伸色继续视为不确定。绘制像素的主色须与同一采样带中平行于边缘的一条线上两端附近的采样吻合。透明全视口命中层也适用此检查，即使它覆盖所有命中点，采样带中部也可能由其他局部前景主导。边缘容器自身或原生 `::backdrop` 的均匀背景可直接提供延伸色。变换稳定且贴合边缘后适用同样的覆盖和取色规则，包括带 forwards fill 的已完成动画。

卡片随后覆盖部分遮罩时，Windows 宿主可保留已稳定的遮罩色。每侧须先在同一透明 fixed 容器内五个常规边缘采样点直接命中同一纯色半透明遮罩。后续须属于同一文档、容器与遮罩，至少保留一个露出的采样点，并确认被前景挡住的位置仍由该遮罩连续覆盖。取色继续跟随该遮罩的实时颜色及透明度。替换遮罩、覆盖缺失或不一致、滤镜或完全遮住该侧的卡片会释放确认。首次绘制即带卡片时，只要同一连续遮罩在前景后方可验证，就沿用上游主色规则。遮罩覆盖不完整或相互冲突，且采样带无法提供连续颜色时，经过已有的有限等待区间便回到文档延伸。此例外沿用实测 Safari 打开顺序，并继续采样其他同容器颜色变化。

页面也可以在布局视口外单独放置与遮罩同色的 fixed 背景块。当前景命中使内侧边缘颜色无法确定时，Windows 宿主在当前非零 inset 内检查属于同一容器的 fixed 背景块。外侧背景块须与同级唯一一块覆盖整个视口的纯色半透明遮罩同色，覆盖整个 inset，在代表性的深度和位置保持最上层命中，绘制路径须避开页面裁剪和效果。如果横跨视口的 `pointer-events: none` 图层先命中，宿主可按 CSS 命中规则定位下方容器并验证其外侧背景，再检查内侧采样带上位于该容器前方的 fixed 子树。重绘边界与采样带相交时，宿主使用范围受限的子树快照检查实际绘制的 alpha，覆盖文字和替换内容。画布有可见像素或快照不可用时，停止恢复下方颜色。模态普通流祖先有统一且覆盖整个 inset 的不透明背景时，宿主将外侧块与该背景混合，否则使用页面延伸背景。这样可在普通流底板盖住文档根背景时使用实际底板。直接合并视口外绘制可能把 WC 已在视口处裁掉的内侧 fixed 遮罩重新计入。宿主仅绘制验证后的颜色标量，保持原有布局和滚动范围。

macOS 桥接在选用系统 WKWebView 的 inset 填充前检查私有偏好 `_setContentInsetBackgroundFillEnabled:` 和 `_updateFixedColorExtensionViews`，可用性随系统 WebKit 版本变化。实测 macOS 27.0.1 提供 `_updateFixedColorExtensionViews` 及视图级的 `_setUsesAutomaticContentInsetBackgroundFill:`，但缺少偏好设置接口。独立探针用视图级接口观察图层，应用桥接兼容性还需独立验证。

`just test-windows-upstream-fixed-colors` 使用通用图案底层夹具，在新文档中比较 v2、保留的上游规则导出和实际 inset 像素，并检查卡片出现及遮罩变色、替换和关闭时的连续遮罩。可用 `just build-windows-webkit-runtime -RuntimeRoot <绝对临时构建目录>` 链接独立验证运行时，成功构建的运行时供后续 checkout 启动使用，已运行实例继续保留加载的 DLL。`DEVDUO_WEBKIT_RUNTIME` 可显式覆盖选择。

Frame presentation API v1 将边缘状态绑定到产出的页面帧及其提交几何。渲染帧内确认无覆盖时立即撤除对应边缘；可靠颜色替换旧值，不确定颜色最多保留 600ms，重复失败不续期。一次性原生失效使用保留页面过期静态不确定颜色，不循环强制 WebCore 重绘。导航／文档及 backing-store 代次拒绝旧结果，侧边拥有角部交点；fixed 取色快照仍只产出标量。

Frame timing API v2 将原生输入序号贯穿渲染／提交、GPU 工作、UI 接收及宿主帧传输／提交。宿主分别记录 `WM_PAINT` 验证及 DirectComposition 提交前后时间，所有时序阶段使用跨进程一致的 Windows QPC 秒，包含合并后保留的原始输入时刻；明确的锚点检查与宿主 Stopwatch 的一致性。时间戳及帧号均不代表屏幕扫描输出。参见 [Microsoft QPC 说明](https://learn.microsoft.com/en-us/windows/win32/sysinfo/acquiring-high-resolution-time-stamps)。诊断历史有界。`DEVDUO_FRAME_TIMING_HISTORY=1` 将可读取历史从默认 8 项扩为最多 128 项，并记录每次成功页面帧提交的物理填充矩形和 RGB。帧表面诊断区分实际 GPU／CPU 传输、GDI 上传及显式捕获。CPU 调用栈属于离散采样，不等于统计 CPU profile。正确的异步滚动仍在实施。

## 桌面输入、对话框与无障碍

Desktop API v1 通过借用的 presenter HWND、四分之一转及偏移映射物理弹窗／caret 坐标。Select 锚点、显示器边界、窗口归属及激活沿可见根所有者处理，原 WebKit HWND 仍是真实输入源。

TSF `ITextStoreACP` 与 IMM 路径驱动真实 WebCore composition。Store 只保存待组合事务，不替代文档；焦点消息 hook 在 Avalonia 翻译前只向文本服务交付一次。UTF-16 预编辑、选区替换、提交／取消及候选窗定位使用真实 editor state。Frame／文档／元素归属拒绝旧操作，焦点变化结束旧组合而不重选旧范围；WebCore 绘制可见 caret，不可见 OS caret 支持 IME 查询。失焦／导航／销毁释放 hook、上下文和 COM，完整周边文本／再转换尚不支持。

普通 `WKPageUIClientV6` listener 提供本地化 owned alert／confirm／prompt，包含请求 frame 来源、串行完成及导航／销毁取消。

`WKViewRequestHostAccessibilityTree` 与 `WKViewPerformHostAccessibilityAction` 经正式 IPC 暴露文档限定的 WebCore AX 语义，旧同步快照导出继续保留。UIA／MSAA 读取已发布缓存，最多一个异步快照在途并合并刷新；密码值不导出，分组节点不将后代文本序列化成可编辑值。导航／销毁拒绝迟到快照。唯一 UIA fragment 归属可见 presenter，源／中间 HWND 通过薄桥接入。快照遍历仍运行于网页进程主线程，大文档上可能昂贵。UIA TextPattern／IAccessible2 尚不支持，验收见 [Windows 交接](009-WINDOWS-HANDOFF.md)。

通知驱动的刷新要求存在监听中的 UIA 客户端，或已为待处理事件安装 WinEvent hook；显式读取／动作仍请求新状态。一次历史无障碍读取不会单独启用永久后台整树重建。订阅者检测较保守，全局监听器可能使其持续活跃，因此不能据此声称 CPU 开销已经实测降低。

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
- **连续帧捕获：** GDI 对分层窗口的回读在切换附近可能与实际合成输出不同。判断可见材质闪烁前，应比较包含首帧的连续 Windows Graphics Capture 帧；截图数量或合成计数本身都不能证明时间稳定性。
- **绘制：** DOM／AX 有内容或提交成功时，像素仍可能全白；静态原生绘制与最终窗口分别验。旧远程白帧停滞未独立定位，不能编造单一根因。
- **后端转换：** 缓存的本地 Skia `DrawTextBlob` 被远程 recorder 重放时发生崩溃。图层提升之间使用后端无关字形操作，并保留旧帧代次保护。
- **就绪与可重入：** 过早同步 AX 发现阻塞启动，回复后清失效会丢新事件；须等待已提交文档并保留泵消息期间的新通知。
- **坐标与参照：** 保留完整标签容器、独立机身／正文变换及 Closed 180° 历史。原生弹窗／caret 必须沿可见 presenter 映射，不能只用离屏源的 `ClientToScreen`。

原始诊断截图、失败尝试及对应构建输入保持私有且不改写。当前限制和指标集中在里程碑，本文不累积逐轮总数。

[English](../en/014-WINDOWS-WEBKIT.md) | [中文](014-WINDOWS-WEBKIT.md)
