# 使用 DevDuo

下载、安装与首次打开页面见 [README](README.md)。本指南介绍启动应用之后的日常操作。

## 导航与对比

顶部地址栏控制真实浏览器：输入 `http` 或 `https` 网址后按 Enter。后退、前进、重新加载/停止、加载进度和**重试**均跟随当前页面。**主页**打开内置离线欢迎页；日常浏览无需标定接收服务。

| 设置分组 | 功能 |
| --- | --- |
| 设备 | 选择折叠/展开，用**机身 +90°**旋转。旋转保留文档、表单状态和浏览历史；切换折叠形态会重建预览并重新加载当前网址。 |
| 容器 | 选择 Safari 或沉浸式 App WebView；切换模式会重建预览。**多标签页栏**选择展开态 Safari 的双示意标签变体，活动标签显示当前页面标题，并非真实标签管理。 |
| 预览 | **适应预览**缩放展示而不改变逻辑 CSS 视口；**1:1 CSS 像素**取消该展示缩放。**设备外框**可以独立开关。 |
| 外观 | 选择跟随系统、English 或简体中文，以及系统/浅色/深色主题。切换语言保留当前文档、滚动位置和场景。 |
| 材质设置 | 调整 Safari 玻璃的桌面近似效果，不代表 iOS 材质或硬件测量。 |
| 诊断 | 查看页面/原生尺寸和场景信息，或**保存状态**生成本地报告。 |

Safari 模式下，向下滚动收窄外壳，向上滚动展开；不能仅凭滚动位置决定状态。展开态额外标签行仅在开启多标签变体且导航展开时显示。[场景参考](005-SAFARI-SCENES.md)记录了视口尺寸、旋转历史和 Device Hub 图集。

设置和语言会保存，但机身角度在启动时重置，**开发者**分组每次启动默认折叠；材质与诊断的展开状态会保存。**重置设置**恢复 Safari、折叠、机身 0° 和 1:1，保留语言、当前页面和网站数据。存储与删除说明统一见[隐私](PRIVACY.md)。

## 触控与鼠标交互

新配置默认使用**预览 → 交互方式 → 触控 · 单指仿真**。鼠标左键轻点，按下拖动即可滑动；在原生浏览器边界拦截页面滚轮/触控板滚轮，整个模拟手机屏幕内（含 Safari 栏）使用半透明圆形指针，替代系统鼠标；直径保持 20 DIP，不随适应预览缩放或旋转改变。宿主真实地址栏和设置侧栏保留普通指针；圆形指针不进入 WebKit 截图与背景取色。设置侧栏仍可正常滚动。**鼠标**模式恢复桌面滚轮、文本选择和鼠标事件。选择会保存；切换保留文档、视口、历史和滚动，并取消正在进行的接触。

单指仿真发送合成的 touch PointerEvent 和触摸事件，支持嵌套滚动容器，遵循 `touch-action`、`preventDefault()` 及滚动链/containment，并将 Safari 栏展开/收窄归因于手势方向。轻点优先使用引擎兼容 click，必要时只补一次回退 click。页面看到仿真的 `navigator.maxTouchPoints = 1`；鼠标模式恢复原生值。已有及后续 frame 均使用所选模式。

这是桌面 DOM 输入仿真，不是可信的 iOS 输入：触摸/指针事件的 `isTrusted = false`，构造器可用性取决于引擎，原生 CSS pointer/hover 媒体能力保持不变。不复现多点触控、捏合、长按、惯性甩动和 iOS 原生表单手势。需要桌面/原生激活或文本选择的页面可切回鼠标。也可通过 `PATCH /api/settings` 设置 `{"interactionMode":"touch"}` 或 `{"interactionMode":"mouse"}`；实际输入诊断位于 `/api/state` 的遥测中。

## 快捷键

| 操作 | macOS | Windows |
| --- | --- | --- |
| 聚焦地址栏 | ⌘L | Ctrl+L |
| 重新加载 | ⌘R 或 F5 | Ctrl+R 或 F5 |
| 后退 / 前进 | ⌘[ / ⌘] | Alt+← / Alt+→ |
| 显示 / 隐藏设置 | ⌘\ | Ctrl+\ |
| 保存诊断状态 | ⌘S | Ctrl+S |
| 开关 Web Inspector | F12 或 Ctrl+Shift+I | F12 或 Ctrl+Shift+I |

## 检查页面

展开侧栏顶部的**开发者**分组。**打开 Web Inspector**可开关独立原生 Inspector 窗口。**颜色演示**无需服务即可打开内置页面颜色示例；**本地标定页**用于测量，需要运行[标定指南](003-CALIBRATION-WORKFLOW.md)中的接收服务。

如需 Chrome 风格的 DOM/Console 面板，启用 **chobitsu CDP**，再打开 **Chrome DevTools**。前端随包离线提供，无需 Node。此 JavaScript CDP 子集不具备引擎断点、性能分析、完整网络覆盖或浏览器级目标。目标页导航后刷新工具面板以重新连接。关闭 CDP 会停止服务；已经注入当前文档的 hook 需要自行刷新目标页才会移除。

**原生远程检查**是独立功能：macOS 通过 Safari 的开发菜单发现页面，Windows 使用 WebKit 原生 Inspector TCP 协议。两项远程服务启动时均关闭，按需启用即可。连接暴露、token 和共享注意事项见[安全](SECURITY.md)；协议实现细节见[架构](001-ARCHITECTURE.md#开发工具与页面代理)。

## Agent 客户端与固定 CDP 地址

在**开发者**中启用 **chobitsu CDP**。默认基础地址为 `http://127.0.0.1:9222/`，配置的端口及路径在重启和场景切换后保持不变；应用启动时监听仍默认关闭。**复制 CDP 地址**只复制不含凭据的基础地址。

```sh
curl http://127.0.0.1:9222/json/list
curl http://127.0.0.1:9222/json/version
```

从 `/json/list` 读取 `webSocketDebuggerUrl` 并连接，通常为 `ws://127.0.0.1:9222/devtools/page/devduo`。空列表表示页面桥尚未就绪。同一时间仅支持一个调试器，agent 接入前请关闭内置面板。导航或场景变化重建容器后，重新发现目标并连接。

**CDP 请求头令牌（可选）**默认留空，本机连接免鉴权。需要请求头鉴权时，填写与 agent 配置相同的令牌，再点击**应用监听配置**。界面遮蔽显示，令牌保存在本机偏好中；局域网 CDP 必须填写。HTTP 发现接口和 WebSocket 握手均发送 `Authorization: Bearer <token>`，持久令牌不进入 URL。例如，在客户端环境中配置匹配的令牌后：

```sh
curl -H "Authorization: Bearer $DEVDUO_CDP_TOKEN" http://127.0.0.1:9222/json/list
```

这里的环境变量属于客户端，不会配置应用。随包前端静态资源无需凭据。由于浏览器 WebSocket 不能附加自定义 Authorization 请求头，内置面板使用每次启动独立生成的 socket 访问能力，启用请求头鉴权后仍可使用。其内部前端 URL 可能包含这份临时凭据，但复制给 agent 的地址始终固定。凭据存储和暴露范围见[隐私](PRIVACY.md)与[安全](SECURITY.md)。

这是供兼容 agent/MCP/skill 客户端使用的端点，并非内置 MCP 服务，也不是完整 Chromium CDP。先在 DevDuo 中选择设备形态/方向，再用已支持的 DOM/CSS/Runtime 操作检查页面与视口；宿主直接处理 `Page.captureScreenshot`，返回原生视口像素；折叠/旋转控制及完整浏览器/引擎操作仍独立于该页面桥。

## REST 控制 API

**9222 只是 CDP 的默认端口。** 如有其他服务占用，可修改 **CDP 端口**并应用监听配置。固定地址指配置后的端口/路径不再包含随机前缀；端口占用会明确报错，不会悄悄换成随机端口。

需要时在**开发者**中启用 **REST 控制 API**。它独立于 CDP，**每次启动默认关闭**。**控制 API 端口**默认为可修改的 **9224**；**应用控制 API 设置**保存端口/令牌，并重启已经运行的监听。它仅绑定 `127.0.0.1`，不受**允许局域网连接**影响。关闭应用或重置设置会停止服务；切换设备形态/容器不会停止服务。

- 打开 `http://127.0.0.1:9224/api/openapi`，阅读离线 **Swagger UI**，通过 **Try it out** 试调接口。
- 获取 `http://127.0.0.1:9224/api/openapi.json`，读取 OpenAPI 3.1 JSON。服务地址和鉴权要求反映当前运行配置。
- 控制令牌留空时本机免鉴权；填写后发送 `Authorization: Bearer <token>`。Swagger UI 中点击 **Authorize**，填写不含 `Bearer` 前缀的令牌。文档和静态资源公开可读；状态/控制接口受已配置的请求头保护。Swagger 不持久化鉴权信息，也不向外部校验服务发送规范。

| 资源 | 方法 | 作用 |
| --- | --- | --- |
| `/api/state` | GET | 读取设置、导航、场景坐标映射及真实原生/页面遥测。 |
| `/api/settings` | PATCH | 修改折叠、绝对机身角度、Safari/App 模式、示意标签栏、触控/鼠标交互、预览缩放/外壳、主题、语言和材质参数。仅修改提供的字段；材质值为 null 时恢复默认值。 |
| `/api/navigation` | POST | `navigate` 搭配 http/https `url`；或 `back`、`forward`、`reload`、`stop`、`home`、`color-demo`、`calibration`。 |
| `/api/inspector` | PUT | 将原生 Inspector 的 `visible` 设为 true/false。 |
| `/api/screenshot` | POST | 返回 PNG/JPEG 字节；`target` 选择 `viewport`（默认）或包含 Safari shell 与已启用边框的 `device`。 |
| `/api/proxy` | PUT | 设置代理 `mode`；自定义模式使用无认证的 `endpoint`。macOS 仍不支持强制直连。 |

```sh
curl http://127.0.0.1:9224/api/state
curl -X PATCH http://127.0.0.1:9224/api/settings \
  -H 'Content-Type: application/json' \
  -d '{"fold":"open","bodyAngle":90,"containerMode":"safari","showTabStrip":true}'
curl -X POST http://127.0.0.1:9224/api/navigation \
  -H 'Content-Type: application/json' \
  -d '{"action":"navigate","url":"http://localhost:5173"}'
```

启用鉴权后，为每个控制请求附加相同的 Bearer 请求头。两种界面语言均使用英文规范标识符。未知字段、非法值和重复字段会被拒绝；参数范围和错误响应见 JSON 规范。请求体限制为 64 KiB，不开放浏览器跨源访问或 CORS。

除截图的二进制响应外，成功响应返回当前宿主状态，页面加载和原生布局仍可能尚未完成。轮询 `/api/state`，对照 `settings` 与真实 `telemetry`/`navigation`，不要把请求尺寸当成已经绘制的尺寸。与 UI 一致，切换折叠或容器会重建页面并重置滚动/历史；旋转、预览、材质和语言修改保留文档。Closed 180° 的旋转历史限定仍然适用，角度命令不等于原生场景验收。标签栏仍为示意，不是真实多标签管理。页面 DOM/CSS/执行/滚动继续使用独立的 CDP；控制 API 不提供远程 shell、文件系统访问或内置 MCP 服务。

## 原生截图

启用 REST API 后，通过 JSON 参数选择截图范围：

```sh
curl -X POST http://127.0.0.1:9224/api/screenshot \
  -H 'Content-Type: application/json' \
  -d '{"target":"viewport","format":"png"}' --output viewport.png
curl -X POST http://127.0.0.1:9224/api/screenshot \
  -H 'Content-Type: application/json' \
  -d '{"target":"device","format":"jpeg","quality":85}' --output device.jpg
```

配置鉴权时照常添加 Bearer 请求头。`format` 默认 `png`；JPEG 的 `quality` 范围为 0–100，默认 80，PNG 须省略该参数。响应直接返回图片字节，不是 JSON，也不会在服务端保存文件。`X-DevDuo-Width`、`X-DevDuo-Height`、`X-DevDuo-Target` 和 `X-DevDuo-Pixel-Scale` 描述输出图片。

| 范围 | 截取内容 |
| --- | --- |
| `viewport` | 当前网页视口，保持网页正向，每 CSS 像素输出一个图片像素。不含 Safari 栏、外壳和宿主圆形触摸指针；适应预览和桌面 DPI 不改变输出尺寸。 |
| `device` | 整台手机的最终原生合成画面，包含 Safari 栏、栏下延伸区、屏幕蒙版及已启用的外壳。保留当前机身方向、适应预览缩放和桌面 backing scale。不包含宿主真实地址栏/设置或系统鼠标；若圆形模拟触摸指针可见，它属于设备合成内容。App WebView 模式没有 Safari shell。 |

截图不修改 viewport、滚动、fixed 布局、inset 或预览缩放，不滚动拼接整页。设备截图要求手机完整显示在可见预览区内，必要时由使用者先选择适应预览。窗口隐藏/最小化、设备被裁切、原生捕获不可用，或截图期间视口/文档/呈现变化时返回 `409`，可待布局稳定后重试。不冻结动画；前后几何检查不代表 DOM 观测和像素捕获是原子操作。实现见[原生截图管线](001-ARCHITECTURE.md#原生截图管线)。

现有页面 WebSocket 的 `Page.captureScreenshot` 使用同一条**视口**路径，返回 `{"data":"<base64 image>"}`：

```json
{"id":1,"method":"Page.captureScreenshot","params":{"format":"png","fromSurface":true,"captureBeyondViewport":false}}
```

支持 PNG/JPEG 及 JPEG 质量。请省略 `clip`；区域/整文档截图、WebP 和 `fromSurface:false` 不受支持，返回 `-32602`。不带 clip 时，`captureBeyondViewport` 仍只截当前视口；`optimizeForSpeed` 作为提示接受。原生截图失败返回 `-32000`。这不代表页面桥已经兼容完整 CDP 或 Playwright 的浏览器端点。

## 页面代理

选择代理模式并点击**应用代理**。Windows 支持系统、直连和自定义；macOS 支持系统和自定义。自定义端点使用带端口且无认证的 `http`、`https` 或 `socks5`。应用时保留当前文档，仅影响后续请求。代理/监听配置及明确填写的 CDP 请求头令牌会保存，远程服务启用状态不保存。已存设置和网络数据见[隐私](PRIVACY.md)。

## 诊断与排错

**保存状态**在安装版中写入本地应用数据的 `DevDuo/captures`，在源码检出中写入忽略的 `captures/desktop-validation/container-modes/`。`DEVDUO_OUTPUT_DIR` 可指定其它输出目录，`DEVDUO_URL` 可覆盖记住的启动网址。附加报告前请自行检查；截图、标题和网址可能包含私人信息。

页面加载失败时，检查完整网址及本地开发服务器是否运行，再点击**重试**或**重新加载**。Windows 报运行时缺失通常是应用归档未完整解压，请保留整个 `webkit/` 文件夹。运行时覆盖和源码构建统一见 [Windows WebKit 指南](014-WINDOWS-WEBKIT.md#avalonia-后端)，下载被阻止见[安装帮助](013-RELEASE.md#发行包签名与安装帮助)。

两种容器均使用观察到的移动 Safari 请求身份，但真实引擎、platform 和 DPR 仍为原生桌面值。兼容性限制见[移动页面请求身份](014-WINDOWS-WEBKIT.md#移动请求身份)。现有平台缺口集中记录在[平台对齐](015-PLATFORM-PARITY.md)，未来宿主 UI 改进见[路线图](100-ROADMAP.md)。

[English](../en/004-USAGE.md) | [中文](004-USAGE.md)
