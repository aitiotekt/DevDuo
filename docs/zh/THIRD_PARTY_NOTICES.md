# 第三方说明与来源

根目录 MIT 许可覆盖第一方 DevDuo 实现与文档，不授予 Apple/Microsoft 产品、商标、SDK、模拟器图形或依赖代码的权利。各依赖保留其自身许可；NuGet、pnpm 和 uv 锁文件记录精确解析结果。

直接依赖包括 Avalonia/Avalonia.Desktop 12、SukiUI（MIT）及 Microsoft.Web.WebView2。SukiUI 引入 Avalonia.Themes.Simple、Avalonia.Controls.ColorPicker、Avalonia.Controls.DataGrid 和 System.Linq.Dynamic.Core；其内嵌字体和图标几何保留各自上游许可。macOS 使用系统 WKWebView/AppKit，Windows 使用独立安装的 WebView2 Runtime。文档使用 VitePress/Vue，开发检查使用 Node 工具；接触表使用锁定的 Pillow 运行时依赖，PDF 遮罩检查可选使用独立安装的 Poppler。这些平台运行时和工具没有作为源代码被捆绑。

`config/device-profiles.json` 包含从本地 Apple Device Hub 模拟器图形变换得到的屏幕路径及外壳几何；`config/safari-scenes.json` 包含模拟器 Safari 测量值与来源哈希。这些数据只描述模拟器外观，不代表真实硬件尺寸。原始 Apple PDF、二进制、截图、视频和提取包不进入 Git。

本项目尚未确立 Apple 原始图形的独立再分发授权。模拟器衍生路径数据与第一方代码分别标注，不因根目录 MIT 许可而被重新授权。分发衍生图形或软件包前应确认相关来源与权利；当前仓库整理不表示已经完成该审查。

iPhone、Safari、Xcode 及相关名称属于 Apple；Microsoft Edge/WebView2 属于 Microsoft。本工具独立开发，不代表关联或背书。

`docs/shared/safari-reference/` 保存经过审查的 Device Hub 窗口 WebP 裁剪衍生图，内容为第一方本地夹具及 Apple 模拟器外壳/Safari UI。索引记录原图哈希、裁剪坐标、编码和观测几何；原始 PNG、原生记录及个人设备元数据继续排除。这些图片用于记录观测行为，不因此获得 Apple UI/外观的 MIT 再分发许可，上述再分发权利不确定性仍适用。

[English](../../THIRD_PARTY_NOTICES.md) | [中文](THIRD_PARTY_NOTICES.md)
