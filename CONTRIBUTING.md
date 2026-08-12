# 参与 TuckClip

感谢你愿意改进 TuckClip。小修复可以直接提交；涉及存储格式、隐私过滤、全局事件、输入注入或发布流程的改动，建议先开 issue 说明问题和方案。

提交代码即表示你有权按项目的 [MIT License](LICENSE) 提供这些内容。参与讨论和评审时请遵守 [行为准则](CODE_OF_CONDUCT.md)。

## 仓库结构

- `TuckClip/`、`TuckClipTests/`、`TuckClip.xcodeproj/`：macOS 客户端和 XCTest。
- `windows/src/TuckClip.Core/`：不依赖 UI 或 Win32 的共享领域逻辑。
- `windows/src/TuckClip.Platform.Windows/`：剪贴板监听、全局快捷键、DPAPI 和安全粘贴等 Win32 边界。
- `windows/src/TuckClip.Windows/`：Avalonia UI 与应用协调层。
- `windows/tests/`：Windows Core、平台、设置集成与 UI 自动测试。
- `shared/specs/`：两个平台应共同遵循的剪贴板与历史约定。
- `scripts/`、`windows/scripts/`、`windows/installer/`：测试和发布打包。

## 开发环境

### macOS

- macOS 14 或更高版本
- Xcode 16.3 或更高版本
- Git

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

### Windows

- Windows 11，优先在与改动匹配的 x64 或 ARM64 设备上开发
- PowerShell 7
- `windows/global.json` 指定的 .NET 10 SDK
- 构建安装器时需要 Inno Setup 6.5 或更高版本
- Git

```powershell
dotnet restore .\windows\TuckClip.Windows.slnx --locked-mode
.\windows\scripts\Test.ps1 -NoRestore
```

依赖升级时先正常 restore 明确更新 lock 文件，审阅包名、版本和传递依赖，再恢复使用 `--locked-mode`。不要在 CI 中隐式重写 `packages.lock.json`。

Windows 10 22H2 是 best effort 兼容目标，不是开发环境下自动推定“已经支持”的依据；涉及窗口、输入、剪贴板或安装器的改动，仍以 Windows 11 实机结果为主要验收证据。

## 修改原则

- 保持 macOS 14 和 Windows 11 支持；避免无版本保护地使用更高系统 API。
- 保持 `TuckClip.Core` 与 UI/Win32 解耦。系统行为通过小接口注入，便于确定性测试。
- 剪贴板内容默认留在本机。新增网络、遥测、云同步、更新检查或外部服务前必须先讨论，并同步更新隐私与安全文档。
- 不要在日志、测试夹具、issue、TRX、截图或提交中放入真实剪贴板内容、密钥、用户名和本机绝对路径。
- 文件 I/O、图片解码、DPAPI 和较大的 JSON 编解码不应阻塞 UI 线程。
- 存储修改必须保留原子写入、保存失败回滚、路径约束和损坏历史的只读保护。
- 自动粘贴必须冻结目标与条目。无法确认目标、焦点、剪贴板序列、权限或输入投递时，降级为只复制。
- Windows 客户端保持 `asInvoker`、`uiAccess=false`，不得靠整体提权绕过 UIPI。
- 尊重 Windows 剪贴板隐私标记和 macOS 私密/瞬时 pasteboard 标记。过滤规则只能增加，不能在没有安全论证时放宽。
- 新行为应有回归测试。系统 API 难以自动化时，提供可注入边界和明确人工步骤。

## 提交前自检

1. 查看 `git diff`，确认没有临时产物、个人路径、证书、秘密或无关改动。
2. 运行受影响模块的定向测试，再运行完整 macOS 或 Windows 测试。
3. 使用 locked restore，在 Release 配置构建；Windows 同时发布 `win-x64` 与 `win-arm64` 并检查 PE machine。
4. 如果改动影响共享行为，对照 `shared/specs/` 检查两个平台，或明确记录平台差异。
5. 手工检查全局快捷键、暂停/恢复、面板焦点、自动粘贴降级和权限被拒绝/目标提权场景。
6. 更新受影响的 README、隐私边界、安全说明和人工验收记录。

Pull request 请写清问题、改动范围、测试命令、实机环境、结果和已知限制。界面变更可以附截图，但必须先清理个人数据。

## 实机验收

托管 CI 没有可靠的交互式桌面会话，不能证明剪贴板监听、全局热键、焦点恢复或输入注入正确。发布前必须在真实登录会话中验收。

### macOS

至少在 Apple 芯片设备验证 `arm64` DMG；Intel 资产需在 Intel Mac 或可信等价环境验证：

1. 校验 SHA-256、DMG 完整性、二进制架构和签名状态。
2. 拖拽安装，验证未公证 Gatekeeper 路径与文档一致。
3. 分别验证剪贴板权限允许、拒绝和状态变化。
4. 验证默认与自定义唤起快捷键、搜索、方向键、1–9、置顶、删除、暂停与恢复。
5. 在普通目标和辅助功能权限被拒绝时验证自动粘贴与只复制降级。
6. 验证文本、链接、图片、Finder 文件、排除应用、敏感内容过滤和保留期清理。

### Windows

正式发布至少覆盖 Windows 11 x64；ARM64 资产需要 Windows 11 ARM64 实机确认。Windows 10 22H2 结果应标记为 best effort：

1. 分别校验 portable ZIP 与 Inno installer 的 SHA-256；确认资产架构和文件名一致。
2. 验证安装器以当前用户安装、不触发 UAC、可卸载，且卸载不会暗中删除历史；验证 portable 包不把数据写在程序目录。
3. 记录未签名 SmartScreen 的实际提示，确认文档没有把未签名构建描述成可信发布者。
4. 验证单实例、托盘/面板生命周期、默认与自定义唤起快捷键、搜索、方向键、1–9、置顶、删除、暂停和立即恢复；另在 1366×768、150% 缩放等紧凑工作区检查窗口没有被裁切。
5. 分别复制文本、链接、图片和资源管理器文件，验证去重、来源、保留期和上限。
6. 验证 `ExcludeClipboardContentFromMonitorProcessing`、`CanIncludeInClipboardHistory=0`、`CanUploadToCloudClipboard=0`、排除进程和常见密码管理器内容不会进入历史；快速交替敏感与普通来源时，序列号或 owner 不稳定的快照应整体丢弃，不得按后一进程错误归因。
7. 在普通非提权目标验证自动粘贴；在管理员目标、目标切换、焦点恢复失败、剪贴板被抢占和输入失败时确认只复制且不误投递按键。
8. 验证暂停状态能快速切换，不阻塞 UI；睡眠/唤醒和注销/登录后快捷键与监听可恢复。
9. 验证 `%LOCALAPPDATA%\TuckClip` 的历史元数据和图片不可直接读出，且换 Windows 用户后 DPAPI 解密失败会进入安全只读/错误路径，不覆盖原文件。
10. 用合成配置损坏或临时拒绝 `settings-v1.json` 读取，确认原文件不被覆盖，并以“暂停记录、关闭自动粘贴和图片捕获”的恢复状态启动。

人工测试只能使用测试数据和获准设备，不要为了验证过滤而复制真实密码或密钥。

## 打包命令

macOS 单架构 DMG：

```bash
./scripts/package-release.sh vX.Y.Z arm64 ./dist-local
./scripts/package-release.sh vX.Y.Z x86_64 ./dist-local
```

Windows portable 与安装器：

```powershell
.\windows\scripts\Build-Installer.ps1 `
    -Tag vX.Y.Z `
    -Runtime win-x64 `
    -OutputDirectory .\dist-local

.\windows\scripts\Build-Installer.ps1 `
    -Tag vX.Y.Z `
    -Runtime win-arm64 `
    -OutputDirectory .\dist-local
```

Windows 脚本只接受 `vMAJOR.MINOR.PATCH[-PRERELEASE]`，执行 locked restore，生成 self-contained 包，检查 PE machine，移除并复查 PDB，并拒绝覆盖已有同名资产。请使用新的空输出目录重跑失败构建。

## 发布者清单

推送版本 tag 会直接触发 `.github/workflows/release.yml` 并创建公开 Release。不要把 tag 当作 CI 试运行按钮；在以下项目全部完成前不要创建 tag：

1. tag、版本号和干净工作区一致；macOS 与 Windows 完整自动测试均通过。
2. 两个平台的实机清单完成，并记录具体系统版本与架构。Windows 交互行为不能只引用 `windows-2025` 托管 CI。
3. 最终资产恰好包含两个 DMG、两个 portable ZIP、两个 Inno installer 和一个统一 `SHA256SUMS`。
4. macOS 分别核对 `arm64`/`x86_64`；Windows 分别核对 publish/portable 包内 `TuckClip.exe` 的 PE machine `0x8664`/`0xAA64`（不要用 Inno bootstrap 自身架构代替），并从最终上传资产重新计算 SHA-256。
5. 发布说明明确最低系统、架构、签名和公证状态：当前 macOS 是 ad-hoc 且未公证；Windows 没有 Authenticode 签名，会触发 SmartScreen。
6. Windows 普通权限与 UIPI 降级、DPAPI 用户绑定、Windows 10 best effort 均在发布说明中可见。
7. 确认同 tag Release 不存在。工作流和脚本不会覆盖已有资产；发现错误应停止并使用新的版本 tag，不要静默替换公开二进制。

如果未来声称 Developer ID、公证或 Authenticode 签名，必须在发布前用最终下载资产重新验证，不能根据文件名或构建日志推断。
