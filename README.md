# TuckClip — Clipboard History Manager for macOS and Windows

English · [简体中文](README.zh-CN.md)

[![CI](https://github.com/iajihga/TuckClip/actions/workflows/ci.yml/badge.svg)](https://github.com/iajihga/TuckClip/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/iajihga/TuckClip?display_name=tag)](https://github.com/iajihga/TuckClip/releases)
[![License](https://img.shields.io/github/license/iajihga/TuckClip)](LICENSE)

TuckClip is a free, open-source clipboard history manager for macOS and Windows. It keeps copied text, links, images, and files on your device so you can search, organize, and paste them again whenever you need them.

![TuckClip clipboard history panel on macOS and Windows](docs/images/tuckclip-overview.png)

## Features

- Clipboard history for text, links, images, and files
- Fast search, type filters, pinned items, and quick deletion
- Customizable global shortcut: `⌥⌘V` on macOS and `Ctrl+Alt+V` on Windows by default
- Keyboard navigation and optional automatic paste after selection
- Capture pause, retention period, and history size controls
- Per-app exclusions for clipboard content you do not want to save
- Interface language that follows the system or uses English or Simplified Chinese
- Local storage with no account, telemetry, cloud sync, or runtime network requests

## Download TuckClip

Download the latest version from [GitHub Releases](https://github.com/iajihga/TuckClip/releases/latest):

| Platform | Recommended download |
|---|---|
| Apple silicon Mac | `TuckClip-*-macOS-arm64.dmg` |
| Intel Mac | `TuckClip-*-macOS-x86_64.dmg` |
| Windows x64 | `TuckClip-*-Windows-x64-Setup.exe` |
| Windows ARM64 | `TuckClip-*-Windows-arm64-Setup.exe` |

Portable Windows ZIPs are also available. Every release includes `SHA256SUMS.txt` for file verification.

> Release builds are currently unsigned, so macOS or Windows may ask you to confirm the first launch. Download TuckClip only from this repository and verify the checksum.

## Quick start

1. Launch TuckClip. It stays available from the macOS menu bar or Windows system tray.
2. Copy text, a link, an image, or a file as usual.
3. Press `⌥⌘V` on macOS or `Ctrl+Alt+V` on Windows to open clipboard history.
4. Search for an item, select it, and press Enter to paste.

You can change the shortcut under **Settings → Capture → Shortcut**. If the new combination is already in use, TuckClip keeps the previous shortcut active.

Choose **Follow System**, **English**, or **Simplified Chinese** under **Settings → Capture → Language**. The interface updates immediately.

## Privacy

TuckClip stores clipboard history locally for the current user:

- macOS: `~/Library/Application Support/TuckClip/`
- Windows: `%LOCALAPPDATA%\TuckClip`

Clipboard history can contain sensitive information. Pause capture before copying passwords or private keys, and exclude password managers or other sensitive apps in Settings. See the [Security Policy](SECURITY.md) for more information.

## System requirements

- macOS 14 or later
- Windows 11; Windows 10 22H2 is available on a best-effort basis

Automatic paste on macOS requires Accessibility permission. Without it, TuckClip still places the selected item on the clipboard for manual paste. Manual paste may also be necessary when the target Windows app is running as administrator.

## Frequently asked questions

### What is TuckClip?

TuckClip is a cross-platform clipboard manager that gives macOS and Windows users a searchable history of recently copied text, links, images, and files.

### Is TuckClip free and open source?

Yes. TuckClip is free to use and released under the MIT License.

### Can I change the clipboard history shortcut?

Yes. The global shortcut is customizable on both macOS and Windows from TuckClip Settings.

### Does TuckClip upload or sync clipboard data?

No. TuckClip has no account system, telemetry, cloud sync, or runtime network requests. Clipboard history stays in the local user data directory.

## Build from source

macOS requires Xcode 16.3 or later:

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

Windows requires the .NET 10 SDK selected by `windows/global.json`:

```powershell
dotnet restore .\windows\TuckClip.Windows.slnx --locked-mode
.\windows\scripts\Test.ps1 -NoRestore
dotnet run --project .\windows\src\TuckClip.Windows\TuckClip.Windows.csproj
```

For project structure, development setup, and pull request guidance, read [CONTRIBUTING.md](CONTRIBUTING.md).

## Community and security

Issues and pull requests are welcome. Please follow the [Code of Conduct](CODE_OF_CONDUCT.md). To report a vulnerability privately, use the process described in the [Security Policy](SECURITY.md).

## License

TuckClip is available under the [MIT License](LICENSE).
