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
; 与 Core/SingleInstance.cs 中的 MutexName 一致
#define AppMutexName "SuixinNotepad.Running"

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
Name: "defaulttxt"; Description: "设为 .txt 文件的默认打开程序"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 应用程序注册：让“打开方式”对话框能找到本程序并显示中文名称
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".txt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".log"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
; 文件类型：.txt、.md 右键“打开方式”中出现本程序；默认打开程序只由用户在系统对话框中确认
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "文本文档"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.log\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\{#MdProgId}"; ValueType: string; ValueData: "Markdown 文档"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#MdProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\{#MdProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "{#MdProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "{#MdProgId}"; ValueData: ""; Flags: uninsdeletevalue
; 默认应用功能：本程序出现在“设置 > 应用 > 默认应用”中
Root: HKA; Subkey: "Software\SuixinNotepad"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\SuixinNotepad\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"
Root: HKA; Subkey: "Software\SuixinNotepad\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "多标签纯文本编辑器"
Root: HKA; Subkey: "Software\SuixinNotepad\Capabilities\FileAssociations"; ValueType: string; ValueName: ".txt"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\SuixinNotepad\Capabilities\FileAssociations"; ValueType: string; ValueName: ".log"; ValueData: "{#ProgId}"
Root: HKA; Subkey: "Software\SuixinNotepad\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "{#MdProgId}"
Root: HKA; Subkey: "Software\SuixinNotepad\Capabilities\FileAssociations"; ValueType: string; ValueName: ".markdown"; ValueData: "{#MdProgId}"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "SuixinNotepad"; ValueData: "Software\SuixinNotepad\Capabilities"; Flags: uninsdeletevalue

[Run]
; 勾选“设为默认程序”时弹出系统的打开方式对话框，由用户确认；已是默认程序时程序直接退出。静默更新时不弹出
Filename: "{app}\{#AppExe}"; Parameters: "--choose-default-txt"; Tasks: defaulttxt; Flags: waituntilterminated; Check: not WizardSilent
; 静默更新时同样会执行，程序因此在更新后自动重新打开
Filename: "{app}\{#AppExe}"; Description: "运行{#AppName}"; Flags: nowait postinstall

[Code]
const
  GENERIC_WRITE = $40000000;
  OPEN_EXISTING = 3;
  INVALID_HANDLE_VALUE = -1;

function CreateFile(lpFileName: String; dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition,
  dwFlagsAndAttributes, hTemplateFile: Integer): Integer; external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(hObject: Integer): Boolean; external 'CloseHandle@kernel32.dll stdcall';

{ 程序文件仍被某个进程占用（程序还在运行或正在退出）时不能独占打开 }
function AppExeInUse(): Boolean;
var
  Path: String;
  Handle: Integer;
begin
  Result := False;
  Path := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Path) then Exit;
  Handle := CreateFile(Path, GENERIC_WRITE, 0, 0, OPEN_EXISTING, 0, 0);
  if Handle = INVALID_HANDLE_VALUE then
    Result := True
  else
    CloseHandle(Handle);
end;

{ 程序内更新时，旧程序启动安装程序后才退出，复制文件前等它完全退出（最多 30 秒）。
  手动安装时不等待，正在运行的程序由“自动关闭应用程序”处理 }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  I: Integer;
begin
  Result := '';
  if not WizardSilent then Exit;
  for I := 1 to 120 do
  begin
    if not CheckForMutexes('{#AppMutexName}') and not AppExeInUse() then Exit;
    Sleep(250);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('{#AppName}需要 .NET Framework 4.8。' + #13#10 +
      'Windows 10 1903 及以后的版本已经自带；其他系统请先从微软官网安装 .NET Framework 4.8，再运行本安装程序。',
      mbCriticalError, MB_OK, IDOK);
end;
