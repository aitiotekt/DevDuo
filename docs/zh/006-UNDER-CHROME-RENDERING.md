# 悬浮组件下方的页面绘制

在已验证的 macOS 27 上，折叠 Safari 使用公开 WKWebView obscuredContentInsets：原生浏览器框保持完整 466×678，右侧 inset 84、初始底部 98，使实际页面 viewport 为 382×580；紧凑底部 80，使 viewport 为 382×598。普通流内容可继续绘制到 viewport 下方，fixed-bottom 仍位于布局 viewport 内。横向对应 594×368 / 594×386，270° 的侧边 inset 移到左侧。

minimum/maximum viewport inset 表示 80..98 的底部变化，独立探针测得 100svh 为 580、100lvh 为 598，100dvh 随状态为 580/598。Fit 缩放原生视图坐标空间，不改变这些 CSS 尺寸；实际呈现 DIP 单独记录。

真实应用已检查展开/滚动态，切到沉浸模式后 inset 清空，恢复 466×678 且无 chrome。状态分别记录原生绘制框、实际页面视口、遮挡 inset、绘制策略和延伸是否启用。取色保持在未遮挡内容区域，因为当前 takeSnapshot 会遗漏真实窗口可见的部分 inset 下方延伸。只看快照不能验证实际呈现效果。

本地 under-chrome 证据包含原始 JPEG、初始/滚动/重置状态、源哈希和构建日志，不进入 Git。编号行探针在 innerHeight580 的页面下方真实显示 600..680 行，同时 fixed 标记截止于 580。探针位于 scripts/archive/2026-10-21/viewport_insets_probe.swift：

```sh
mkdir -p temp/cache/swift
xcrun swift -module-cache-path temp/cache/swift scripts/archive/2026-10-21/viewport_insets_probe.swift temp/inset-capability
```

探针自动关闭自己的小窗口，HOLD_PROBE=1 可保持窗口以供原生检查，完成后主动关闭。保留的 PNG 是诊断快照，不证明窗口实际延伸表面。顶部 inset 情况仅作能力探测，未标定为 Safari 顶部实现。

该实现只覆盖当前 macOS 折叠模式。展开及 Windows 仍保留预留内容框方案，Windows 尚未建立等价原生 viewport API 路线。两平台编译成功不能证明 Windows 效果，也不推导 iOS 光学一致或硬件尺寸。原始 Safari 矩阵及采集时冻结源码不变，当前页面使用 DevDuo 遥测命名。

参考 [Apple obscuredContentInsets](https://developer.apple.com/documentation/webkit/wkwebview/obscuredcontentinsets) 与 [WebKit 功能说明](https://webkit.org/blog/17333/webkit-features-in-safari-26-0/)。

[English](../en/006-UNDER-CHROME-RENDERING.md) | [中文](006-UNDER-CHROME-RENDERING.md)
