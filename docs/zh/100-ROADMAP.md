# 路线图

桌面宿主是围绕实测设备预览的标定工作台：真实地址栏、分组设置侧栏和状态栏，基于 Avalonia 12 并使用 SukiUI 主题。当前行为见[使用指南](004-USAGE.md)和[架构](001-ARCHITECTURE.md)；平台缺口由[平台能力](015-PLATFORM-PARITY.md)和 [Windows 交接](009-WINDOWS-HANDOFF.md)记录。

## 宿主 UI

- 继续改进宿主控件和视觉外观，并在各受影响平台检查键盘、无障碍及实际呈现窗口。
- 真实标签管理是独立的后续工作；当前双标签栏用于示意实测外壳变体。
- 可选：通过原地重新配置遮罩，在切换形态/模式时保留页面。机身旋转已保留原生视图；进一步保留页面需要新的原生证据，证明 viewport、inset 和折叠 180° 行为符合已验收场景。

## Windows 输入与无障碍

- 在原生滚动目标确定后处理 Windows 触控轴锁。触点桥先收到 `touch-action` 和指针位移，此时还不知道命中的嵌套／根滚动链能消费哪些轴。应结合滚动链判定方向意图，覆盖仅单轴可用、主动斜拖、嵌套滚动传递给祖先，并在抬手惯性中沿用判定。验收取消及模式切换，旧 DOM 仿真路径应有等价检查后再调整。
- 实现无障碍文本导航（UIA TextPattern／IAccessible2）、周边文本／再转换及更广泛的辅助技术兼容。当前 Value／Invoke 与讲述人名称／值检查不覆盖这些能力。

## CDP 与 agent 能力

对固定版本 [chobitsu 1.8.6 注册表](https://github.com/liriliri/chobitsu/blob/v1.8.6/src/index.ts)的源码审查区分了真实实现和兼容空操作。

| 优先级 | 当前限制 | 保留 WebKit 时的后续方向 |
| --- | --- | --- |
| 下一步 | Runtime 的按值序列化/context 语义不完整，对象释放注册含空操作。 | 修正序列化/错误边界，释放长时间 agent 会话追踪的对象与分组。 |
| 下一步 | `DOM.getBoxModel` 为空操作，frame/context 路由不完整。 | 补 DOM 几何和选择器，明确 transform、滚动和 frame 坐标。 |
| 下一步 | 输入依靠合成 JS，无法等同原生焦点、默认行为和 IME。 | 桥接经验证的原生点击/输入/滚轮/触控，并保留场景/Fit 映射。 |
| 下一步 | 导航/历史/资源为页面近似，没有能力清单。 | 复用原生导航，暴露真实布局指标/生命周期及逐方法的实现/近似/不支持状态。 |
| 后续 | Network hook 无法补回早期请求、主导航或完整资源/cookie；缓存/配额响应存在占位。 | 明确接入原生网络/存储；页面 JS 无法观察 HttpOnly cookie 或完整流量。 |
| 独立项目 | 断点、Target 自动附加、Emulation 含空操作；缺少 CPU/heap profile、CDP Accessibility 域、真实 worker/browser 会话。 | 评估 WebKit Inspector Protocol/JSC 适配及平台/版本边界；JS 注入无法提供这些引擎能力。 |

优先完成状态 → DOM/几何 → 原生输入/导航 → 截图的 agent 闭环。DevDuo 专用 MCP 可封装已验证的 REST/CDP 操作。[Playwright 的 CDP 连接](https://playwright.dev/docs/api/class-browsertype#browser-type-connect-over-cdp)仅支持 Chromium，其 [WebKit 后端](https://playwright.dev/docs/browsers#webkit)依赖补丁，无法附着系统 WKWebView。

## 验收边界

宿主地址栏与模拟设备内部的 Safari 地址表面职责不同。后者属于标定场景，应保留测量框、悬浮行为和页面衍生外观。多标签开关表示外壳变体。

宿主 UI 改进须保留两种容器、八个机身/折叠映射、展开单/多标签、滚动转换、独立内外路径和实际 CSS viewport。宿主面板可以改变可用显示空间，但不能悄然改变逻辑设备尺寸，修改前后用相同设置核对状态。不将普通 Avalonia 控件覆盖在原生浏览器子视图之上。

Safari 组件、系统玻璃、硬件模拟和设备几何是分别需要证据的工作，通用视觉重设计不能替换测量框参照。

[English](../en/100-ROADMAP.md) | [中文](100-ROADMAP.md)
