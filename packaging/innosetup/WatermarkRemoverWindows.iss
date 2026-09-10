#define MyAppName "视频去水印"
#define MyAppEnglishName "Watermark Remover Windows"
#define MyAppVersion "0.4"
#define MyAppPublisher "Watermark Remover Windows"
#define MyAppExeName "WatermarkRemoverWindows.exe"

#ifndef SourceRoot
  #define SourceRoot "..\..\release\staging\portable"
#endif

#ifndef OutputRoot
  #define OutputRoot "..\..\release\installer"
#endif

[Setup]
AppId={{4C3B8E1C-69EF-4B4D-9F81-4E4E8A7C0F31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\WatermarkRemoverWindows
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputRoot}
OutputBaseFilename=WatermarkRemoverWindows_v0.4_Setup
SetupIconFile=..\..\assets\logo\AppLogo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion=0.4.0.0
VersionInfoDescription={#MyAppEnglishName} Installer
VersionInfoProductName={#MyAppEnglishName}
VersionInfoProductVersion=0.4.0.0

[Languages]
Name: "chinesesimp"; MessagesFile: "..\..\..\.tools\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Excludes: "WatermarkRemoverWindows_v0.4_Fix*.exe,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
