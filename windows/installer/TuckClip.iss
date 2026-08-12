#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.1.0"
#endif
#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#if SameText(Runtime, "win-x64")
  #define ArchitectureName "x64"
  #define AllowedArchitectures "x64compatible and not arm64"
  #define InstallArchitectures "x64compatible and not arm64"
#elif SameText(Runtime, "win-arm64")
  #define ArchitectureName "arm64"
  #define AllowedArchitectures "arm64"
  #define InstallArchitectures "arm64"
#else
  #error Unsupported Runtime. Expected win-x64 or win-arm64.
#endif

[Setup]
AppId={{277FCF83-11F5-4B77-A65D-FFAB189D4E90}
AppName=TuckClip
AppVersion={#Version}
AppVerName=TuckClip {#Version}
AppPublisher=TuckClip contributors
AppCopyright=Copyright (C) 2026 TuckClip contributors
AppPublisherURL=https://github.com/iajihga/TuckClip
AppSupportURL=https://github.com/iajihga/TuckClip/issues
AppUpdatesURL=https://github.com/iajihga/TuckClip/releases
VersionInfoVersion={#NumericVersion}
VersionInfoDescription=TuckClip installer
VersionInfoProductName=TuckClip
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductTextVersion={#Version}
DefaultDirName={localappdata}\Programs\TuckClip
DefaultGroupName=TuckClip
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitectures}
ArchitecturesInstallIn64BitMode={#InstallArchitectures}
MinVersion=10.0.19045
OutputDir={#OutputDir}
OutputBaseFilename=TuckClip-v{#Version}-Windows-{#ArchitectureName}-Setup
SetupIconFile=..\src\TuckClip.Windows\Assets\TuckClip.ico
UninstallDisplayIcon={app}\TuckClip.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardResizable=no
CloseApplications=yes
RestartApplications=no
AppMutex=Local\io.github.iajihga.TuckClip
LicenseFile=..\..\LICENSE
UsePreviousAppDir=yes
UsePreviousTasks=yes
UninstallDisplayName=TuckClip

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "third_party\InnoSetup\ChineseSimplified.isl"

[Tasks]
Name: "startup"; Description: "随 Windows 登录启动"; GroupDescription: "其他选项："; Flags: unchecked
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\README.en.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TuckClip"; Filename: "{app}\TuckClip.exe"
Name: "{userstartup}\TuckClip"; Filename: "{app}\TuckClip.exe"; Tasks: startup
Name: "{autodesktop}\TuckClip"; Filename: "{app}\TuckClip.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TuckClip.exe"; Description: "启动 TuckClip"; Flags: nowait postinstall skipifsilent
