# 可复现标定流程

## 从公开基准开始

先按 [README](README.md) 完成 setup/check/run，再修改配置。每次新会话在被忽略的 captures/measurements 下使用独立目录。记录工具链/runtime 版本与时间，公开文档不加入个人主机名和绝对用户目录。

## 采集真实 Safari

1. 运行 `mise exec -- just serve` 启动 loopback 测量接收器，在本地模拟器中访问标定页。物理设备需要明确的可达地址，尚不属于已验证流程。
2. 在 Xcode Device Hub 中启动目标模拟器/runtime，通过 UI 选择 Closed、Partially Open 或 Open 及机身旋转，每次采集前确认实际显示状态。
3. 本地获取 simulator ID，将其作为参数传入；公开文档始终保留占位符：

```sh
xcrun simctl list devices booted
mise exec -- uv run --locked devduo-tools safari-probe --udid SIMULATOR_UDID --mode closed --orientation portrait --video
```

模式为 closed/partial/open；方向为 portrait、portraitUpsideDown、landscapeLeft、landscapeRight。runner 验证实际姿态，保留原生框 JSON、明确 display 的原始 PNG、浏览器记录、源哈希及可选视频。方向命令成功不证明机身已旋转，屏幕与机身坐标可能不同。

4. 初始加载及材质运动稳定后，分别采集初始与滚动态；核对实际 scrollY 和 viewport。按矩阵补充上滚/键盘状态，不能仅凭已发出 swipe 就判定滚动完成。
5. 展开态分别采集单标签页和多标签页。打开 URL 可能新增标签并改变外壳；单标签页 runner 使用 `--tab-mode single --reuse-current-url URL`，要求页面已经加载且带唯一标记。明确保留/恢复此前页面。
6. 保留原始字节，不做隐式转换。默认 XCTest 图片可能是错误显示器，必须核对明确 display、尺寸、非空内容、时刻和哈希。失败尝试单独标明，不能混入有效集。

## 更新参考

当前提取器匹配[参考数据](007-REFERENCE-DATA.md)中的既有会话布局，并非任意新会话的自动导入器。先生成到 work 临时文件，根据原生组件框和浏览器记录评审后，再替换公开配置。分别处理屏幕 mask、外壳、可见表面和 AX 点击区域。

选中标签的 TabBarTab 点击框不含关闭按钮。背景要使用完整 TabBarItemTitleContainer，并验证关闭按钮区域的并集，避免选中标签比其它标签窄。

## 桌面验证

覆盖两种容器、折叠/展开和四个机身角度。展开 Safari 增加单/多标签页的初始/滚动态。保存桌面状态和原始窗口截图，分别检查实际 CSS 视口、原生框、遮挡 inset、组件位置、标签框和碰撞，不能只核对请求参数。

以下是**归档的特定会话验证器**，用于持有原始私有证据时复现既有基线，不用于自动验收任意新会话：

```sh
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_desktop_scenes.py captures/desktop-session --include-single --output captures/desktop-session/verification.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_tab_strip.py captures/tab-session --output captures/tab-session/verification.json
```

验证器有既定状态文件命名约定，准备新会话前应先检查代码，不推断缺失原件。`uv run --locked devduo-tools png-compare` 对同尺寸无损 PNG 进行显式区域/排除比较；`uv run --locked devduo-tools contact-sheet --pair REFERENCE CANDIDATE --output NEW.png` 生成评审图，将图片适配到面板，不能证明光学一致。参见[脚本清单](012-SCRIPT-INVENTORY.md)与[归档复现说明](../../scripts/archive/README.md)。

材质需要保留多帧/视频、页面颜色、主题、滚动方向及采样时序。macOS 原生玻璃是对 iOS Safari 的近似。当前 WK 快照会遗漏窗口实际呈现的部分 inset 下方内容，因此也要检查真实窗口。可作出的结论由[测量约定](002-MEASUREMENT-CONTRACT.md)约束。

[English](../en/003-CALIBRATION-WORKFLOW.md) | [中文](003-CALIBRATION-WORKFLOW.md)
