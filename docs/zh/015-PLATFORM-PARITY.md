# 平台能力与 macOS 验收

两个平台均以 [Safari 场景矩阵](005-SAFARI-SCENES.md)为参照。Windows 使用固定源码 WebKit Windows Port，macOS 使用系统 WKWebView；共享源码和另一平台的测试不能认证本平台原生行为。

## 当前验收状态

维护者于 2026-10-28 确认 beta.3 候选版的 macOS 手工验收完成，逐项回执由该轮记录保存。本轮 Windows 文档工作不增加 macOS 通过结论，后续原生变化仍需针对性验证。

Windows 沿用 [`windows-desktop/2026-10-29`](009-WINDOWS-HANDOFF.md#当前状态) 里程碑，保留运行时／宿主归属及待完成的依赖／应用包交付门槛。实现、编译、原生检查和不支持行为分别记录；构建、签名或共享测试不代表光学／输入一致。

## 当前实现与差异

| 范围 | Windows | macOS |
| --- | --- | --- |
| 引擎／运行时 | 源码 WebKit 及必需宿主 API 清单 | 系统 WKWebView；记录真实 OS／WebKit／SDK 能力，不复制 Windows 运行时或修改其锁 |
| 网站存储 | 持久 store，绝对路径 `DEVDUO_WEBKIT_PROFILE` 可隔离测试 | 默认持久网站 store，无对应 profile 覆盖；隔离宿主偏好不等于隔离网站数据 |
| 呈现 | 完整绘制框、四向 obscured inset 和独立 backing scale | Closed 侧边布局使用支持的系统 inset，Open 顶部延伸仍是预留框／颜色近似 |
| 呈现变化 | 旋转、语言、Fit、外框、外观和外壳设置保留视图／文档，形态／模式替换 | 相同所有权约束；原生点／backing 像素与 CSS／场景坐标保持独立 |
| 移动身份／标题 | 首请求前设置原生兼容 UA，按文档确认标题 | Swift 回调／KVO 实现相同策略，两种容器共用观测身份 |
| 本地 Inspector | 显示前 detach 的独立原生 Inspector | 检查私有 selector 能力，以隐藏 attachment target 防止异步 docking 缩小实测视口 |
| 原生远程检查 | 明确的无鉴权 WebKit TCP listener | `isInspectable` 提供系统 Safari Develop 发现，Windows listener 地址／端口不配置该发现 |
| CDP／chii | Token 保护页面桥，独立系统 WebView2 前端 | 同一页面桥，独立非持久工具 WKWebView；不声明引擎调试器或完整 CDP 兼容 |
| 代理 | 原生 system／direct／custom HTTP／HTTPS／SOCKS5 | 支持系统上的 website-store system／custom 配置，拒绝 direct；默认 store 在被测视图间共享，两端均不提供带鉴权代理 |
| 固定容器颜色 | 原生四边 API 及宿主填充 | 检查能力的系统延伸，否则仅有限的不透明全宽 footer／root 回退，不能据此认证半透明遮罩 |
| 滚动指示器 | 原生资格／输入与被动场景外缘 thumb | 无对应宿主策略，系统 WebKit 滚动条和用户偏好仍有效 |
| 玻璃与无障碍 | DirectComposition 近似，限定 WebCore AX／UIA 与讲述人支持 | 系统 AppKit 玻璃及 WKWebView 无障碍，能力与辅助技术覆盖分别验收 |

`src/DevDuo/macos/WebViewBridge.swift`、`DeveloperWebViewBridge.swift` 和 `ScreenshotBridge.swift` 管理 macOS 原生行为；共享宿主／浏览器／场景逻辑位于 `Host/`、`Browser/`、`Device/`、`DeveloperTools/`。Windows 精确 API 及不支持能力见[后端指南](014-WINDOWS-WEBKIT.md)。

## 准备 macOS 复验

mise、Xcode／macOS SDK、真实符号链接和构建命令见[贡献指南](CONTRIBUTING.md)。`just test-macos` 检查 AppKit 布局、输入策略、私有能力／回退和 Inspector 附着，不等于端到端网站或最终窗口验收。

按变化选择下表组件，不重新执行所有场景；未受影响的行为沿用证据。使用隔离宿主偏好／输出和唯一测试 origin／token，不清理无关网站数据。`DEVDUO_WEBKIT_PROFILE` 不隔离 macOS 网站存储。

`just serve` 在回环 8765 提供校准服务，需要时另开受审静态夹具服务：

```sh
mise exec -- uv run --locked python -m http.server 8878 --bind 127.0.0.1 --directory tests/DevDuo.WebKitProbe/pages
```

静态服务不能验证请求头、重定向或代理路由，这些需要受控 recorder；临时代码和回执保持本地并保留来源。

## 原生验收矩阵

对精确候选版选择相关行。几何修改覆盖两种容器、受影响机身映射及初始／紧凑／标签变体，保留 Closed 180° 历史并验证实际方向；无关日常修改不要求完整矩阵或重新配置显示器。

| 变化范围 | 可观察要求 |
| --- | --- |
| 导航、身份与标题 | 离线欢迎／Home、重定向／历史／错误处理、原生首文档／子资源／fetch UA、动态／空／长标题；无 provisional 标题泄漏 |
| 存储与服务 | 同源 token 化 localStorage／IndexedDB／持久 cookie 跨正常重启保留；工具存储隔离，服务重启默认关闭 |
| 场景与呈现 | 呈现变化保留 URL、文档／time origin、表单／历史及合法滚动；记录 clamp 而不脚本恢复；在相关 Fit 检查独立遮罩、完整标签容器和原生框 |
| Inspector 与快捷键 | 独立窗口真实 DOM／Console，网页焦点下 F12／Ctrl+Shift+I，无 docking 视口预留；能力缺失明确报错，无过期前端 |
| 远程／CDP／代理 | 真实 Safari 发现或页面桥、目标重连、listener 清理、互斥及未来流量路由，且不重载当前页面；不绕过 Console 粘贴保护，不将失败 TLS 握手当可信 HTTPS 通过 |
| 延伸、alpha 与材质 | 静态／动画真实流、文档边界、遮罩／backdrop 和原生返回色；明确 Open 近似及系统／回退分支，同时比较实际窗口及 DOM／快照 |
| 输入、生命周期与无障碍 | 变换后点击、键盘／IME、弹窗、VoiceOver、相关 resize／关闭／替换竞争及资源清理；不回调已销毁容器 |

遮罩／指示器夹具通过自有控件或测试 API 逐步操作，等待 settled 状态，不能把测试输入变成生产 DOM 绕过。视口 alpha、inset 纯色和被玻璃遮盖的像素分别观测，明确系统偏好、原生指示器资格及 macOS 场景外缘策略缺口。

## 重要裁决与经验

- 原生子视图逻辑大小必须精确采用场景单位，Fit 缩放祖先层；浮点大小截断不能改变 CSS 视口。
- Inspector 连接与 docking 恢复异步发生，需预留隐藏 attachment target，并验证独立可见前端不缩小设备视图。
- 工具 DOM 有内容时，原生布局／方向错误仍可能导致空白，不能只凭协议求值成功验收。
- 私有 SPI 必须运行时检查；强制回退分支只证明该分支几何／生命周期，不证明系统延伸像素或其他 OS 能力。

## 证据与验收报告

原始状态／窗口截图、运行时／能力分支及源码／二进制来源保存在新建的忽略证据会话。报告只列变化范围、实际执行测试、沿用基线及剩余限制。macOS 点、backing 像素、CSS 视口和原生窗口截图分别记录，Windows DPI 或源栅格结果不能替代。

[English](../en/015-PLATFORM-PARITY.md) | [中文](015-PLATFORM-PARITY.md)
