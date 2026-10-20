# 标定与验证

修改参考前先运行已提交基线。[测量约定](002-MEASUREMENT-CONTRACT.md)定义坐标与来源规则，[场景矩阵](005-SAFARI-SCENES.md)是视口和标签几何的统一参考。

## 采集新的 Safari 会话

1. 运行 `mise exec -- just serve`，在本地模拟器中打开标定页。物理设备联网尚不属于已验证流程。
2. 启动目标 Xcode Device Hub runtime，选择 Closed/Partially Open/Open 与机身旋转；每次采集前核对实际姿态。使用 `xcrun simctl list devices booted` 获取本地标识，公开示例始终使用占位符。

```sh
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

模式为 `closed`、`partial`、`open`；方向为 `portrait`、`portraitUpsideDown`、`landscapeLeft`、`landscapeRight`。runner 验证实际姿态，保留原生组件 JSON、明确显示屏的原始 PNG、浏览器记录、源哈希及可选视频。命令成功不证明姿态改变；支持部分展开采集也不代表存在标定后的部分展开模型。

3. 加载与材质运动稳定后采集初始/滚动态，检查实际 scrollY 和 viewport；按需增加上滚/键盘状态。发出 swipe 不等于滚动态已经验证。
4. 展开单/多标签分别采集。对于已加载且带唯一标记的页面，`--tab-mode single --reuse-current-url URL` 避免再建标签。主动保留并恢复此前页面。
5. 每次在忽略的 `captures/` / `measurements/` 下创建新会话目录。记录 runtime 版本和时间，失败尝试独立保留；检查非空内容、尺寸、明确显示屏、时间戳和哈希后才接受图像。默认 XCTest 截图可能来自错误显示屏。

## 更新参考与比较

历史提取器/验证器依赖原参考集布局，不是任意新会话的通用导入器。具体输入和命令统一见[归档指南与审查索引](../../scripts/archive/README.md)。先输出到新的 `temp/` 路径，对照原生框和浏览器记录，保留原始字节后再更新确认的配置。缺失原件必须明确失败。

桌面验收覆盖两种容器、折叠/展开与四个机身角度。Safari 增加初始/滚动和展开标签栏两种设置，共 24 个参考状态。保存应用状态和原始窗口截图，分别比较真实 DOM 视口、原生绘制框、遮挡 inset、组件位置、完整标签边界与碰撞，不能只看请求选项。沉浸模式应清空预留，Fit 保持逻辑视口，语言切换保留页面、滚动和实测几何。

同尺度无损 PNG 使用 `mise exec -- uv run --locked devduo-tools png-compare`，裁剪与排除区域必须显式指定。`contact-sheet --pair REFERENCE CANDIDATE --output NEW.png` 仅供展示，会缩放，不能证明光学保真。参数详见 [Python 指南](../../scripts/python/README.md)，视频时序/哈希检查见 [Swift 指南](../../scripts/swift/README.md)。

比较材质时保留多帧/视频、页面/主题颜色、滚动方向和时序。检查真实窗口，因为 WK 快照可能遗漏可见的 inset 下方延伸。窗口 JPEG 观测及明确标为转换的 PNG 都不是原始无损模拟器 framebuffer。像素差异只描述匹配图对，不代表硬件保真度或 iOS 玻璃内核。

## 已有基线与待验收范围

2026-10-21 本地基线使用真实 macOS 窗口、无障碍、保存状态和独立 Device Hub 参考建立。以下是已有记录，不表示每次编辑文档都会重跑测试。

| 范围 | 已建立的观测 | 剩余边界 |
| --- | --- | --- |
| 参考几何 | 原始来源重新提取与公开配置一致；核对八映射、24 个 Safari 状态及四个完整选中标签框 | runtime/标签数量/旋转历史变化需新证据；无部分展开几何 |
| macOS WKWebView | 检查 Safari/沉浸、Fit、独立遮罩、标签居中及折叠组件下方普通流 | 展开顶部延伸为近似，旧系统材质分支未验证 |
| 语言切换 | 英中切换保留 URL、scrollY 580、382 × 598 视口、原生框/inset，并检查主题和偏好持久化 | Windows 原生语言/无障碍/存储待验收 |
| 便携工具 | 已检查仅公开文件的 setup、源/链接检查、图片/broker 测试及严格双语站构建 | 私有证据验证需要原件，不代表远程 CI 已运行 |
| Windows | 共享 WebView2 路线已交叉编译 | 初始化、输入、DPI、多显示器、zoom Fit、裁剪和外观待[原生验收](009-WINDOWS-HANDOFF.md) |

原始本地证据包括 `captures/desktop-validation/container-modes/`、`under-chrome/`、`selected-tab/`。非对称 90° 遮罩暴露并修复了额外 Y 翻转，单看对称纵向不足以发现；选中标签修正使用完整标题容器，而非较窄的标题点击框；Closed 180° 保留顺时针序列限定。修改渲染时保留这些回归情形。

当前自动检查见[贡献指南](CONTRIBUTING.md)，平台清单见 [Windows 交接](009-WINDOWS-HANDOFF.md)。无论构建是否成功，物理尺寸、真实硬件半径和 iOS 光学参数仍未知。

[English](../en/003-CALIBRATION-WORKFLOW.md) | [中文](003-CALIBRATION-WORKFLOW.md)
