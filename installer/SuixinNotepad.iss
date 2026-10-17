; 随心记事本安装包脚本，由 build.ps1 调用 ISCC 编译：
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\build\app /DOutputDir=..\dist SuixinNotepad.iss
; 按当前用户安装，不需要管理员权限；升级安装时沿用原安装位置。

#ifndef AppVersion
  #error 缺少 /DAppVersion 参数
#endif
#ifndef SourceDir
  #define SourceDir "..\build\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "随心记事本"
#define AppExe "SuixinNotepad.exe"
#define ProgId "SuixinNotepad.txt"
#define MdProgId "SuixinNotepad.md"

[Setup]
AppId={{677BD9FD-E826-42D4-95E5-D7E391CEAADF}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
DefaultDirName={autopf}\SuixinNotepad
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=6.1sp1
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
SetupIconFile=..\src\SuixinNotepad\Resources\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename=SuixinNotepad-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
Name: "openwith"; Description: "加入 .txt、.md 文件的“打开方式”列表"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 应用程序注册：让“打开方式”对话框能找到本程序并显示中文名称
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".txt"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".log"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""; Tasks: openwith
; 文件类型：.txt、.md 右键“打开方式”中出现本程序，但不抢占默认打开程序
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "文本文档"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.log\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\{#MdProgId}"; ValueType: string; ValueData: "Markdown 文档"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\{#MdProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\{#MdProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#MdProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#MdProgId}"; ValueData: ""; Flags: uninsdeletevalue; Tasks: openwith

[Run]
; 静默更新时同样会执行，程序因此在更新后自动重新打开
Filename: "{app}\{#AppExe}"; Description: "运行{#AppName}"; Flags: nowait postinstall

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('{#AppName}需要 .NET Framework 4.8。' + #13#10 +
      'Windows 10 1903 及以后的版本已经自带；其他系统请先从微软官网安装 .NET Framework 4.8，再运行本安装程序。',
      mbCriticalError, MB_OK, IDOK);
end;
