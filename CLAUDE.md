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
  - `TextEncoding`：支持的编码表（`All`、菜单分组 `Groups`），按引用比较，会话里存 `Name`。`SystemLegacy` 是系统默认的旧编码，简体中文系统用 GB18030 代替 GBK。`Utf16LENoBom`/`Utf16BENoBom` 只用于自动识别出的无 BOM UTF-16 文件，在 `All` 中但不在菜单中，保存时不加 BOM。
  - `EncodingDetector`：BOM → 含 NUL 时判断无 BOM 的 UTF-16，否则抛 `NotTextFileException` → 严格 UTF-8 → 旧编码。无 BOM 的 UTF-16 返回 NoBom 编码。旧编码中能严格解码的候选按“非 ASCII 字符里该语言常用字的比例”打分，系统默认编码领先 0.2 以内时仍用默认编码。韩文按中文解码时汉字全部落在 GBK/Big5 的 B0–C8 区段，以此压低中文分数。打分对每段连续的非 ASCII 字符只计算一次长度，耗时与文件长度成正比，测试里有长单行的耗时上限。调整打分后用 `EncodingDetectorTests` 的样本验证。
  - `TextFile`：解码（自动识别或指定编码，BOM 优先）、编码写出、换行符识别。`ReadBytes` 允许其他程序同时写入，超过 `MaxFileSize`（64 MB）抛 `FileTooLargeException`。`DecodedText.Lossless` 表示按该编码重新编码能得到与文件相同的字节；混用多种换行符另由 `MixedLineEndings` 表示，保存时会统一。非 Unicode 编码写不下的字符抛 `UnrepresentableTextException`。
  - `TextFile.WriteAtomic`：符号链接先解析到目标文件；在同目录写短名临时文件（`~sx` 加 8 位十六进制）再 `File.Replace`。文件有多个硬链接、目录不允许新建文件、文件系统不支持替换（错误码 1、50）时改为原地覆盖。
  - `FilePath`：`Normalize`（完整路径并展开 8.3 短文件名）、`SameFile`（按卷序列号和文件编号判断同一文件）、`ResolveLinks`。查找已打开的文档一律用 `SameFile`，不要比较路径字符串。
  - `BackupStore`：存档。每个文件一个目录，目录名为“文件名_完整路径 SHA256 前 16 位”，目录内 `source.txt` 记录原路径；存档文件名是保存时间。`Create` 遇到与最新存档相同的内容返回 false。`Cleanup` 按一年、50 份清理，`keep` 指定的存档（正在恢复的那份）不删。`MoveHistory` 用于另存为和重命名。
  - `AppSettings` / `SessionState`：设置和会话，存于 `%LOCALAPPDATA%\SuixinNotepad`（经 `Environment.GetFolderPath` 取得，设置 `LOCALAPPDATA` 环境变量不能改变它，手工测试会用到真实数据）。新增字段的默认值写在 `[OnDeserializing]` 方法里，旧文件缺字段时才能拿到正确默认值；`[OnDeserialized]` 校验字段，null、空字体名、无效字号等抛 `SerializationException`，由 `JsonFile.LoadOrDefault` 按损坏处理（改名留档、用默认内容）。读文件本身出错（被占用、无权限）时 `Program` 提示后退出，不用默认内容覆盖。
  - `UpdateService`：读取 `latest.json`（https 网址或文件夹），比较版本，下载并校验 SHA-256。SHA-256 与安装包同源，所以不接受 http；文件夹清单中的安装包只能是同一文件夹中的文件名；四段版本号视为无效。所有失败都包装成 `UpdateException`，消息可直接展示。`ValidateSource` 供设置对话框校验地址。
  - `DefaultApp`：.txt 默认打开程序。Windows 不允许程序直接改默认程序，只能用 `SHOpenWithDialog` 弹出系统对话框由用户确认；对话框关闭时总返回成功，结果用 `AssocQueryString` 重新查询。安装包用 `--choose-default-txt` 参数调用它。
  - `SingleInstance`：互斥体加命名管道，第二个进程把文件路径转交给已运行的窗口。监听线程上的回调不能抛异常。`Program` 取得单实例后立即开始监听，`FileRelay` 在窗口 `Load` 之前把收到的文件存起来，窗口关闭后丢弃。
- `UI/`：
  - `NoteEditor`：继承 RichTextBox，窗口类换成 RICHEDIT50W 纯文本模式。对外文本统一用 `\n` 换行，下标与控件一致；读写文本一律用 `GetText`/`SetText`/`ReplaceSelection`，不要用 `Text` 属性。富文本格式快捷键在 `ProcessCmdKey` 中屏蔽。中文字形通过字体关联处理：所选字体自带中文字形时跟随所选字体，否则用微软雅黑。
  - `NoteEditor` 取文本：全文用 `EM_GETTEXTEX` 的 `GT_RAWTEXT`，范围和光标行列用 TOM（`EM_GETOLEINTERFACE` 取得 ITextDocument，经 `dynamic` 调用）。`GT_DEFAULT` 和 `EM_GETTEXTRANGE` 会把 U+FFFD 换成空格，不能用。光标在选区的活动端，行号是逻辑行，不受自动换行影响。
- `NoteEditor` 与输入法：RichEdit 收到 `EM_STREAMOUT` 时会强制结束组字，把未选字的拼音写进正文。RichTextBox 的 `SelectionLength`、`SelectedText`、`Text`、`Rtf` 都走 `EM_STREAMOUT`，所以 `SelectionLength` 已改写为 `EM_EXGETSEL`；`SelectedText`、`Text`、`Rtf` 不要使用。定时器（自动保存、状态栏）在组字过程中也会触发，新增的定时操作要用真实输入法确认不会打断组字。
  - 控件写入时会改写 U+0007、U+2028、U+2029、U+FFF9–U+FFFC、U+FFFE、U+FFFF（见 `Unsupported`），`CanHold` 判断文本能否原样保存。垂直制表符和 U+FFFD 能原样保存。粘贴统一走 `PastePlainText`（Ctrl+V、Shift+Insert、`WM_PASTE`、菜单），把 U+2028/U+2029 换成换行；Shift+Enter 插入普通换行。
  - `MainForm.cs`（窗口、菜单、状态栏、快捷键）、`MainForm.Documents.cs`（标签页、载入、自动保存、手动保存与存档、另存为、关闭、会话）、`MainForm.Commands.cs`（编辑、查找、格式、查看、更新命令）。
  - `TabStrip`：自绘标签栏。点击只发出 `TabClicked`，选中哪个标签由 `MainForm` 设置 `SelectedIndex` 决定，所以激活失败时高亮不会跑到别的标签上。弹出提示框可能吞掉 MouseUp，拖动状态在下一次按下和增删标签时清掉。`Dpi`：系统级 DPI 感知下的尺寸换算，界面上所有像素尺寸都要经过 `Dpi.Scale`。
  - 对话框继承 `DialogBase`。

## 关键行为约定

- 关闭程序（包括更新前退出）走 `MainForm.Shutdown()`：写回所有内容、保存会话，不询问用户。写不回原位置的内容另存为默认文件夹里的“恢复-…”文件；用户取消退出时也要告知哪些标签已改到恢复文件上。更新时安装程序启动成功后才设置 `shutdownDone`。
- 只有“清空标签页”会关闭全部标签页。关闭标签页时，空白且未命名的笔记文件会被删除。会话恢复时打不开的文件照样保留标签。
- `ActivateDocument` 读取失败时当前标签不变：刚打开的文件（`closeOnFailure`）直接关闭标签，其余情况询问是否关闭。`ActivateNearest` 从指定位置起找第一个能打开的标签，都打不开时新建空白页；启动时 `RestoreSession` 只建立标签，`OpenFiles` 之后仍没有活动标签才调用它。批量关闭（清空、关闭其他）先切到保留的标签或不激活，避免逐个读入将被关闭的文件。
- `WriteDocument` 写回前比较磁盘上的 `FileStamp` 与 `DiskStamp`：不同时，没有未写回的修改就重新载入，否则询问覆盖还是重新载入（与窗口激活时的检查共用 `KeepLocalAfterExternalChange`，询问期间不再写文件）。另存为前把 `DiskStamp` 设为目标文件当前状态（用户已确认覆盖）。`FileStamp` 都在读文件之前取得。
- 自动保存失败时每 10 秒重试（`retryTimer`）。
- 重命名：还没有文件的笔记在新位置建立文件；文件移动成功后，即使迁移存档历史失败也要改用新路径。
- 手动保存 = 写回文件 + 存档（自动备份开启时）+ 清理（自动清理开启时）。自动保存只写回文件。
- 恢复存档前先手动保存当前内容，恢复操作可撤消。
- 读写文件出错时，标签显示红色并保留内容，不静默丢弃。
- 不能原样写回的文档（`NoteDocument.LossReason`：`Undecodable` 解码有损，`UnsupportedChars` 含编辑器会改写的字符）编辑器只读，`WriteDocument` 遇到它直接抛异常；只能按编码重新打开（仅对 `Undecodable` 有用）或另存为。全部保存跳过它们。这类文档恢复存档不进入撤消记录，否则撤消会把有损文本放回可编辑的编辑器。用户选定的编码记在 `EncodingForced`，载入、重新载入、存档预览和恢复都按它解码。
- 安装包总是注册打开方式、ProgId 和“默认应用”功能（`Software\SuixinNotepad\Capabilities`），默认打开程序只经用户在系统对话框中确认后改变。

## 规范

- 注释、界面文字、提交说明都用中文。界面只放用户操作需要的文字，不加解释性小字。
- `build.ps1` 和 `.iss` 含中文，必须保存为带 BOM 的 UTF-8（Windows PowerShell 5.1 和 ISCC 需要）。
- 修改代码后提交到本地 git，中文提交说明，不 push。
- 项目以 MIT 许可证开源，远程仓库为 https://github.com/hit1190100321/SuixinNotepad（`origin`），只在用户要求时推送。
