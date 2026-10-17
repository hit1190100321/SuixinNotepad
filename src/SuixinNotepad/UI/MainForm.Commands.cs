using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    /// <summary>编辑、查找、格式、查看、更新等菜单命令。</summary>
    internal sealed partial class MainForm : IFindHost
    {
        #region 编辑

        private void SearchWithBing()
        {
            NoteEditor? editor = Editor;
            if (editor == null || editor.SelectionLength == 0) return;
            string query = editor.GetTextRange(editor.SelectionStart, editor.SelectionStart + Math.Min(editor.SelectionLength, 500)).Trim();
            if (query.Length == 0) return;
            Process.Start("https://www.bing.com/search?q=" + Uri.EscapeDataString(query));
        }

        private static string DateTimeStamp()
        {
            DateTime now = DateTime.Now;
            return now.ToString("t", CultureInfo.CurrentCulture) + " " + now.ToString("d", CultureInfo.CurrentCulture);
        }

        private void InsertDateTime() => Editor?.ReplaceSelection(DateTimeStamp());

        private void GoToLine()
        {
            NoteEditor? editor = Editor;
            if (editor == null) return;
            string text = editor.GetText();
            int lineCount = 1;
            foreach (char c in text) if (c == '\n') lineCount++;
            editor.GetCaretLineColumn(out int currentLine, out _);

            string? input = InputDialog.Ask(this, "转到指定行", "行号(&L):", currentLine.ToString(CultureInfo.InvariantCulture), "转到", value =>
            {
                if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1) return "请输入行号。";
                return n > lineCount ? "行数超过了总行数" : null;
            });
            if (input == null) return;

            int start = TextSearch.LineStart(text, int.Parse(input.Trim(), CultureInfo.InvariantCulture));
            editor.Select(start, 0);
            editor.ScrollToCaret();
        }

        #endregion

        #region 查找

        private FindOptions RememberedFindOptions() => new FindOptions
        {
            Find = settings.FindText,
            Replace = settings.ReplaceText,
            MatchCase = settings.FindMatchCase,
            WrapAround = settings.FindWrapAround,
        };

        private void Remember(FindOptions options)
        {
            settings.FindText = options.Find;
            settings.ReplaceText = options.Replace;
            settings.FindMatchCase = options.MatchCase;
            settings.FindWrapAround = options.WrapAround;
        }

        private void OpenFind(bool replaceMode)
        {
            NoteEditor? editor = Editor;
            if (editor == null) return;
            string initial = "";
            if (editor.SelectionLength > 0 && editor.SelectionLength <= 200)
            {
                string selected = editor.GetTextRange(editor.SelectionStart, editor.SelectionStart + editor.SelectionLength);
                if (selected.IndexOf('\n') < 0) initial = selected;
            }
            if (findDialog == null) findDialog = new FindReplaceDialog(this);
            findDialog.Open(this, replaceMode, initial, RememberedFindOptions());
        }

        /// <summary>F3 / Shift+F3：用上次的查找内容继续查找。</summary>
        private void FindAgain(bool up)
        {
            if (settings.FindText.Length == 0)
            {
                OpenFind(false);
                return;
            }
            FindOptions options = RememberedFindOptions();
            options.Up = up;
            FindNext(options);
        }

        public void FindNext(FindOptions options)
        {
            NoteEditor? editor = Editor;
            if (editor == null || options.Find.Length == 0) return;
            Remember(options);
            int hit = TextSearch.FindNext(editor.GetText(), options, editor.SelectionStart, editor.SelectionLength);
            if (hit < 0)
            {
                IWin32Window owner = findDialog != null && findDialog.Visible ? (IWin32Window)findDialog : this;
                MessageBox.Show(owner, "找不到“" + options.Find + "”", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            editor.Select(hit, options.Find.Length);
            editor.ScrollToCaret();
        }

        public void Replace(FindOptions options)
        {
            NoteEditor? editor = Editor;
            if (editor == null || options.Find.Length == 0) return;
            string selected = editor.GetTextRange(editor.SelectionStart, editor.SelectionStart + editor.SelectionLength);
            if (string.Equals(selected, options.Find, options.Comparison))
                editor.ReplaceSelection(options.Replace);
            FindNext(options);
        }

        public void ReplaceAll(FindOptions options)
        {
            NoteEditor? editor = Editor;
            if (editor == null || options.Find.Length == 0) return;
            Remember(options);
            string result = TextSearch.ReplaceAll(editor.GetText(), options, out int count);
            if (count == 0)
            {
                IWin32Window owner = findDialog != null && findDialog.Visible ? (IWin32Window)findDialog : this;
                MessageBox.Show(owner, "找不到“" + options.Find + "”", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int caret = editor.SelectionStart;
            editor.ReplaceAllUndoable(result);
            editor.Select(Math.Min(caret, editor.TextLength), 0);
            editor.ScrollToCaret();
            ShowMessage("已替换 " + count + " 处");
        }

        #endregion

        #region 格式与查看

        private void SetWordWrap(bool wrap)
        {
            settings.WordWrap = wrap;
            wordWrapItem.Checked = wrap;
            foreach (NoteDocument doc in docs)
            {
                if (doc.Editor != null) doc.Editor.WrapLines = wrap;
            }
            SaveSettings();
        }

        private void ChooseFont()
        {
            using (var dialog = new FontDialog
            {
                Font = editorFont,
                ShowEffects = false,
                FontMustExist = true,
                AllowVerticalFonts = false,
                AllowScriptChange = true,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Font old = editorFont;
                editorFont = new Font(dialog.Font.FontFamily, dialog.Font.SizeInPoints, dialog.Font.Style, GraphicsUnit.Point);
                settings.FontName = editorFont.Name;
                settings.FontSize = editorFont.SizeInPoints;
                settings.FontStyle = (int)editorFont.Style;
                foreach (NoteDocument doc in docs)
                {
                    if (doc.Editor == null) continue;
                    doc.Editor.Font = editorFont;
                    doc.Editor.ZoomPercent = settings.ZoomPercent;
                }
                old.Dispose();
                SaveSettings();
            }
        }

        private void ChangeZoom(int delta) => SetZoom(settings.ZoomPercent + delta);

        private void SetZoom(int percent)
        {
            settings.ZoomPercent = Math.Max(NoteEditor.MinZoom, Math.Min(NoteEditor.MaxZoom, percent));
            if (Editor != null) Editor.ZoomPercent = settings.ZoomPercent;
            UpdateStatus();
        }

        private void SetStatusBarVisible(bool show)
        {
            settings.ShowStatusBar = show;
            statusBarItem.Checked = show;
            status.Visible = show;
            if (show) UpdateStatus();
            SaveSettings();
        }

        private void UpdateStatus()
        {
            NoteDocument? doc = active;
            if (doc?.Editor == null || !settings.ShowStatusBar) return;
            NoteEditor editor = doc.Editor;
            editor.GetCaretLineColumn(out int line, out int column);
            positionLabel.Text = "第 " + line + " 行，第 " + column + " 列";
            charsLabel.Text = editor.TextLength.ToString("N0", CultureInfo.CurrentCulture) + " 个字符";
            zoomLabel.Text = settings.ZoomPercent + "%";
            lineEndingButton.Text = TextFile.DisplayName(doc.LineEnding);
            encodingButton.Text = TextFile.DisplayName(doc.Encoding);
            if (doc.Untitled && doc.RefreshTitle()) UpdateTab(doc);
        }

        private void ChangeEncoding(FileEncoding encoding)
        {
            if (active == null || active.Encoding == encoding) return;
            active.Encoding = encoding;
            active.Dirty = true;
            RestartTimer(autosaveTimer);
            UpdateStatus();
        }

        private void ChangeLineEnding(LineEnding lineEnding)
        {
            if (active == null || active.LineEnding == lineEnding) return;
            active.LineEnding = lineEnding;
            active.Dirty = true;
            RestartTimer(autosaveTimer);
            UpdateStatus();
        }

        #endregion

        #region 文件

        private void Print()
        {
            if (active?.Editor == null) return;
            printer.Print(this, active.Editor.GetText(), editorFont, active.Untitled ? active.Title : active.FileName);
        }

        private void OpenContainingFolder(NoteDocument? doc)
        {
            if (doc == null) return;
            if (File.Exists(doc.Path))
                Process.Start("explorer.exe", "/select,\"" + doc.Path + "\"");
            else
                Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(doc.Path) + "\"");
        }

        private void ShowSettings()
        {
            using (var dialog = new SettingsDialog(settings, paths.BackupRoot))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) SaveSettings();
            }
        }

        #endregion

        #region 更新

        /// <summary>检查更新。手动检查时直接询问是否安装；启动时的自动检查只在状态栏给出提示。</summary>
        private async void CheckForUpdates(bool manual)
        {
            string source = settings.UpdateSource;
            if (source.Length == 0)
            {
                if (!manual) return;
                MessageBox.Show(this, "请先在“设置”中填写更新地址。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowSettings();
                return;
            }

            UpdateInfo? info;
            try
            {
                info = await Task.Run(() => UpdateService.Check(source));
            }
            catch (Exception ex) when (ex is IOException || ex is System.Net.WebException || ex is UnauthorizedAccessException ||
                                       ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException ||
                                       ex is NotSupportedException || ex is UriFormatException)
            {
                if (manual) MessageBox.Show(this, "检查更新失败：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            settings.LastUpdateCheck = DateTime.Now;
            SaveSettings();

            if (info == null)
            {
                if (manual) MessageBox.Show(this, "当前已是最新版本（" + UpdateService.CurrentVersion + "）。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            pendingUpdate = info;
            updateLabel.Text = "新版本 " + info.Version + " 可用";
            updateLabel.Visible = true;
            if (manual) InstallUpdate(info);
        }

        /// <summary>下载并校验安装包，保存全部内容后启动安装程序并退出；安装完成后程序自动重新打开。</summary>
        private async void InstallUpdate(UpdateInfo info)
        {
            var answer = MessageBox.Show(this, "发现新版本 " + info.Version + "（当前 " + UpdateService.CurrentVersion + "）。\r\n\r\n现在更新吗？更新完成后会自动重新打开。",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            ShowMessage("正在下载更新…");
            string setup;
            try
            {
                setup = await Task.Run(() => UpdateService.Download(info, paths.UpdateDownloadDir));
            }
            catch (Exception ex) when (ex is IOException || ex is System.Net.WebException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "下载更新失败：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Shutdown()) return;
            shutdownDone = true;
            Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
            Close();
        }

        #endregion
    }
}
