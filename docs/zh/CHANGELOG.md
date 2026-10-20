# 变更日志

## 未发布

- 增加 `devduo-metadata.toml` 作为发布配置来源，支持版本同步设置、递增和一致性检查。
- 增加带检查门禁的 macOS/Windows 发布打包、macOS 签名公证、产物完整性与来源验证，以及仅 main 的 Pages 部署。
- 建立 Avalonia 桌面视口工作台，macOS 使用 WKWebView，Windows 使用 WebView2。
- 添加独立屏幕/外壳几何、Safari 与沉浸模式、八个机身/折叠映射、初始/滚动态及展开单/多标签页变体。
- 修正选中标签页可见边界及文案居中，物理硬件未知量保留 null。
- 添加采集、提取、截图对比与验证工具，原始证据不进入 Git。
- 以 just 作为仓库主命令入口，mise 使用兼容工具系列，pnpm/uv 仅管理各自生态命令及冻结依赖。
- 逐项审查原 20 个工具，归并维护中的 Python/Swift 工具，移除冗余包装，记录仅供历史复现的归档。
- 新增持久化英/简中 App 本地化、原生组件/自有示例页即时更新及资源/场景不变性测试。
- 按编号整理中英文文档及 VitePress 文档站，强制相对符号链接及语言源文件配对检查。
- 将 macOS WKWebView 桥接移入 `src/DevDuo/macos`，将 Device Hub XCUITest 探针移入 `scripts/DeviceHubUITests`。

Windows 原生运行验收和宿主 UI 改进仍待完成。本节记录开发工作，不表示已经发布发行版，详见[验证记录](008-VALIDATION.md)和[路线图](100-ROADMAP.md)。

[English](../../CHANGELOG.md) | [中文](CHANGELOG.md)
