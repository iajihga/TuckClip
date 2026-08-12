# TuckClip

[English](README.en.md) · 简体中文

[![CI](https://github.com/iajihga/TuckClip/actions/workflows/ci.yml/badge.svg)](https://github.com/iajihga/TuckClip/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/iajihga/TuckClip?display_name=tag)](https://github.com/iajihga/TuckClip/releases)
[![License](https://img.shields.io/github/license/iajihga/TuckClip)](LICENSE)

一款简洁、本地优先的 macOS 与 Windows 剪贴板历史工具。

![TuckClip 使用界面](docs/images/tuckclip-overview.png)

## 功能

- 保存文本、链接、图片和文件复制记录
- 搜索、类型筛选、置顶与快速删除
- 全局快捷键呼出，支持在设置中自定义
- 键盘导航与选择后自动粘贴
- 暂停记录、保留期与容量控制
- 按应用排除不想记录的内容
- 无账号、无遥测，历史保存在本机

## 下载

前往 [GitHub Releases](https://github.com/iajihga/TuckClip/releases) 下载适合设备的版本：

| 平台 | 推荐文件 |
|---|---|
| Apple 芯片 Mac | `TuckClip-*-macOS-arm64.dmg` |
| Intel Mac | `TuckClip-*-macOS-x86_64.dmg` |
| Windows x64 | `TuckClip-*-Windows-x64-Setup.exe` |
| Windows ARM64 | `TuckClip-*-Windows-arm64-Setup.exe` |

Windows 还提供免安装的 `portable.zip`。每个版本附带 `SHA256SUMS.txt` 校验文件。

> 当前公开构建未购买商业代码签名。macOS 或 Windows 首次启动时可能显示来源提示；请只从本仓库 Releases 下载并核对校验值。

## 使用

1. 启动 TuckClip，它会常驻菜单栏或系统托盘。
2. 正常复制文本、链接、图片或文件。
3. macOS 按 `⌥⌘V`，Windows 按 `Ctrl+Alt+V` 打开历史面板。
4. 搜索或选择一条记录，按回车粘贴。

唤起快捷键可在“设置 → 记录 → 快捷键”中修改；新组合键如果被占用，TuckClip 会继续保留之前可用的快捷键。

## 平台要求

- macOS 14 或更高版本
- Windows 11；Windows 10 22H2 尽力兼容

macOS 的自动粘贴需要辅助功能权限。没有权限时仍可恢复剪贴板内容并手动粘贴。Windows 中遇到管理员权限应用时也可能需要手动粘贴。

## 数据与隐私

TuckClip 不包含账号、遥测、云同步或运行时网络请求。剪贴板历史保存在当前用户的本机目录：

- macOS：`~/Library/Application Support/TuckClip/`
- Windows：`%LOCALAPPDATA%\TuckClip`

剪贴板可能包含敏感信息。建议在复制密码或密钥前暂停记录，并在设置中排除密码管理器等应用。更多说明见 [安全政策](SECURITY.md)。

## 从源码构建

macOS 需要 Xcode 16.3 或更高版本：

```bash
xcodebuild \
  -project TuckClip.xcodeproj \
  -scheme TuckClip \
  -configuration Debug \
  -derivedDataPath .build/DerivedData \
  CODE_SIGNING_ALLOWED=NO \
  build

./scripts/test.sh
```

Windows 需要仓库 `windows/global.json` 指定的 .NET 10 SDK：

```powershell
dotnet restore .\windows\TuckClip.Windows.slnx --locked-mode
.\windows\scripts\Test.ps1 -NoRestore
dotnet run --project .\windows\src\TuckClip.Windows\TuckClip.Windows.csproj
```

## 参与项目

欢迎提交 issue 和 pull request。开始前请阅读 [贡献指南](CONTRIBUTING.md)、[行为准则](CODE_OF_CONDUCT.md) 与 [安全政策](SECURITY.md)。

## 许可证

TuckClip 使用 [MIT License](LICENSE)。
