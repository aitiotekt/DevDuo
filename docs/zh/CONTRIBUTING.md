# 贡献指南

## 必需环境

通过 mise 管理项目工具，通过已提交的锁文件管理依赖。先安装 Git 和 mise。Xcode 是 macOS 原生开发的额外条件，WebView2 Runtime 是 Windows 原生运行的额外条件。

**所有平台都必须支持真实的文件和目录符号链接。** 文档源文件由文档工具校验的相对链接共享。Windows 贡献者必须使用支持符号链接的文件系统，并启用开发者模式或具备适当的符号链接创建权限。在克隆之前指定 Git 行为：

```sh
git -c core.symlinks=true clone git@github.com:aitiotekt/DevDuo.git
cd DevDuo
git config core.symlinks true
mise install
mise exec -- just setup
```

参考 [Microsoft 开发设置](https://learn.microsoft.com/en-us/windows/advanced-settings/developer-mode) 与 [Git core.symlinks](https://git-scm.com/docs/git-config#Documentation/git-config.txt-coresymlinks)。`doctor` 会实际创建文件/目录链接，`docs check` 会检查相对链接的目标。普通文件替代和 junction 回退均不接受。如果链接被检出为文本文件，先保留自己的修改，再建立正确的检出目录；同步工具不会覆盖普通文件。

## 命令与依赖

```sh
mise exec -- just doctor
mise exec -- just check
mise exec -- just verify
mise exec -- just build-windows
```

just 是仓库主命令入口。mise 选择兼容工具系列，pnpm 管理 JS/文档，uv 管理 Python。Python 自定义参数直接传给已安装的包：

```sh
mise exec -- uv run --locked devduo-tools png-compare captures/reference.png captures/candidate.png --output captures/comparison
```

不要使用全局 pip、系统 Python、`py -3`、临时 `npx` 下载或另一个 uv 管理的解释器。Python 来自 mise，uv 使用该确切可执行文件及锁定项目环境。依赖升级见[开发工具链](010-DEVELOPMENT.md)。需要 hooks 时显式运行 `mise exec -- prek install`，setup 不会自动修改 Git hooks。

## 文档规范

根目录约定文档为英文源文件；`docs/en` 链接到这些文件，并存放编号技术文档。`docs/zh` 存放文件名匹配的中文版本。行为变更应同步两种语言，并更新文档清单。`docsite` 是独立 VitePress 工作区，页面及目录以相对符号链接指向同一组源文件。

```sh
mise exec -- just docs-sync
mise exec -- just docs-check
mise exec -- just build-docs
```

编辑源文件，不复制出单独的网站内容。新增标识符及代码注释使用英文。公开文档不得依赖个人主机名、本地账号、模拟器标识或绝对用户目录。

新增文档补充双向语言页脚。App 新文案同步加入两份资源目录并运行 `just test-localization`，见[本地化](011-LOCALIZATION.md)。新增脚本参照[逐项审查清单](012-SCRIPT-INVENTORY.md)。

## 测量与评审边界

分别记录机身角度、场景角度、CSS 像素、原生点、呈现 DIP 和 framebuffer 像素。保留原始证据字节与哈希。物理未知值保持 null。除非有测量依据，宿主 UI 改动不得改变逻辑视口、预留空间、遮罩及选中标签的完整可见边界。

原生宿主改动应编译两个目标，并进行相关平台交互验收。Windows 交叉编译不代表 Windows 原生运行验证，详见 [Windows 交接](009-WINDOWS-HANDOFF.md)。

原始证据、构建产物、浏览数据、密钥和个人绝对路径不得进入 Git。公开检查覆盖已知模式及批准的相对链接，仍需评审 diff。PR 应说明实际行为、完成的验证及未测试的平台。未经用户要求不要 commit 或 push。

[English](../../CONTRIBUTING.md) | [中文](CONTRIBUTING.md)
