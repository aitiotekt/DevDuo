# 参考数据与公开边界

运行 DevDuo 使用已检入配置即可。新检出仅在收集/重新提取参考时需要 Device Hub、原始 Safari 会话或 Apple PDF；macOS 编译本身仍需要 Xcode。

| 文件 | 含义 |
| --- | --- |
| config/device-profiles.json | 独立屏幕路径、外壳比例及 canvas 到屏幕的映射，来自本地模拟器图形 |
| config/safari-scenes.json | 八个机身/折叠映射、多标签初始/滚动以及额外八个展开单标签状态 |
| config/safari-shell.json | 可调组件/材质默认值，区分估计和点击边界 |
| config/physical-parameters.json | 物理尺寸及硬件圆角未知值，明确 null |
| config/measurement-matrix.json | 计划采集矩阵，列入计划不代表已经完成 |

当前场景来自 Xcode 27.1 / iOS 27.1 iPhone Duo 模拟器观测，可能随 runtime、Safari 版本、标签数量和旋转历史改变。折叠纵向屏幕为 466×678，展开纵向为 669×951，均为逻辑坐标而非物理尺寸，详见[场景对应](005-SAFARI-SCENES.md)。

公开记录保留源文件相对名称和 SHA-256 哈希以标识原始来源，对应原件不在 Git。哈希不让文件公开可用，也不证明硬件规格。部分不透明浏览器记录 ID 作为来源引用保留，配置不依赖模拟器 UDID、主机名或个人绝对路径。

captures、measurements、work、.local 整体被忽略，可能包含原始截图、视频、浏览标题/URL、原生树、模拟器 ID、工具路径、xcresult 和私人会话笔记。保持原件字节和哈希不变，公开问题或未来证据传递应使用另行检查的脱敏衍生物，不原地改写原件。

历史提取器位于 `scripts/archive/2026-10-21/`：通过 `mise exec -- uv run --locked python scripts/archive/2026-10-21/extract_geometry.py --source SOURCE_DIRECTORY --output temp/device-profiles.json` 从本地图形重建设备配置；`extract_safari_scenes.py --root EVIDENCE_ROOT --output temp/safari-scenes.json` 从原始布局重建当前场景，使用相同前缀。复现说明见[归档](../../scripts/archive/README.md)。两者都解释缺失输入；先输出到 temp，比较后再替换公开配置。采集 runner 可用于新会话，但当前场景提取器不自动识别任意新会话。

完整桌面/选中标签验证器还需要原始原生组件记录。公开检出缺少它们是证据边界，不是运行依赖失败。可移植检查和原生构建只依赖公开文件。模拟器图形来源与分发状态见[第三方说明](THIRD_PARTY_NOTICES.md)。

[English](../en/007-REFERENCE-DATA.md) | [中文](007-REFERENCE-DATA.md)
