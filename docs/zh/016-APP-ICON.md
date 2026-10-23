# 应用图标

图形融合用户提供的 iPhone Duo 开口圆环、五个圆点和开发者代码括号，以青色斜杠与深蓝底色区分。参考图是设计输入，不属于模拟器测量证据。图稿由内置 image_gen 工具生成；平台导出仅做尺寸、遮罩与格式转换，不修改设备几何。

`assets/icons/AppIcon.icon/Assets/artwork.png` 为 1024 × 1024 不透明主图；`icon.json` 是可编辑的 Apple Icon Composer 文档，由系统应用平台遮罩与效果。`Assets.car` 已在 macOS 上用 Xcode 实际编译，并非改名图片。`manifest.json` 记录源文件、产物哈希、编译器与部署目标。

- `app.ico`：Windows 可执行文件的 16、24、32、48、64、128、256 像素图标。
- `app-icon.png`：Avalonia 窗口使用的 256 像素圆角图标。
- `AppIcon.icns`：旧格式兼容资源，1024 像素透明画布中居中放置 824 像素图块。
- `Assets.car`：现代 macOS 资源，与 ICNS 一起放入 `Contents/Resources`；Info.plist 设置 `CFBundleIconName=AppIcon`。

修改主图后，在完整 Xcode 和锁定 Python 环境的 Mac 上运行 `mise exec -- just sync-icons`。`just export-icons` 仅导出 PNG/ICO/ICNS，旧目录清单仍会过期，须继续原生编译。非标准 Xcode 路径可设置 `DEVELOPER_DIR`。`just check-icons` 跨平台验证文件；发布审计也会校验。托管原生打包消费已提交资源，无须重编目录或额外安装 Python。编译和资源验证不代表每种 macOS 配置的 Finder/Dock 外观均已验收。

格式流程参考用户指定项目的现代资源目录与旧版安全边距分离方式，未复用其图案。参见 [Apple Icon Composer 指南](https://developer.apple.com/documentation/xcode/creating-your-app-icon-using-icon-composer)。

2026-10-24 对图标提交 `338c76c` 的验证：ICO 七档尺寸均成功解码，编译目录包含 `AppIcon`，macOS 包内图标字节与 plist 引用均匹配源文件。Avalonia URI 从 macOS、Windows 两种程序集加载到相同 PNG。macOS 27.0.1 的 Finder 缩略图和“显示简介”预览实际显示新标志，Windows 交叉编译通过。随后维护者确认 Windows 实施 Agent 已完成 Windows 图标验证；该原生验证未在本机复跑。其他 macOS 外观变体仍未验证。私有回执保存在 `captures/desktop-validation/app-icons/`。

## 最终生成提示词

生成一枚完整 DevDuo 图标：深蓝底铺满正方形，不预先加圆角；将 Duo 白色开口圆环与底部浅弧上的五个独立圆点，融合为内部的开发者 `</>` 符号，替换 Wi-Fi。括号为白色，斜杠为明亮青色；留出清楚间隙，使用平滑曲线和清晰粗线，不添加文字。平台遮罩和效果交给系统。生成图缩放为 1024 像素主图，原始测量采集未修改。

[English](../../assets/icons/README.md) | [中文](016-APP-ICON.md)
