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
    /// 2. 手动保存（Ctrl+S、另存为、恢复存档前）额外在备份目录留一份存档并按规则清理；
    /// 3. 标签页列表保存在会话中，只有“清空标签页”才会清掉。
    /// </summary>
    internal sealed partial class MainForm
    {
        #region 创建与打开

        private const string OpenFilter = "文本文件 (*.txt;*.md)|*.txt;*.md;*.markdown;*.log|所有文件 (*.*)|*.*";
        private const string SaveFilter = "文本文档 (*.txt)|*.txt|Markdown 文档 (*.md)|*.md|所有文件 (*.*)|*.*";

        private static bool IsMarkdown(string fileName)
        {
            string ext = Path.GetExtension(fileName);
            return ext.Equals(".md", StringComparison.OrdinalIgnoreCase) || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
        }

        private void RestoreSession(SessionState session)
        {
            foreach (SessionTab tab in session.Tabs)
            {
                if (string.IsNullOrWhiteSpace(tab.Path)) continue;
                string path;
                try
                {
                    path = Path.GetFullPath(tab.Path);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    continue;
                }
                // 用户指定位置的文件已被删除或移走时不再恢复；未指定位置的空白记事本可能本来就还没有文件
                if (!tab.Untitled && !File.Exists(path)) continue;
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
                AddDocument(doc, docs.Count);
            }

            if (docs.Count > 0)
            {
                int index = Math.Max(0, Math.Min(session.ActiveIndex, docs.Count - 1));
                ActivateDocument(docs[index]);
            }
        }

        private NoteDocument? FindDocument(string path) =>
            docs.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));

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
            ActivateDocument(doc);
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
                string path = Path.GetFullPath(raw);
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
                if (!ActivateDocument(doc)) continue;
                if (doc.Lossy)
                    MessageBox.Show(this, LossyMessage(doc), AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                    ApplyLogStamp(doc);
            }
        }

        private const string ReadOnlyHint = "此文件已只读打开，可换一种编码重新打开或另存为新文件";

        private static string LossyMessage(NoteDocument doc) =>
            "“" + doc.FileName + "”中有无法按 " + doc.Encoding.Name + " 识别的内容，已只读打开以免损坏原文件。\r\n\r\n" +
            "可以点状态栏的编码，换一种编码重新打开，或者另存为新文件。";

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

        /// <summary>切换到指定标签页，必要时创建编辑器并读入文件。读取失败时关闭该标签并返回 false。</summary>
        private bool ActivateDocument(NoteDocument doc)
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
                MessageBox.Show(this, "无法打开“" + doc.Path + "”：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RemoveDocument(doc);
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

        private void EnsureLoaded(NoteDocument doc)
        {
            if (doc.Editor != null) return;

            DecodedText? content = File.Exists(doc.Path) ? TextFile.Load(doc.Path, doc.EncodingForced ? doc.Encoding : null) : null;

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
                doc.DiskStamp = FileStamp.Of(doc.Path);
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
            editor.KeyPress += (s, e) => { if (doc.Lossy) ShowMessage(ReadOnlyHint); };

            doc.Editor = editor;
            if (doc.RefreshTitle()) UpdateTab(doc);
        }

        /// <summary>记下解码结果。文件开头的 BOM 与用户选定的编码不同时，以 BOM 为准。</summary>
        private static void ApplyDecoded(NoteDocument doc, NoteEditor editor, DecodedText content)
        {
            if (doc.EncodingForced && content.Encoding != doc.Encoding) doc.EncodingForced = false;
            doc.Encoding = content.Encoding;
            doc.LineEnding = content.LineEnding;
            doc.Lossy = !content.Lossless;
            editor.ReadOnly = doc.Lossy;
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
            SaveAllDirty();
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
        /// 把编辑器内容写入文件，返回写入的字节（备份使用）；失败返回 null 并在标签上标红。
        /// 未指定位置、内容为空且文件尚不存在的记事本不创建文件。
        /// </summary>
        private byte[]? WriteDocument(NoteDocument doc, bool force = false)
        {
            if (doc.Editor == null) return null;
            if (doc.Lossy) throw new InvalidOperationException("有损解码的文档不能写回：" + doc.Path);
            string text = doc.Editor.GetText();
            if (!force && doc.Untitled && text.Length == 0 && !File.Exists(doc.Path))
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
                doc.SaveError = ex.Message;
                UpdateTab(doc);
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

        /// <summary>手动保存：写入文件，再按设置备份并清理。返回文件是否写入成功。</summary>
        private bool SaveManually(NoteDocument doc)
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
            BackupAfterManualSave(doc, bytes);
            return true;
        }

        private void BackupAfterManualSave(NoteDocument doc, byte[] bytes)
        {
            if (!settings.AutoBackup)
            {
                ShowMessage("已保存");
                return;
            }
            try
            {
                bool created = backups.Create(doc.Path, bytes);
                if (settings.EffectiveAutoCleanup) backups.Cleanup(doc.Path);
                ShowMessage(created ? "已保存并存档" : "已保存");
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                MessageBox.Show(this, "文件已保存，但存档失败：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ManualSave()
        {
            if (active == null) return;
            SaveManually(active);
            SaveSession();
        }

        private void SaveAll()
        {
            foreach (NoteDocument doc in docs.ToArray())
            {
                if (doc.IsLoaded && !SaveManually(doc)) break;
            }
            SaveSession();
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
                target = Path.GetFullPath(dialog.FileName);
            }

            if (string.Equals(target, doc.Path, StringComparison.OrdinalIgnoreCase))
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
            bool wasLossy = doc.Lossy;
            doc.Path = target;
            doc.Untitled = false;
            doc.Lossy = false;
            byte[]? bytes = WriteDocument(doc, force: true);
            if (bytes == null)
            {
                string error = doc.SaveError ?? "";
                doc.Path = oldPath;
                doc.Untitled = wasUntitled;
                doc.Lossy = wasLossy;
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
            BackupAfterManualSave(doc, bytes);
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

            string target = Path.GetFullPath(Path.Combine(dir, name.Trim()));
            if (target == doc.Path) return;
            if (doc.Dirty && WriteDocument(doc) == null)
            {
                MessageBox.Show(this, "无法保存“" + doc.Path + "”：" + doc.SaveError, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                if (File.Exists(doc.Path)) File.Move(doc.Path, target);
                backups.MoveHistory(doc.Path, target);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                MessageBox.Show(this, "无法重命名：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            doc.Path = target;
            doc.Untitled = false;
            doc.DiskStamp = FileStamp.Of(target);
            doc.RefreshTitle();
            UpdateTab(doc);
            SaveSession();
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
            DeleteIfEmptyUntitled(doc);
            RemoveDocument(doc);
            SaveSession();
        }

        private void CloseTabsWhere(Func<int, bool> predicate)
        {
            NoteDocument[] targets = docs.Where((d, i) => predicate(i)).ToArray();
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
                RemoveDocument(doc, keepOneTab: false);
            }
            NewTab();
            SaveSession();
        }

        /// <summary>移除标签与编辑器（不保存）。没有标签时新建一个空白页。</summary>
        private void RemoveDocument(NoteDocument doc, bool keepOneTab = true)
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

            if (active == doc)
            {
                active = null;
                if (docs.Count > 0) ActivateDocument(docs[Math.Min(index, docs.Count - 1)]);
            }
            if (docs.Count == 0)
            {
                UpdateWindowTitle();
                if (keepOneTab) NewTab();
            }
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
        /// </summary>
        private bool Shutdown()
        {
            autosaveTimer.Stop();
            var rescued = new List<string>();
            var lost = new List<string>();
            foreach (NoteDocument doc in SaveAllDirty())
            {
                string original = doc.Path;
                bool wasUntitled = doc.Untitled;
                doc.Path = UniqueNotePath("恢复-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                    Path.GetFileNameWithoutExtension(original), Path.GetExtension(original));
                doc.Untitled = true;
                if (WriteDocument(doc, force: true) != null)
                {
                    rescued.Add(original + "\r\n    → " + doc.Path);
                }
                else
                {
                    doc.Path = original;
                    doc.Untitled = wasUntitled;
                    lost.Add(original);
                }
            }

            if (lost.Count > 0)
            {
                var answer = MessageBox.Show(this, "以下文件无法保存：\r\n" + string.Join("\r\n", lost) + "\r\n\r\n仍要退出吗？未保存的修改将丢失。",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return false;
            }
            if (rescued.Count > 0)
            {
                MessageBox.Show(this, "以下文件无法保存到原位置，内容已另存：\r\n" + string.Join("\r\n", rescued), AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            if (entry != null) RestoreBackup(doc, entry);
        }

        /// <summary>恢复存档：先对当前内容执行一次手动保存，再把存档内容写回（可撤销）。</summary>
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
            if (!(doc.Lossy ? ArchiveRawFile(doc) : SaveManually(doc))) return;

            NoteEditor editor = doc.Editor!;
            ApplyDecoded(doc, editor, decoded);
            editor.ReplaceAllUndoable(decoded.Text);
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

        /// <summary>有损解码的文档无法按文本保存，恢复存档前把磁盘上的原始字节原样存档。</summary>
        private bool ArchiveRawFile(NoteDocument doc)
        {
            try
            {
                if (File.Exists(doc.Path)) backups.Create(doc.Path, File.ReadAllBytes(doc.Path));
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
            if (checkingExternalChanges) return;
            checkingExternalChanges = true;
            try
            {
                foreach (NoteDocument doc in docs.ToArray())
                {
                    if (!doc.IsLoaded || doc.DiskStamp == null) continue;
                    FileStamp? now = FileStamp.Of(doc.Path);
                    if (now == null)
                    {
                        // 文件被外部删除：保留内容，下一次自动保存时重新写出；有损解码的内容不能写出
                        if (doc.Lossy) continue;
                        doc.DiskStamp = null;
                        doc.Dirty = true;
                        RestartTimer(autosaveTimer);
                        continue;
                    }
                    if (now.Value.Equals(doc.DiskStamp.Value)) continue;

                    if (!doc.Dirty)
                    {
                        Reload(doc);
                        continue;
                    }
                    var answer = MessageBox.Show(this, "“" + doc.Path + "”已被其他程序修改。\r\n\r\n是否重新载入？选择“否”将保留当前内容并覆盖该文件。",
                        AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes) Reload(doc);
                    else doc.DiskStamp = now;
                }
            }
            finally
            {
                checkingExternalChanges = false;
            }
        }

        private void Reload(NoteDocument doc)
        {
            DecodedText content;
            try
            {
                content = TextFile.Load(doc.Path, doc.EncodingForced ? doc.Encoding : null);
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                ShowMessage("无法重新载入“" + doc.Title + "”：" + ex.Message);
                return;
            }
            ShowDecoded(doc, content);
            if (doc.Lossy) ShowMessage("“" + doc.Title + "”被其他程序改成了无法按 " + doc.Encoding.Name + " 识别的内容，已只读打开");
        }

        /// <summary>用新的解码结果替换编辑器内容（不进入撤消记录，不触发自动保存）。</summary>
        private void ShowDecoded(NoteDocument doc, DecodedText content)
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
            doc.DiskStamp = FileStamp.Of(doc.Path);
            doc.Dirty = false;
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
