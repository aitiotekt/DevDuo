# 架构

Avalonia 管理宿主窗口和设备/场景选择。`src/DevDuo/NativeWebView.cs` 承载平台原生子视图，由平台渲染器在独立屏幕遮罩内组合浏览器内容和 Safari 外壳。外部设备图形可独立切换，不会移除内部屏幕遮罩。

| 层 | 源文件 | 职责 |
| --- | --- | --- |
| 宿主 UI | `src/DevDuo/MainWindow.cs` | 模式、设备、方向、URL、预览尺寸及状态保存 |
| 共享配置 | `config/`、`DeviceProfile.cs`、`SafariScene.cs` | 屏幕/外壳路径、机身映射、组件及标签几何 |
| macOS | `src/DevDuo/macos/WebViewBridge.swift` | WKWebView、系统玻璃、裁剪、遮挡 inset 及页面取色 |
| Windows | `src/DevDuo/WindowsWebViewPanel.cs` | WebView2、原生区域裁剪、GDI 半透明着色及缩放预览 |
| 观察器 | `Fixtures/browser-observer.js` | 实际桌面视口、主题、标题、主机名及滚动 |
| 标定 | `web/`、`scripts/python/`、`scripts/swift/`、`scripts/DeviceHubUITests/` | 浏览器记录、真实 Safari 原生框、原始图片和比较 |
| 开发 | `justfiles/`、`mise.toml`、生态清单 | just 工作流、mise 环境、pnpm JS/文档与 uv Python 职责 |
| 文档 | 根约定文档、`docs/en`、`docs/zh`、`docsite` | 以相对符号链接共享双语源文件 |

系统浏览器保留真实桌面 UA、DPR、方向及 CSS safe area。设置目标尺寸或标签不能把引擎变成 iPhone Safari。原生视图框、页面 viewport、可见玻璃、点击范围及屏幕坐标分别记录。

macOS 折叠模式使用公开遮挡 inset，使内容真实延伸到悬浮组件下方。展开及 Windows 仍预留较小原生浏览器框。macOS 系统玻璃与 Windows 着色/alpha 是不同材质后端，对 iOS Safari 都属于近似，详见[容器模式](004-CONTAINER-MODES.md)及[组件下方绘制](006-UNDER-CHROME-RENDERING.md)。

接收器只提供固定标定资源并保存本地 JSON，不控制桌面应用或执行任意命令。开发 CLI 使用明确的子进程参数数组；Python 由 mise 选择，依赖由 uv 锁定。另见[开发工具链](010-DEVELOPMENT.md)、[安全](SECURITY.md)及[隐私](PRIVACY.md)。

[English](../en/001-ARCHITECTURE.md) | [中文](001-ARCHITECTURE.md)
