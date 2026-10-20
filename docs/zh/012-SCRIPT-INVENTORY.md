# 脚本审查与职责

原 `tools/` 下的 **20 个脚本均已逐个审查**：保留或重构 9 个 Python 脚本、合并 1 个 Python 辅助脚本、归档 6 个特定参考集 Python 脚本、将 1 个 Swift 工具保留为软件包、归档 1 个 Swift 实验，并移除 2 个重复的 shell 包装。维护中的 Python 包位于 `scripts/python/devduo_tools`；`tools/` 不再承担执行入口或分类职责。

[机器可读审查索引](../../scripts/archive/index.json)记录每个原脚本的 SHA-256、处理决定、目标位置及理由。原始 captures、measurements、本地记录和随证据冻结的源码快照均未改写。归档只调整路径、导入、显式输出及缺失输入处理；索引中的原哈希对应整理前的脚本。

## 逐项决定

首列路径均相对于原来的 `tools/` 目录。

| 原脚本 | 决定与目标位置 | 具体理由 |
| --- | --- | --- |
| `calibration_server.py` | 保留 → `scripts/python/devduo_tools/calibration_server.py` | 固定资源的回环 HTTP 接收器及并发 JSON 持久化可复用；补充回环绑定约束、UTF-8 写入与输入/错误检查。 |
| `capture_macos.py` | 保留 → `scripts/python/devduo_tools/capture_macos.py` | 显式显示屏、原样 PNG 字节、哈希及浏览器记录关联检查有持续价值；拒绝路径形式的状态标签和全黑/透明采集。 |
| `check_repository.py` | 保留 → `scripts/python/devduo_tools/check_repository.py` | 公开文件、隐私模式及链接的只读审计仍有用；初始暂存索引尚含旧路径时，按工作区删除意图审计，不改写索引。 |
| `png_compare.py` | 重构 → `scripts/python/devduo_tools/png_compare.py` | 保留等尺寸 RGBA 差分、显式裁剪/排除及哈希；以锁定 Pillow 替换不完整的手写解码，检查完整性并支持调色板；拒绝隐式对齐及高位深转换。 |
| `sample_appearance.py` | 保留 → `scripts/python/devduo_tools/sample_appearance.py` | 显式命名像素区域及编码 RGBA 统计可跨平台使用；继续声明色彩管理、材质识别及物理推断限制。 |
| `validate_framebuffer_mask.py` | 保留 → `scripts/python/devduo_tools/validate_framebuffer_mask.py` | 原始本地 PDF 与 framebuffer alpha 的重叠检查可复用，比例必须独立验证；Poppler 为可选外部前提，不自动旋转、缩放或换算物理单位。 |
| `validate_video_evidence.py` | 保留 → `scripts/python/devduo_tools/validate_video_evidence.py` | 检查原视频预期哈希、解码帧尺寸/哈希、非黑像素与首末变化；旧元数据必须显式提供预期源哈希。 |
| `run_safari_probe.py` | 保留 → `scripts/python/devduo_tools/run_safari_probe.py` | 活跃 XCTest 采集代理保留阶段顺序、会话标识、显式显示屏 PNG 回执及原始编码视频；新会话必须使用新目录并保存工具/锁文件哈希，原生采集源码位于 `scripts/DeviceHubUITests/`。 |
| `self_test.py` | 重构 → `scripts/python/devduo_tools/self_test.py` 与 `tests/python/` | 将不透明的 assert 检查改为 unittest，覆盖 HTTP 持久化、无效输入、图片完整性/差分、配置、受管 CLI 边界与证据来源。 |
| `open_safari.py` | 合并 → `capture_macos.py:open_safari` | 保留带标签的模拟器导航能力，不单独维护三行包装；正确保留参数边界、既有查询值和 URL 片段。 |
| `extract_geometry.py` | 归档 → `scripts/archive/2026-10-21/extract_geometry.py` | 原始 PDF 标识、精确路径标记、画布变换及外壳偏移只适用于该 Device Hub 图形；保留推导过程，不宣传为通用提取器。 |
| `extract_safari_scenes.py` | 归档 → `scripts/archive/2026-10-21/extract_safari_scenes.py` | 固定采集日期/目录、3 倍 framebuffer 比例、地址框回退及顺时针 Closed 180° 历史属于规范参考集；保留原生/图片哈希和已验证单标签筛选。 |
| `summarize_safari_session.py` | 归档 → `scripts/archive/2026-10-21/summarize_safari_session.py` | 会话、回执、哈希关联有来源证明价值，但固定 12 姿态矩阵只是历史采集计划，不是未来通用验收标准。 |
| `verify_desktop_scenes.py` | 归档 → `scripts/archive/2026-10-21/verify_desktop_scenes.py` | 独立原生/图片哈希及实际视口/组件检查仍是强证据；固定 16/24 状态与截图命名将其绑定到该参考集。 |
| `verify_tab_strip.py` | 归档 → `scripts/archive/2026-10-21/verify_tab_strip.py` | 保留整标签及关闭按钮的独立原生几何检查；四个固定机身角度、32 点标题容器筛选及 3 点露出间距属于该参考集假设。 |
| `scene_contact_sheet.py` | 归档 → `scripts/archive/2026-10-21/scene_contact_sheet.py`；通用能力由维护包中的 `contact_sheet.py` 替代 | 原场景/状态排布及暗像素、顶部 420 像素排除是历史展示裁剪启发式；新命令显式接收图片对与标签，不猜测裁剪边界。 |
| `inspect_video.swift` | 保留 → `scripts/swift/Sources/InspectVideo/main.swift` | AVFoundation 按实际时间解码视频可复用；整理为 `devduo-inspect-video` 软件包，记录源/帧哈希、要求新输出目录并简洁报告错误。 |
| `viewport_insets_probe.swift` | 归档 → `scripts/archive/2026-10-21/viewport_insets_probe.swift` | 固定 466 × 678 场景和标定 inset 案例构成一次性 macOS 27 能力实验；保留手动构建方法和快照/实际呈现表面的限制。 |
| `build-macos.sh` | 移除 | 四行 zsh 转发包装重复规范 `just build` 配方，没有独立构建行为。 |
| `package-macos.sh` | 移除 | 四行 zsh 转发包装重复 `just package-macos` 与 Node 产物打包，没有独立打包行为。 |

## 维护中的入口

直接使用锁定的 Python 生态入口：

```sh
mise exec -- uv run --locked devduo-tools --help
mise exec -- uv run --locked devduo-tools COMMAND --help
```

| 命令 | 用途与前提 |
| --- | --- |
| `serve` | 回环标定页面与 JSON 接收器。 |
| `capture-macos`、`open-safari`、`safari-probe` | macOS/Xcode 模拟器采集，显式提供设备标识；姿态标签不证明物理状态。 |
| `png-compare`、`sample-appearance` | 使用锁定 Pillow 进行显式图片像素比较或区域统计。 |
| `contact-sheet` | 可重复 `--pair REFERENCE CANDIDATE`，可选对应 `--label`，使用 `--output NEW.png`；仅用于展示。 |
| `validate-mask` | 原始 PDF、framebuffer、显式比例及可选外部 Poppler。 |
| `validate-video` | 原始视频及检查器元数据/帧，必须有预期源哈希。 |
| `check-repository`、`self-test` | 工作区公开内容只读审计及可移植 Python 测试。 |

Python 支持兼容范围（`>=3.12`），mise 选择开发版本线，uv 不会另行下载解释器。Pillow 是锁定运行依赖，Ruff 是开发依赖。参数及格式检查见 [Python 包说明](../../scripts/python/README.md)。

独立 Swift 包要求 macOS 13+ 与 Xcode Swift 工具链：

```sh
swift run --package-path scripts/swift --scratch-path temp/build/swift-diagnostics devduo-inspect-video captures/transition.mov temp/new-video-frames
mise exec -- uv run --locked devduo-tools validate-video temp/new-video-frames
```

参见 [Swift 包说明](../../scripts/swift/README.md)。编码标称帧率不能证明合成器时序或物理刷新率。

## 历史重现边界

归档不是日常维护命令或新检出测试。它们需要另行保存的原始私有证据，缺少必要输入时应失败。[归档说明](../../scripts/archive/README.md)列明各参考集路径、假设、参数及 inset 实验构建步骤，例如：

```sh
mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_safari_scenes.py --root LOCAL_EVIDENCE_ROOT --output temp/reproduced-scenes.json
mise exec -- uv run --locked python scripts/archive/2026-10-21/verify_desktop_scenes.py LOCAL_DESKTOP_DIRECTORY --root LOCAL_EVIDENCE_ROOT --include-single --output temp/historical-verification.json
```

输出应使用原始证据之外的新位置，替换配置前先比较。本次整理在本地从原证据重现出语义一致的设备/Safari 配置；保留的几何验证器通过了 24 个桌面状态和四个选中标签角度。这些结果复用历史采集，不建立新的平台、运行时或光学验证结论。

维护的发布入口是 `scripts/release.mts`（版本/ref 策略、清单和发布验证）及 `scripts/package-release.mts`（原生自包含包、签名和压缩）。流程编排仍归 just 和 GitHub Actions，详见[发布说明](013-RELEASE.md)。

[English](../en/012-SCRIPT-INVENTORY.md) | [中文](012-SCRIPT-INVENTORY.md)
