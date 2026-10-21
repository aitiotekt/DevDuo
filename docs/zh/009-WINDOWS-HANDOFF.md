# Windows 交接

## 目标和起点

继续现有 Avalonia/WebView2 实现。使用 README 中的仓库和 SSH 远程地址，不需要特定主机名、检出绝对目录或模拟器 ID。

macOS 已有交互证据，Windows 只有 macOS 上的交叉编译基准。接手先建立真实 Windows 基准并修复平台问题，再按[宿主 UI 路线图](100-ROADMAP.md)改进设备预览之外的应用界面。

## 准备环境

安装 Git 和 mise，支持真实文件/目录符号链接，并以 core.symlinks=true 克隆。这是所有 Windows 贡献者的必须条件，详见[贡献指南](CONTRIBUTING.md)。单独安装 WebView2 Runtime，在仓库根目录 PowerShell 运行：

```powershell
mise install
mise exec -- just setup
mise exec -- just doctor
mise exec -- just check
mise exec -- just serve
```

第二个终端运行：

```powershell
mise exec -- just build
mise exec -- just dev
```

Python、uv、Node、pnpm 和 just 由 mise 管理。CLI 显式选择 mise Python 和锁定环境，不替换为 Windows Python launcher 或全局包。工具链与升级见[开发工具链](010-DEVELOPMENT.md)。Windows 自动选择 net10.0-windows 及其 NuGet lock，不为解决 Windows 依赖问题重写 macOS lock。WebView2 profile 位于当前用户本地应用数据的 DevDuo/WebView2，不进入仓库。

## 代码地图

| 文件 | 职责 |
| --- | --- |
| src/DevDuo/MainWindow.cs | 宿主控件、URL 提交、诊断和预览 |
| src/DevDuo/NativeWebView.cs | 原生子视图生命周期及平台分发 |
| src/DevDuo/WindowsWebViewPanel.cs | WebView2、原生裁剪、GDI 外壳、滚动/主题 |
| src/DevDuo/SafariScene.cs | 场景解析及选中标签几何 |
| src/DevDuo/SafariContainerOptions.cs | 共享配置 |
| src/DevDuo/Fixtures/browser-observer.js | 实际桌面 viewport/theme/scroll |
| src/DevDuo/macos/WebViewBridge.swift | 修改共享代码时需要保留的 macOS 行为 |
| config/safari-scenes.json | 八个映射、24 个初始/滚动参考状态 |

## Windows 必做验收

1. 从只含公开文件的检出运行，验证 WebView2 初始化和缺少 Runtime 的可理解错误。记录通用 OS/runtime/DPI 信息，不记录主机身份。
2. 测试导航、刷新、焦点、输入、滚动、销毁、窗口尺寸及反复切换设备/模式；原生子视图必须裁剪到屏幕内部且不覆盖宿主控件。
3. 覆盖折叠/展开 × 机身 0/90/180/270 × 初始/滚动；展开增加单/多标签。原始截图和状态保存到被忽略的 captures，检查真实 viewport、组件位置与碰撞，而非只看请求配置。
4. 尽量验证 100%/150%/200% DPI 和跨显示器移动；区分 CSS px、WebView2 zoom、原生 DIP、图像像素。Windows Fit 使用 zoom，不能假定等价 macOS。
5. 沉浸模式移除所有 Safari 组件并使用完整逻辑屏幕，切换时不得遗留 inset；外框开关独立，内屏遮罩保持。
6. 四个展开角度检查选中和未选中标签。白色背景使用含关闭按钮的完整 title-container，不使用较窄的标题点击框；字体保持 14 点场景字号居中。
7. 用颜色夹具、theme-color、实际内容取色检查初始/滚动变化。Windows 目前是着色/alpha 回退，没有原生 blur，不能标成已验证 iOS Liquid Glass。

| Safari 场景 | 初始 | 滚动 |
| --- | --- | --- |
| 折叠 466×678 | 382×580 | 382×598 |
| 折叠 678×466 | 594×368 | 594×386 |
| 展开 951×669，多标签 | 951×543 | 951×618 |
| 展开 669×951，多标签 | 669×825 | 669×900 |
| 展开 951×669，单标签 | 951×589 | 951×618 |
| 展开 669×951，单标签 | 669×871 | 669×900 |

这些是模拟器衍生目标，尚不是 Windows 已经符合的证明。Closed 机身 180° 保留记录中的前一横向，修改映射前阅读[场景对应](005-SAFARI-SCENES.md)。

## 证据和限制

公开配置足够运行。完整截图/原生框验证需要另行提供原始私有证据，不得伪造记录或把缺图当通过。证据转移应明确授权并先审查。`uv run --locked devduo-tools png-compare` 使用同尺度 PNG 和显式裁剪；`uv run --locked devduo-tools contact-sheet` 接受显式图片对及可选 `--font`，依赖锁定的 Pillow，其展示缩放不构成精度测试。参见[脚本清单](012-SCRIPT-INVENTORY.md)。

macOS 折叠通过遮挡 inset 保留较小 viewport 并让普通流延伸到地址栏下；Windows 当前缩小原生框。等价路线尚未建立，必须先检查真实内容和 fixed 元素行为，见[组件下方绘制](006-UNDER-CHROME-RENDERING.md)。硬件毫米/半径/铰链角和 iOS 光学内核继续未知。

完成后追加平台验证记录，列出真实结果、缺失用例与复现命令。宿主 UI 改动应能独立识别视口回归；只有用户要求时才 commit/push。

## 语言与命令职责

仓库任务使用 just，uv/pnpm 仅负责各自生态，见[开发工具链](010-DEVELOPMENT.md)。在 Windows 验证 English、简体中文与系统默认、偏好持久化、错误/无障碍文案，以及页面滚动期间切换语言。比较切换前后 URL、滚动与真实 viewport。交叉编译及便携本地化测试不代表这些原生运行结果。见[本地化](011-LOCALIZATION.md)。

[English](../en/009-WINDOWS-HANDOFF.md) | [中文](009-WINDOWS-HANDOFF.md)
