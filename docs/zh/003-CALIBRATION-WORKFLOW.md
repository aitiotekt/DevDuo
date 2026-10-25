# 标定与验证

[测量约定](002-MEASUREMENT-CONTRACT.md)管理坐标／来源规则，[场景矩阵](005-SAFARI-SCENES.md)管理已接受的视口／标签几何。本文说明采集及比较方法，不记录平台逐轮通过历史。

## 采集新的 Safari 会话

1. 运行 `mise exec -- just serve`，在目标本地 Device Hub runtime 打开校准页；实机网络不在该流程范围。
2. 每次采集前选择形态／机身角度，核对实际显示场景。使用 `xcrun simctl list devices booted` 获取本地标识，公开示例使用占位符。

```sh
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

模式为 `closed`、`partial`、`open`，方向为 `portrait`、`portraitUpsideDown`、`landscapeLeft`、`landscapeRight`。支持 partial 采集不等于存在已标定的部分展开模型，命令成功也不证明实际方向改变。

3. 等待加载／材质运动稳定，记录初始／紧凑／向上或键盘场景的真实滚动及视口；滑动动作不代表状态切换成功。
4. 分别采集 Open 单／多标签。`--tab-mode single --reuse-current-url URL` 可复用唯一标记页面，已有页面应有意识地保留／恢复。
5. 在忽略的 `captures/`／`measurements/` 新建会话，保留原始组件 JSON、明确显示器 PNG、浏览器记录、版本／时间、源码哈希及可选视频。检查非空图像尺寸与选中显示器，默认 XCTest 图像可能来自其他显示器；失败尝试分别保留。

## 更新参考与比较

归档 extractor 假定原批次结构，不是通用导入器。按[归档复现说明](../../scripts/archive/README.md)生成到新临时路径，对比原生／浏览器记录后才修改已接受配置；原件缺失必须明确说明。

桌面只选择受影响检查。几何变化须保留两容器、机身映射、初始／紧凑、Open 标签变体、遮罩及真实 CSS 视口。独立比较实际 DOM 视口、绘制框／inset、完整标签容器与碰撞，不把请求设置当测量值。语言及呈现变化保留文档／滚动，沉浸模式清除 Safari 预留。

`devduo-tools png-compare` 比较等比例无损 PNG，明确裁剪／排除区；`contact-sheet` 会缩放，仅作展示，不能认证光学效果。命令细节见 [Python 指南](../../scripts/python/README.md)，视频／耗时工具见 [Swift 指南](../../scripts/swift/README.md)。

动态材质使用多帧／视频，记录页面／背景／meta 输入及滚动方向。WK 快照可能省略可见的 inset 下正文，仍须检查实际窗口。JPEG 观察或格式转换图不是原始无损 framebuffer，像素差异也不建立硬件尺寸或 Apple 玻璃内核。

## 文档末端与原生定位回归

加载 `/document-end.html?fixed=full`，与 `fixed=narrow`、`fixed=none` 比较。通过页面自有滚动／高度控件保存真实 DOM／状态／窗口证据。夹具 API 支持可重复的脚本滚动，不代表原生滚轮通过。

目标是文档范围内保留真实普通流，范围外使用画布背景；合格的不透明全宽 fixed footer 可独立延伸颜色。窄／移除 footer 不能伪造普通流，Safari 可能保留此前 footer 色，因此比较变体时使用干净页面。Inset 切换保留真实 fixed 框、文档大小及滚动范围。实现规则见 [WebKit 绘制](014-WINDOWS-WEBKIT.md#绘制与文档边界)。

冷启动、resize／恢复、侧栏变化及相关 Fit 下比较原生屏幕与外框，分别记录原生放置／裁剪几何与 DOM。macOS AppKit 检查补充实际窗口观察，不认证 Windows 行为或不可用的系统延伸能力。

## 半透明弹窗背景

分别操作 fixed scrim 和原生 dialog backdrop，切换前清除旧状态；包含 renderer `opacity`、底层全宽 footer、紧凑态和文档末端。这些是页面自有测试输入，不是生产视口扩张或 DOM padding。

Safari 的关键区别是**颜色延伸**而非更大的 fixed DOM 框：视口像素接收 authored alpha，遮挡条带可使用平坦混合画布色而非连续纹理。[WebKit backdrop 实现](https://github.com/WebKit/WebKit/commit/2ae949b78743)及[滤镜／渐变限定](https://bugs.webkit.org/show_bug.cgi?id=300965)说明此边界。Footer 颜色历史是限定，不能当通用目标，也不推广到任意滤镜、渐变或祖先组 opacity。

`fixed-overlay.html` 和 `just test-windows-fixed-overlay` 分别比较视口 alpha 与 inset 颜色。记录前／中／后的文档标识、范围、滚动、fixed 框及视口单位；被 chrome 遮盖的像素不算直接页面采样，该处另保留最终窗口证据。Windows 和 macOS 系统／回退的当前验收见 [Windows 交接](009-WINDOWS-HANDOFF.md)与[平台能力](015-PLATFORM-PARITY.md)。

## Safari 滚动指示器

使用 `/document-end.html?fixed=none&trace=1` 或专用 `scroll-indicators.html`；短文档／overflow／CSS 控件是测试条件，不是 renderer 绕过。

已观察目标为初始隐藏、原生滚动／减速时显示、跟随位置并在运动后淡出。非零静止位置或纯脚本 instant／smooth 运动不要求显示。短内容、`scrollbar-width:none` 和隐藏／零宽 authored 条是负对照。原生键盘、取消 wheel 后脚本运动及端点行为分别记录，不据此规定全部场景／runtime 的精确宽度或时序。

Windows 使用原生资格与被动场景外缘 visual，macOS 保留已记录的宿主策略缺口；不能改变文档视口、fixed 位置或滚动范围制造通过。当前实现归[后端](014-WINDOWS-WEBKIT.md#safari-滚动指示器)，本文不保留带日期结果表。

## 验证归属与经验

[Windows 交接](009-WINDOWS-HANDOFF.md)和[平台能力](015-PLATFORM-PARITY.md)维护当前验收，[贡献指南](CONTRIBUTING.md)选择检查。[受审图集](005-SAFARI-SCENES.md#已入库的-device-hub-参考图)与私有原件提供参照来源，不代表新原生通过。

保留可复用经验：非对称遮罩暴露对称图隐藏的翻转，选中背景使用完整标签容器而非命中框，Closed 180° 受采集历史限定。跨形态 trait／inset 残留或重启后的机身角度变化，会使比较失效，须先恢复干净的实际场景。

原始证据保持私有且不改写，逐次耗时、哈希及通过总数放在回执，只有当前验收基线保留摘要指标。物理尺寸、实机圆角和 iOS 光学参数仍未知。

[English](../en/003-CALIBRATION-WORKFLOW.md) | [中文](003-CALIBRATION-WORKFLOW.md)
