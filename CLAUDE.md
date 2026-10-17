# 随心记事本 项目说明

Windows 记事本的改进版，需求原文见 `readme.txt`，面向用户的说明见 `README.md`。每次修改代码后同步更新这两份文档中受影响的部分。

## 技术选型

- C# WinForms，目标框架 .NET Framework 4.8（Windows 10/11 自带，exe 约 160 KB）。用 SDK 风格项目编译，需要 .NET SDK 8+。
- `Directory.Build.props` 是版本号的唯一来源，并统一开启 `Nullable`、`TreatWarningsAsErrors`、`LangVersion latest`。
- 只依赖框架自带程序集；JSON 用 `DataContractJsonSerializer`（`Core/JsonFile.cs`）。不要引入第三方运行时依赖。
- 安装包用 Inno Setup 6.7.1（`installer/SuixinNotepad.iss`），由 `build.ps1` 从 nuget.org 下载到 `tools/` 并校验 SHA-256。

## 构建与测试

```powershell
.\build.ps1 [-Proxy http://127.0.0.1:7890] [-SkipTests]   # 测试 → 编译到 build\app → 安装包和 latest.json 到 dist\
dotnet test tests\SuixinNotepad.Tests                      # 只跑测试
```

本机访问 GitHub 需要代理 `http://127.0.0.1:7890`，nuget.org 不需要。编译前要先关闭正在运行的 SuixinNotepad.exe，否则 exe 被占用。

## 代码结构

- `Core/`：与界面无关、可单元测试的逻辑。
  - `TextEncoding`：支持的编码表（`All`、菜单分组 `Groups`），按引用比较，会话里存 `Name`。`SystemLegacy` 是系统默认的旧编码，简体中文系统用 GB18030 代替 GBK。
  - `EncodingDetector`：BOM → 含 NUL 时判断无 BOM 的 UTF-16，否则抛 `NotTextFileException` → 严格 UTF-8 → 旧编码。旧编码中能严格解码的候选按“非 ASCII 字符里该语言常用字的比例”打分，系统默认编码领先 0.2 以内时仍用默认编码。韩文按中文解码时汉字全部落在 GBK/Big5 的 B0–C8 区段，以此压低中文分数。调整打分后用 `EncodingDetectorTests` 的样本验证。
  - `TextFile`：解码（自动识别或指定编码，BOM 优先）、编码写出、换行符识别，`WriteAtomic` 先写临时文件再替换。`DecodedText.Lossless` 表示重新编码能得到与文件相同的字节。非 Unicode 编码写不下的字符抛 `UnrepresentableTextException`。
  - `BackupStore`：存档。每个文件一个目录，目录名为“文件名_完整路径 SHA256 前 16 位”，目录内 `source.txt` 记录原路径；存档文件名是保存时间。`Create` 遇到与最新存档相同的内容返回 false。`Cleanup` 按一年、50 份清理。`MoveHistory` 用于另存为和重命名。
  - `AppSettings` / `SessionState`：设置和会话，存于 `%LOCALAPPDATA%\SuixinNotepad`。新增字段的默认值写在 `[OnDeserializing]` 方法里，旧文件缺字段时才能拿到正确默认值。
  - `UpdateService`：读取 `latest.json`（网址或文件夹），比较版本，下载并校验 SHA-256。
  - `DefaultApp`：.txt 默认打开程序。Windows 不允许程序直接改默认程序，只能用 `SHOpenWithDialog` 弹出系统对话框由用户确认；对话框关闭时总返回成功，结果用 `AssocQueryString` 重新查询。安装包用 `--choose-default-txt` 参数调用它。
  - `SingleInstance`：互斥体加命名管道，第二个进程把文件路径转交给已运行的窗口。
- `UI/`：
  - `NoteEditor`：继承 RichTextBox，窗口类换成 RICHEDIT50W 纯文本模式。对外文本统一用 `\n` 换行，下标与控件一致；读写文本一律用 `GetText`/`SetText`/`ReplaceSelection`，不要用 `Text` 属性。富文本格式快捷键在 `ProcessCmdKey` 中屏蔽。中文字形通过字体关联处理：所选字体自带中文字形时跟随所选字体，否则用微软雅黑。
  - `MainForm.cs`（窗口、菜单、状态栏、快捷键）、`MainForm.Documents.cs`（标签页、载入、自动保存、手动保存与存档、另存为、关闭、会话）、`MainForm.Commands.cs`（编辑、查找、格式、查看、更新命令）。
  - `TabStrip`：自绘标签栏。`Dpi`：系统级 DPI 感知下的尺寸换算，界面上所有像素尺寸都要经过 `Dpi.Scale`。
  - 对话框继承 `DialogBase`。

## 关键行为约定

- 关闭程序（包括更新前退出）走 `MainForm.Shutdown()`：写回所有内容、保存会话，不询问用户。写不回原位置的内容另存为默认文件夹里的“恢复-…”文件。
- 只有“清空标签页”会关闭全部标签页。关闭标签页时，空白且未命名的笔记文件会被删除。
- 手动保存 = 写回文件 + 存档（自动备份开启时）+ 清理（自动清理开启时）。自动保存只写回文件。
- 恢复存档前先手动保存当前内容，恢复操作可撤消。
- 读写文件出错时，标签显示红色并保留内容，不静默丢弃。
- 解码有损（`NoteDocument.Lossy`）的文档编辑器只读，`WriteDocument` 遇到它直接抛异常；只能按编码重新打开或另存为。用户选定的编码记在 `EncodingForced`，载入、重新载入、存档预览和恢复都按它解码。
- 安装包总是注册打开方式、ProgId 和“默认应用”功能（`Software\SuixinNotepad\Capabilities`），默认打开程序只经用户在系统对话框中确认后改变。

## 规范

- 注释、界面文字、提交说明都用中文。界面只放用户操作需要的文字，不加解释性小字。
- `build.ps1` 和 `.iss` 含中文，必须保存为带 BOM 的 UTF-8（Windows PowerShell 5.1 和 ISCC 需要）。
- 修改代码后提交到本地 git，中文提交说明，不 push。
