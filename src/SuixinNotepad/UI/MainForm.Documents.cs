using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    /// <summary>
    /// 文档生命周期。核心约定：
    /// 1. 每个标签页都对应磁盘上一个确定的文件，编辑停顿 1.5 秒后自动写回，关闭时不询问；
    /// 2. 写回前确认文件没有被其他程序改过，改过时重新载入或由用户决定是否覆盖；
    /// 3. 手动保存（Ctrl+S、另存为、恢复存档前）额外在备份目录留一份存档并按规则清理；
    /// 4. 标签页列表保存在会话中，只有“清空标签页”才会清掉。
    /// </summary>
    internal sealed partial class MainForm
    {
        #region 创建与打开

        private const string OpenFilter = "文本文件 (*.txt;*.md;*.markdown;*.log)|*.txt;*.md;*.markdown;*.log|所有文件 (*.*)|*.*";
        private const string SaveFilter = "文本文档 (*.txt)|*.txt|Markdown 文档 (*.md)|*.md|所有文件 (*.*)|*.*";

        private static bool IsMarkdown(string fileName)
        {
            string ext = Path.GetExtension(fileName);
            return ext.Equals(".md", StringComparison.OrdinalIgnoreCase) || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 恢复上次的标签页，只建立标签不读文件。暂时打不开的文件（U 盘未插入、网络盘未连接）照样保留标签。
        /// 返回上次活动标签在当前列表中的位置。
        /// </summary>
        private int RestoreSession(SessionState session)
        {
            int preferred = 0;
            for (int i = 0; i < session.Tabs.Count; i++)
            {
                SessionTab tab = session.Tabs[i];
                if (string.IsNullOrWhiteSpace(tab.Path)) continue;
                string path;
                try
                {
                    path = FilePath.Normalize(tab.Path);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    continue;
                }
                if (FindDocument(path) != null) continue;

                var doc = new NoteDocument(path, tab.Untitled)
                {
                    PendingSelectionStart = tab.SelectionStart,
                    PendingSelectionLength = tab.SelectionLength,
                };
                doc.RestoreTitle(tab.Title);
                TextEncoding? forced = TextEncoding.FromName(tab.Encoding);
                if (forced != null)
                {
                    doc.Encoding = forced;
                    doc.EncodingForced = true;
                }
                if (i <= session.ActiveIndex) preferred = docs.Count;
                AddDocument(doc, docs.Count);
            }
            return preferred;
        }

        private NoteDocument? FindDocument(string path) => docs.FirstOrDefault(d => FilePath.SameFile(d.Path, path));

        private void AddDocument(NoteDocument doc, int index)
        {
            docs.Insert(index, doc);
            tabStrip.Insert(index, new TabStrip.Tab { Text = doc.Title, ToolTip = doc.Path });
        }

        private int InsertIndex => active == null ? docs.Count : docs.IndexOf(active) + 1;

        private void NewTab()
        {
            var doc = new NoteDocument(NewUntitledPath(), true);
            AddDocument(doc, InsertIndex);
            ActivateDocument(doc, closeOnFailure: true);
        }

        private string NewUntitledPath() =>
            UniqueNotePath("无标题-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), ".txt");

        /// <summary>默认文件夹中的新文件路径，与已有文件和已打开的标签都不冲突。</summary>
        private string UniqueNotePath(string stem, string extension)
        {
            string path = Path.GetFullPath(Path.Combine(settings.NotesFolder, stem + extension));
            for (int n = 2; File.Exists(path) || FindDocument(path) != null; n++)
                path = Path.GetFullPath(Path.Combine(settings.NotesFolder, stem + "-" + n.ToString(CultureInfo.InvariantCulture) + extension));
            return path;
        }

        private void OpenWithDialog()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = OpenFilter,
                Multiselect = true,
                Title = "打开",
            })
            {
                if (active != null && !active.Untitled) dialog.InitialDirectory = Path.GetDirectoryName(active.Path);
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenFiles(dialog.FileNames);
            }
        }

        private void OpenFiles(IEnumerable<string> files)
        {
            foreach (string raw in files)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string path;
                try
                {
                    path = FilePath.Normalize(raw);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    MessageBox.Show(this, "“" + raw + "”不是有效的文件路径：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    continue;
                }
                if (Directory.Exists(path)) continue;

                NoteDocument? existing = FindDocument(path);
                if (existing != null)
                {
                    ActivateDocument(existing);
                    continue;
                }

                if (!File.Exists(path))
                {
                    var answer = MessageBox.Show(this, "找不到文件“" + path + "”。\r\n\r\n是否创建新文件？", AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) continue;
                    try
                    {
                        TextFile.WriteAtomic(path, new byte[0]);
                    }
                    catch (Exception ex) when (IsFileError(ex))
                    {
                        MessageBox.Show(this, "无法创建文件“" + path + "”：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        continue;
                    }
                }

                var doc = new NoteDocument(path, false);
                AddDocument(doc, InsertIndex);
                if (!ActivateDocument(doc, closeOnFailure: true)) continue;
                if (doc.Lossy)
                    MessageBox.Show(this, LossyMessage(doc), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    ApplyLogStamp(doc);
            }
        }

        private static string ReadOnlyHint(NoteDocument doc) =>
            doc.LossReason == LossReason.Undecodable
                ? "此文件已只读打开，可换一种编码重新打开或另存为新文件"
                : "此文件已只读打开，可另存为新文件";

        private static string LossyMessage(NoteDocument doc)
        {
            switch (doc.LossReason)
            {
                case LossReason.Undecodable:
                    return "“" + doc.FileName + "”中有无法按 " + doc.Encoding.Name + " 识别的内容，已只读打开以免损坏原文件。\r\n\r\n" +
                           "可以点状态栏的编码，换一种编码重新打开，或者另存为新文件。";
                case LossReason.UnsupportedChars:
                    return "“" + doc.FileName + "”中有本程序无法原样保留的特殊字符（如 Unicode 行分隔符、段分隔符），已只读打开以免改变原文件。\r\n\r\n" +
                           "可以另存为新文件，这些字符会被换掉。";
                default:
                    throw new InvalidOperationException("文档没有只读原因：" + doc.Path);
            }
        }

        /// <summary>与 Windows 记事本相同：以 .LOG 开头的文件，打开时在末尾追加当前时间。</summary>
        private void ApplyLogStamp(NoteDocument doc)
        {
            NoteEditor editor = doc.Editor!;
            if (editor.GetTextRange(0, Math.Min(4, editor.TextLength)) != ".LOG") return;
            editor.Select(editor.TextLength, 0);
            editor.ReplaceSelection("\n" + DateTimeStamp() + "\n");
            editor.ScrollToCaret();
        }

        #endregion

        #region 激活与载入

        /// <summary>
        /// 切换到指定标签页，必要时创建编辑器并读入文件。读取失败时当前标签不变并返回 false：
        /// closeOnFailure 为 true（刚打开的文件）时直接关闭该标签，否则询问用户是否关闭。
        /// </summary>
        private bool ActivateDocument(NoteDocument doc, bool closeOnFailure = false)
        {
            if (active == doc)
            {
                doc.Editor?.Focus();
                return true;
            }

            try
            {
                EnsureLoaded(doc);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                string message = "无法打开“" + doc.Path + "”：" + ex.Message;
                if (closeOnFailure)
                {
                    MessageBox.Show(this, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RemoveDocument(doc, activateNext: false);
                }
                else if (MessageBox.Show(this, message + "\r\n\r\n是否关闭这个标签页？", AppName, MessageBoxButtons.YesNo,
                             MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    RemoveDocument(doc, activateNext: false);
                    SaveSession();
                }
                return false;
            }

            NoteEditor editor = doc.Editor!;
            if (active?.Editor != null) active.Editor.Visible = false;
            active = doc;
            ApplyViewSettings(editor);
            editor.Visible = true;
            tabStrip.SelectedIndex = docs.IndexOf(doc);
            UpdateWindowTitle();
            UpdateStatus();
            if (Visible) editor.Focus();
            SaveSession();
            return true;
        }

        /// <summary>从 index 起向后、再向前，激活第一个能打开的标签；都打不开时新建空白页。</summary>
        private void ActivateNearest(int index)
        {
            if (docs.Count > 0)
            {
                index = Math.Max(0, Math.Min(index, docs.Count - 1));
                NoteDocument[] order = docs.Skip(index).Concat(docs.Take(index).Reverse()).ToArray();
                foreach (NoteDocument doc in order)
                {
                    if (docs.Contains(doc) && ActivateDocument(doc)) return;
                }
            }
            NewTab();
        }

        private void EnsureLoaded(NoteDocument doc)
        {
            if (doc.Editor != null) return;

            DecodedText? content = null;
            FileStamp? stamp = null;
            if (File.Exists(doc.Path))
            {
                // 先记下文件状态再读：读取期间文件被改动时，下一次检查会发现并重新载入
                stamp = FileStamp.Of(doc.Path);
                content = TextFile.Load(doc.Path, doc.EncodingForced ? doc.Encoding : null);
            }
            else if (!doc.Untitled)
            {
                throw new FileNotFoundException("找不到文件。", doc.Path);
            }

            var editor = new NoteEditor
            {
                Dock = DockStyle.Fill,
                Visible = false,
                Font = editorFont,
                ContextMenuStrip = editorMenu,
            };
            editor.WrapLines = settings.WordWrap;
            editorHost.Controls.Add(editor);
            editor.CreateControl();
            IntPtr unused = editor.Handle;

            if (content != null)
            {
                editor.SetText(content.Text);
                ApplyDecoded(doc, editor, content);
                doc.DiskStamp = stamp;
            }

            int start = Math.Max(0, Math.Min(doc.PendingSelectionStart, editor.TextLength));
            int length = Math.Max(0, Math.Min(doc.PendingSelectionLength, editor.TextLength - start));
            editor.Select(start, length);
            editor.ScrollToCaret();

            editor.TextChanged += (s, e) => OnEditorTextChanged(doc);
            editor.SelectionChanged += (s, e) => { if (doc == active) RestartTimer(statusTimer); };
            editor.ZoomChangedByUser += (s, e) =>
            {
                settings.ZoomPercent = editor.ZoomPercent;
                UpdateStatus();
            };
            editor.DragEnter += OnFileDragEnter;
            editor.DragDrop += OnFileDragDrop;
            editor.KeyPress += (s, e) =>
            {
                // 只对会改动文字的按键提示；Ctrl+C 等组合键也会产生控制字符，不算
                bool edits = !char.IsControl(e.KeyChar) || e.KeyChar == '\b' || e.KeyChar == '\r' || e.KeyChar == '\t';
                if (doc.Lossy && edits) ShowMessage(ReadOnlyHint(doc));
            };

            doc.Editor = editor;
            if (doc.RefreshTitle()) UpdateTab(doc);
        }

        /// <summary>
        /// 记下解码结果。文件开头的 BOM 与用户选定的编码不同时，以 BOM 为准。
        /// 解码有损或含编辑器无法保存的字符时编辑器只读。
        /// </summary>
        private void ApplyDecoded(NoteDocument doc, NoteEditor editor, DecodedText content)
        {
            if (doc.EncodingForced && content.Encoding != doc.Encoding) doc.EncodingForced = false;
            doc.Encoding = content.Encoding;
            doc.LineEnding = content.LineEnding;
            doc.LossReason = !content.Lossless ? LossReason.Undecodable
                : !NoteEditor.CanHold(content.Text) ? LossReason.UnsupportedChars
                : LossReason.None;
            editor.ReadOnly = doc.Lossy;
            if (content.MixedLineEndings && !doc.Lossy)
                ShowMessage("“" + doc.Title + "”中混用了多种换行符，修改后保存时会统一为 " + TextFile.DisplayName(doc.LineEnding));
        }

        private void ApplyViewSettings(NoteEditor editor)
        {
            if (!editor.Font.Equals(editorFont)) editor.Font = editorFont;
            if (editor.WrapLines != settings.WordWrap) editor.WrapLines = settings.WordWrap;
            if (editor.ZoomPercent != settings.ZoomPercent) editor.ZoomPercent = settings.ZoomPercent;
        }

        private void OnEditorTextChanged(NoteDocument doc)
        {
            if (suppressTextEvents) return;
            doc.Dirty = true;
            RestartTimer(autosaveTimer);
            if (doc == active) RestartTimer(statusTimer);
        }

        private static void RestartTimer(Timer timer)
        {
            timer.Stop();
            timer.Start();
        }

        private void SwitchTab(int delta)
        {
            if (docs.Count < 2 || active == null) return;
            int index = (docs.IndexOf(active) + delta + docs.Count) % docs.Count;
            ActivateDocument(docs[index]);
        }

        private void MoveDocument(int from, int to)
        {
            NoteDocument doc = docs[from];
            docs.RemoveAt(from);
            docs.Insert(to, doc);
            SaveSession();
        }

        private void UpdateTab(NoteDocument doc)
        {
            int index = docs.IndexOf(doc);
            if (index < 0) return;
            TabStrip.Tab tab = tabStrip[index];
            tab.Text = doc.Title;
            tab.ToolTip = doc.SaveError == null ? doc.Path : doc.Path + "\r\n保存失败：" + doc.SaveError;
            tab.Error = doc.SaveError != null;
            tabStrip.RefreshTabs();
            if (doc == active) UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            Text = active == null ? AppName : active.Title + " - " + AppName;
        }

        #endregion

        #region 保存

        private void AutosaveTick()
        {
            autosaveTimer.Stop();
            retryTimer.Stop();
            // 正在询问外部修改时不写文件，等用户回答后再保存
            if (askingExternalChange)
            {
                RestartTimer(autosaveTimer);
                return;
            }
            // 文件被短暂锁住（杀毒软件、同步盘）时稍后重试，不等用户下一次编辑
            if (SaveAllDirty().Count > 0) retryTimer.Start();
            if (active != null && active.Untitled && active.RefreshTitle()) UpdateTab(active);
            SaveSession();
        }

        /// <summary>把所有未写回的修改写入文件。返回写入失败的文档。</summary>
        private List<NoteDocument> SaveAllDirty()
        {
            var failed = new List<NoteDocument>();
            foreach (NoteDocument doc in docs.ToArray())
            {
                if (doc.Dirty && WriteDocument(doc) == null) failed.Add(doc);
            }
            return failed;
        }

        /// <summary>
        /// 把编辑器内容写入文件，返回文件中现在的字节（备份使用）；失败返回 null，原因记在 SaveError 并把标签标红。
        /// 文件被其他程序改过时：没有未保存的修改就重新载入，否则询问用户覆盖还是重新载入，
        /// 重新载入时返回从磁盘读到的字节。未指定位置、内容为空且文件尚不存在的记事本不创建文件。
        /// </summary>
        private byte[]? WriteDocument(NoteDocument doc, bool force = false)
        {
            NoteEditor editor = doc.Editor ?? throw new InvalidOperationException("文档尚未载入：" + doc.Path);
            if (doc.Lossy) throw new InvalidOperationException("有损解码的文档不能写回：" + doc.Path);

            FileStamp? onDisk = FileStamp.Of(doc.Path);
            if (onDisk != null && !onDisk.Equals(doc.DiskStamp))
            {
                if (askingExternalChange)
                    return Fail(doc, "文件已被其他程序修改，等待确认是否覆盖");
                if (!KeepLocalAfterExternalChange(doc, onDisk.Value, out byte[]? reloaded))
                    return reloaded ?? Fail(doc, "文件已被其他程序修改，且无法重新载入");
            }

            string text = editor.GetText();
            if (!force && doc.Untitled && text.Length == 0 && onDisk == null)
            {
                doc.Dirty = false;
                return new byte[0];
            }

            byte[] bytes;
            try
            {
                bytes = TextFile.Encode(text, doc.Encoding, doc.LineEnding);
            }
            catch (UnrepresentableTextException)
            {
                ShowMessage("“" + doc.Title + "”包含 " + doc.Encoding.Name + " 无法表示的字符，已改用 UTF-8 保存");
                doc.Encoding = TextEncoding.Utf8;
                bytes = TextFile.Encode(text, doc.Encoding, doc.LineEnding);
                if (doc == active) UpdateStatus();
            }

            try
            {
                if (doc.Untitled) Directory.CreateDirectory(Path.GetDirectoryName(doc.Path)!);
                TextFile.WriteAtomic(doc.Path, bytes);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Fail(doc, ex.Message);
                ShowMessage("无法保存“" + doc.Title + "”：" + ex.Message);
                return null;
            }

            doc.DiskStamp = FileStamp.Of(doc.Path);
            doc.Dirty = false;
            if (doc.SaveError != null)
            {
                doc.SaveError = null;
                UpdateTab(doc);
            }
            return bytes;
        }

        private byte[]? Fail(NoteDocument doc, string error)
        {
            doc.SaveError = error;
            UpdateTab(doc);
            return null;
        }

        /// <summary>手动保存：写入文件，再按设置备份并清理。keepBackup 指定的存档不会被清理掉。返回文件是否写入成功。</summary>
        private bool SaveManually(NoteDocument doc, string? keepBackup = null)
        {
            if (doc.Lossy)
            {
                MessageBox.Show(this, LossyMessage(doc), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            byte[]? bytes = WriteDocument(doc, force: true);
            if (bytes == null)
            {
                MessageBox.Show(this, "无法保存“" + doc.Path + "”：\r\n" + doc.SaveError, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            ReportArchive(doc, bytes, keepBackup);
            return true;
        }

        /// <summary>按设置给刚写入的内容存档并清理。返回存档失败的原因；成功或没有开启存档时返回 null。</summary>
        private string? Archive(NoteDocument doc, byte[] bytes, string? keepBackup, out bool created)
        {
            created = false;
            if (!settings.AutoBackup) return null;
            try
            {
                created = backups.Create(doc.Path, bytes);
                if (settings.EffectiveAutoCleanup) backups.Cleanup(doc.Path, keepBackup);
                return null;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return ex.Message;
            }
        }

        private void ReportArchive(NoteDocument doc, byte[] bytes, string? keepBackup = null)
        {
            string? error = Archive(doc, bytes, keepBackup, out bool created);
            if (error != null)
                MessageBox.Show(this, "文件已保存，但存档失败：" + error, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                ShowMessage(created ? "已保存并存档" : "已保存");
        }

        private void ManualSave()
        {
            if (active == null) return;
            SaveManually(active);
            SaveSession();
        }

        /// <summary>保存并存档所有已载入的标签。只读的标签跳过，某个文件失败不影响其余文件，最后一并报告。</summary>
        private void SaveAll()
        {
            var problems = new List<string>();
            foreach (NoteDocument doc in docs.ToArray())
            {
                if (!doc.IsLoaded || doc.Lossy || !docs.Contains(doc)) continue;
                byte[]? bytes = WriteDocument(doc, force: true);
                if (bytes == null)
                {
                    problems.Add("无法保存“" + doc.Path + "”：" + doc.SaveError);
                    continue;
                }
                string? error = Archive(doc, bytes, null, out _);
                if (error != null) problems.Add("“" + doc.Path + "”已保存，但存档失败：" + error);
            }
            SaveSession();
            if (problems.Count > 0)
                MessageBox.Show(this, string.Join("\r\n", problems), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                ShowMessage("已全部保存");
        }

        private void SaveAs()
        {
            NoteDocument? doc = active;
            if (doc == null) return;

            // 文件类型默认跟随当前文件：Markdown 文件另存时仍是 Markdown
            string fileName = doc.Untitled ? SafeFileName(doc.Title) + ".txt" : doc.FileName;
            bool markdown = IsMarkdown(fileName);
            string target;
            using (var dialog = new SaveFileDialog
            {
                Filter = SaveFilter,
                FilterIndex = markdown ? 2 : 1,
                DefaultExt = markdown ? "md" : "txt",
                AddExtension = true,
                OverwritePrompt = true,
                Title = "另存为",
                FileName = fileName,
                InitialDirectory = doc.Untitled ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(doc.Path),
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                target = FilePath.Normalize(dialog.FileName);
            }

            if (FilePath.SameFile(target, doc.Path))
            {
                ManualSave();
                return;
            }
            if (FindDocument(target) != null)
            {
                MessageBox.Show(this, "“" + target + "”已在另一个标签页中打开。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string oldPath = doc.Path;
            bool wasUntitled = doc.Untitled;
            LossReason oldReason = doc.LossReason;
            FileStamp? oldStamp = doc.DiskStamp;
            doc.Path = target;
            doc.Untitled = false;
            doc.LossReason = LossReason.None;
            // 用户已在对话框中确认覆盖目标文件，不再当作外部修改
            doc.DiskStamp = FileStamp.Of(target);
            byte[]? bytes = WriteDocument(doc, force: true);
            if (bytes == null)
            {
                string error = doc.SaveError ?? "";
                doc.Path = oldPath;
                doc.Untitled = wasUntitled;
                doc.LossReason = oldReason;
                doc.DiskStamp = oldStamp;
                doc.SaveError = null;
                UpdateTab(doc);
                MessageBox.Show(this, "无法保存到“" + target + "”：\r\n" + error, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 未指定位置的记事本被移到用户指定的位置：原文件删除，存档历史随之迁移
            if (wasUntitled)
            {
                try
                {
                    backups.MoveHistory(oldPath, target);
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    MessageBox.Show(this, "已另存为“" + target + "”，但整理原文件时出错：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            doc.Editor!.ReadOnly = false;
            ReportArchive(doc, bytes);
            doc.RefreshTitle();
            UpdateTab(doc);
            if (doc == active) UpdateStatus();
            SaveSession();
        }

        private void Rename(NoteDocument? doc)
        {
            if (doc == null) return;
            string dir = Path.GetDirectoryName(doc.Path)!;
            string current = doc.Untitled ? SafeFileName(doc.Title) + ".txt" : doc.FileName;

            string? name = InputDialog.Ask(this, "重命名", "新文件名:", current, "确定", input =>
            {
                string n = input.Trim();
                if (n.Length == 0) return "请输入文件名。";
                if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "文件名不能包含下列任何字符：\\ / : * ? \" < > |";
                string p = Path.Combine(dir, n);
                bool samePath = string.Equals(p, doc.Path, StringComparison.OrdinalIgnoreCase);
                if (!samePath && (File.Exists(p) || Directory.Exists(p) || FindDocument(p) != null)) return "已存在同名文件。";
                return null;
            }, Path.GetFileNameWithoutExtension(current).Length);
            if (name == null) return;

            // 只改大小写也是重命名，所以按区分大小写比较
            string target = Path.GetFullPath(Path.Combine(dir, name.Trim()));
            if (target == doc.Path) return;
            if (doc.Dirty && WriteDocument(doc) == null)
            {
                MessageBox.Show(this, "无法保存“" + doc.Path + "”：" + doc.SaveError, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string oldPath = doc.Path;
            try
            {
                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, target);
                }
                else
                {
                    // 还没写入过的空白记事本：在新位置建立文件，标签才有确定的文件可以恢复
                    Directory.CreateDirectory(dir);
                    TextFile.WriteAtomic(target, TextFile.Encode(doc.Editor?.GetText() ?? "", doc.Encoding, doc.LineEnding));
                }
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                MessageBox.Show(this, "无法重命名：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 文件已经在新位置，之后无论存档能否迁移，都必须写到新位置，否则旧文件会被重新写出来
            doc.Path = target;
            doc.Untitled = false;
            doc.DiskStamp = FileStamp.Of(target);
            doc.RefreshTitle();
            UpdateTab(doc);
            SaveSession();

            try
            {
                backups.MoveHistory(oldPath, target);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                MessageBox.Show(this, "文件已重命名，但迁移存档历史时出错：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static string SafeFileName(string title)
        {
            var sb = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in title.TrimEnd('…'))
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string name = sb.ToString().Trim().TrimEnd('.');
            return name.Length == 0 ? NoteDocument.UntitledTitle : name;
        }

        #endregion

        #region 关闭

        private void CloseTab(int index)
        {
            if (index < 0 || index >= docs.Count) return;
            NoteDocument doc = docs[index];
            if (doc.Dirty && WriteDocument(doc) == null)
            {
                var answer = MessageBox.Show(this, "无法保存“" + doc.Path + "”：" + doc.SaveError + "\r\n\r\n仍要关闭吗？未保存的修改将丢失。",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }
            if (!docs.Contains(doc)) return;
            DeleteIfEmptyUntitled(doc);
            RemoveDocument(doc);
            SaveSession();
        }

        /// <summary>关闭标签右键菜单所指标签以外的部分标签。先切到保留的标签，关闭时不再逐个激活将被关闭的标签。</summary>
        private void CloseTabsWhere(Func<int, bool> predicate)
        {
            NoteDocument? keep = TabMenuDocument;
            NoteDocument[] targets = docs.Where((d, i) => predicate(i)).ToArray();
            if (keep != null && active != null && targets.Contains(active) && !ActivateDocument(keep)) return;
            foreach (NoteDocument doc in targets)
            {
                int index = docs.IndexOf(doc);
                if (index >= 0) CloseTab(index);
            }
        }

        /// <summary>清空所有标签页（文件都已保存在各自的位置），然后打开一个新的空白页。</summary>
        private void ClearAllTabs()
        {
            List<NoteDocument> failed = SaveAllDirty();
            if (failed.Count > 0)
            {
                var answer = MessageBox.Show(this, "以下文件无法保存：\r\n" + string.Join("\r\n", failed.Select(d => d.Path)) +
                    "\r\n\r\n仍要清空吗？未保存的修改将丢失。", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }

            foreach (NoteDocument doc in docs.ToArray())
            {
                DeleteIfEmptyUntitled(doc);
                RemoveDocument(doc, activateNext: false);
            }
            NewTab();
            SaveSession();
        }

        /// <summary>移除标签与编辑器（不保存）。移除的是当前标签且 activateNext 为 true 时激活相邻标签，没有可用标签时新建空白页。</summary>
        private void RemoveDocument(NoteDocument doc, bool activateNext = true)
        {
            int index = docs.IndexOf(doc);
            if (index < 0) return;
            docs.RemoveAt(index);
            tabStrip.RemoveAt(index);
            if (doc.Editor != null)
            {
                editorHost.Controls.Remove(doc.Editor);
                doc.Editor.Dispose();
                doc.Editor = null;
            }

            if (active != doc) return;
            active = null;
            if (activateNext) ActivateNearest(index);
            else UpdateWindowTitle();
        }

        /// <summary>空白的未命名记事本在关闭时删除其文件，避免默认文件夹里堆积空文件。</summary>
        private static void DeleteIfEmptyUntitled(NoteDocument doc)
        {
            if (!doc.Untitled || !File.Exists(doc.Path)) return;
            bool empty = doc.Editor != null ? doc.Editor.TextLength == 0 : new FileInfo(doc.Path).Length == 0;
            if (!empty) return;
            try
            {
                File.Delete(doc.Path);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Trace.WriteLine("删除空白记事本失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 退出前写回所有修改。写不回原位置的内容另存到默认文件夹作为新的未命名记事本，
        /// 保证退出不丢内容；连默认文件夹也写不进时由用户决定是否放弃退出。
        /// 无论是否退出，都要告诉用户哪些标签已经改到了另存的文件上。
        /// </summary>
        private bool Shutdown()
        {
            autosaveTimer.Stop();
            retryTimer.Stop();
            var rescued = new List<string>();
            var lost = new List<string>();
            foreach (NoteDocument doc in SaveAllDirty())
            {
                string original = doc.Path;
                bool wasUntitled = doc.Untitled;
                FileStamp? oldStamp = doc.DiskStamp;
                doc.Path = UniqueNotePath("恢复-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                    Path.GetFileNameWithoutExtension(original), Path.GetExtension(original));
                doc.Untitled = true;
                doc.DiskStamp = null;
                if (WriteDocument(doc, force: true) != null)
                {
                    rescued.Add(original + "\r\n    → " + doc.Path);
                    doc.RefreshTitle();
                    UpdateTab(doc);
                }
                else
                {
                    doc.Path = original;
                    doc.Untitled = wasUntitled;
                    doc.DiskStamp = oldStamp;
                    lost.Add(original);
                }
            }

            string rescuedText = rescued.Count == 0 ? "" :
                "以下文件无法保存到原位置，内容已另存：\r\n" + string.Join("\r\n", rescued) + "\r\n\r\n";
            if (lost.Count > 0)
            {
                var answer = MessageBox.Show(this, rescuedText + "以下文件无法保存：\r\n" + string.Join("\r\n", lost) +
                    "\r\n\r\n仍要退出吗？未保存的修改将丢失。", AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    SaveSession();
                    retryTimer.Start();
                    return false;
                }
            }
            else if (rescued.Count > 0)
            {
                MessageBox.Show(this, rescuedText.TrimEnd(), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            SaveSession();
            RememberWindowBounds();
            SaveSettings();
            return true;
        }

        #endregion

        #region 存档历史

        private void ShowHistory(NoteDocument? doc)
        {
            if (doc == null) return;
            BackupEntry? entry;
            using (var dialog = new HistoryDialog(backups, doc.Path, doc.EncodingForced ? doc.Encoding : null, doc.Title, editorFont))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                entry = dialog.Selected;
            }
            if (entry != null && docs.Contains(doc)) RestoreBackup(doc, entry);
        }

        /// <summary>
        /// 恢复存档：先对当前内容执行一次手动保存，再把存档内容写回。
        /// 普通文档的恢复可撤消；只读文档的原内容不能回到编辑器里，恢复不进入撤消记录。
        /// </summary>
        private void RestoreBackup(NoteDocument doc, BackupEntry entry)
        {
            if (!ActivateDocument(doc)) return;
            DecodedText decoded;
            try
            {
                decoded = TextFile.Decode(backups.Read(entry), doc.EncodingForced ? doc.Encoding : null);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                MessageBox.Show(this, "无法读取存档：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!decoded.Lossless)
            {
                MessageBox.Show(this, "这份存档中有无法按 " + decoded.Encoding.Name + " 识别的内容，不能恢复。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!NoteEditor.CanHold(decoded.Text))
            {
                MessageBox.Show(this, "这份存档中有本程序无法原样保留的特殊字符，不能恢复。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool wasLossy = doc.Lossy;
            // 恢复前的保存会触发清理，要恢复的这份存档不能被清理掉
            if (!(wasLossy ? ArchiveRawFile(doc) : SaveManually(doc, entry.BackupPath))) return;

            NoteEditor editor = doc.Editor!;
            ApplyDecoded(doc, editor, decoded);
            if (wasLossy)
            {
                editor.SetText(decoded.Text);
                doc.Dirty = true;
            }
            else
            {
                editor.ReplaceAllUndoable(decoded.Text);
            }
            editor.Select(0, 0);
            editor.ScrollToCaret();
            if (WriteDocument(doc) == null)
            {
                MessageBox.Show(this, "无法保存“" + doc.Path + "”：" + doc.SaveError, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            doc.RefreshTitle();
            UpdateTab(doc);
            UpdateStatus();
            ShowMessage("已恢复到 " + entry.SavedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " 的存档");
        }

        /// <summary>只读文档无法按文本保存，恢复存档前把磁盘上的原始字节原样存档。</summary>
        private bool ArchiveRawFile(NoteDocument doc)
        {
            try
            {
                if (File.Exists(doc.Path)) backups.Create(doc.Path, TextFile.ReadBytes(doc.Path));
                return true;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                MessageBox.Show(this, "无法存档当前文件，已取消恢复：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        #endregion

        #region 外部修改

        /// <summary>窗口激活时检查已载入的文件是否被其他程序修改。</summary>
        private void CheckExternalChanges()
        {
            if (askingExternalChange) return;
            foreach (NoteDocument doc in docs.ToArray())
            {
                if (!doc.IsLoaded || doc.DiskStamp == null || !docs.Contains(doc)) continue;
                FileStamp? now = FileStamp.Of(doc.Path);
                if (now == null)
                {
                    // 文件被外部删除：保留内容，下一次自动保存时重新写出；只读文档的内容不能写出
                    if (doc.Lossy) continue;
                    doc.DiskStamp = null;
                    doc.Dirty = true;
                    RestartTimer(autosaveTimer);
                    continue;
                }
                if (now.Value.Equals(doc.DiskStamp.Value)) continue;
                if (KeepLocalAfterExternalChange(doc, now.Value, out _)) RestartTimer(autosaveTimer);
            }
        }

        /// <summary>
        /// 文件被其他程序改过：编辑器里没有未保存的修改时直接重新载入，否则询问用户。
        /// 返回 true 表示保留编辑器中的内容，之后写回会覆盖磁盘上的文件；
        /// 返回 false 表示已尝试重新载入，reloaded 是读到的字节，读取失败时为 null。
        /// </summary>
        private bool KeepLocalAfterExternalChange(NoteDocument doc, FileStamp onDisk, out byte[]? reloaded)
        {
            reloaded = null;
            if (doc.Dirty)
            {
                DialogResult answer;
                askingExternalChange = true;
                try
                {
                    answer = MessageBox.Show(this, "“" + doc.Path + "”已被其他程序修改。\r\n\r\n是否重新载入？选择“否”将保留当前内容并覆盖该文件。",
                        AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                }
                finally
                {
                    askingExternalChange = false;
                }
                if (answer != DialogResult.Yes)
                {
                    doc.DiskStamp = onDisk;
                    return true;
                }
            }
            reloaded = Reload(doc);
            return false;
        }

        /// <summary>重新读取磁盘上的文件，返回读到的字节；读取失败时保留编辑器内容并返回 null。</summary>
        private byte[]? Reload(NoteDocument doc)
        {
            FileStamp? stamp = FileStamp.Of(doc.Path);
            byte[] bytes;
            DecodedText content;
            try
            {
                bytes = TextFile.ReadBytes(doc.Path);
                content = TextFile.Decode(bytes, doc.EncodingForced ? doc.Encoding : null);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                ShowMessage("无法重新载入“" + doc.Title + "”：" + ex.Message);
                return null;
            }
            ShowDecoded(doc, content, stamp);
            ShowMessage(doc.Lossy
                ? "“" + doc.Title + "”被其他程序修改，新内容无法原样编辑，已只读打开"
                : "“" + doc.Title + "”被其他程序修改，已重新载入");
            return bytes;
        }

        /// <summary>用新的解码结果替换编辑器内容（不进入撤消记录，不触发自动保存）。stamp 是读取前的文件状态。</summary>
        private void ShowDecoded(NoteDocument doc, DecodedText content, FileStamp? stamp)
        {
            NoteEditor editor = doc.Editor!;

            int start = editor.SelectionStart;
            suppressTextEvents = true;
            try
            {
                editor.SetText(content.Text);
            }
            finally
            {
                suppressTextEvents = false;
            }
            editor.Select(Math.Min(start, editor.TextLength), 0);
            editor.ScrollToCaret();
            ApplyDecoded(doc, editor, content);
            doc.DiskStamp = stamp;
            doc.Dirty = false;
            if (doc.SaveError != null)
            {
                doc.SaveError = null;
                UpdateTab(doc);
            }
            if (doc.RefreshTitle()) UpdateTab(doc);
            if (doc == active) UpdateStatus();
        }

        #endregion

        #region 会话与设置

        private void SaveSession()
        {
            var state = new SessionState { ActiveIndex = active == null ? 0 : Math.Max(0, docs.IndexOf(active)) };
            foreach (NoteDocument doc in docs)
            {
                var tab = new SessionTab
                {
                    Path = doc.Path,
                    Untitled = doc.Untitled,
                    Title = doc.Title,
                    Encoding = doc.EncodingForced ? doc.Encoding.Name : "",
                };
                if (doc.Editor != null)
                {
                    tab.SelectionStart = doc.Editor.SelectionStart;
                    tab.SelectionLength = doc.Editor.SelectionLength;
                }
                else
                {
                    tab.SelectionStart = doc.PendingSelectionStart;
                    tab.SelectionLength = doc.PendingSelectionLength;
                }
                state.Tabs.Add(tab);
            }

            byte[] json = JsonFile.Serialize(state);
            string text = Encoding.UTF8.GetString(json);
            if (text == lastSessionJson) return;
            try
            {
                Directory.CreateDirectory(paths.DataRoot);
                TextFile.WriteAtomic(paths.SessionFile, json);
                lastSessionJson = text;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                ShowMessage("无法保存标签页记录：" + ex.Message);
            }
        }

        private void SaveSettings()
        {
            try
            {
                JsonFile.Save(paths.SettingsFile, settings);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                ShowMessage("无法保存设置：" + ex.Message);
            }
        }

        #endregion

        private static bool IsFileError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
            ex is NotSupportedException || ex is ArgumentException;
    }
}
