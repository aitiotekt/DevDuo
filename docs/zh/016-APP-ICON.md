# 应用图标

图形融合用户提供的 iPhone Duo 开口圆环、五个圆点和开发者代码括号，以青色斜杠与深蓝底色区分。参考图是设计输入，不属于模拟器测量证据。图稿由内置 image_gen 工具生成；平台导出仅做缩放和遮罩。

`assets/icons/AppIcon.icon/Assets/artwork.png` 为 1024 × 1024 不透明主图；`AppIcon.icon/icon.json` 是可编辑的 Apple Icon Composer 文档，系统遮罩与渲染独立于平面图稿。`Assets.car` 在 macOS 上用 Xcode 编译。`manifest.json` 记录源文件、产物哈希、编译器与部署目标。

- `app.ico`：Windows 可执行文件的 16、24、32、48、64、128、256 像素图标。
- `app-icon.png`：Avalonia 窗口使用的 256 像素圆角图标。
- `AppIcon.icns`：旧格式兼容资源，1024 像素透明画布中居中放置 824 像素图块。
- `Assets.car`：现代 macOS 资源目录，放入 `Contents/Resources`，Info.plist 设置 `CFBundleIconName=AppIcon`；ICNS 作为兼容资源。

修改主图后，在完整 Xcode 和锁定 Python 环境的 Mac 上运行 `mise exec -- just sync-icons`。`just export-icons` 仅导出 PNG/ICO/ICNS，资源目录清单在原生编译前仍会过期。非标准 Xcode 路径可设置 `DEVELOPER_DIR`。`just check-icons` 跨平台验证已提交文件，公开信息审计也会校验。托管打包直接使用这些资源，不重编资源目录。参见 [Apple Icon Composer 指南](https://developer.apple.com/documentation/xcode/creating-your-app-icon-using-icon-composer)。

当前验证：ICO 各档尺寸均能解码，编译目录包含 `AppIcon`，macOS 包内图标字节与 plist 引用匹配源文件，Avalonia URI 在两个平台加载同一 PNG。macOS 27.0.1 的 Finder 缩略图和“显示简介”及 Windows 可执行文件图标均已原生检查。程序坞显示及其他 macOS 外观变体尚未验证。私有回执位于 `captures/desktop-validation/app-icons/`。

## 生成提示词

生成一枚完整 DevDuo 图标：深蓝底铺满正方形，不预先加圆角；将 Duo 白色开口圆环与底部浅弧上的五个独立圆点，融合为内部的开发者 `</>` 符号，替换 Wi-Fi。括号为白色，斜杠为明亮青色；留出清楚间隙，使用平滑曲线和清晰粗线，不添加文字。平台遮罩和效果交给系统。

生成的正方形图稿缩放为 1024 像素主图。

[English](../en/016-APP-ICON.md) | [中文](016-APP-ICON.md)
