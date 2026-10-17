using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    /// <summary>某个 txt 的存档历史：按时间列出备份、预览内容、选择一份恢复。</summary>
    internal sealed class HistoryDialog : Form
    {
        private readonly BackupStore store;
        private readonly string filePath;
        private readonly TextEncoding? encoding;
        private readonly ListView list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Dock = DockStyle.Fill,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        private readonly NoteEditor preview = new NoteEditor
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            AllowDrop = false,
        };
        private readonly Button restore = new Button { Text = "恢复此版本(&R)", AutoSize = true, MinimumSize = new Size(Dpi.Scale(110), 0) };
        private IReadOnlyList<BackupEntry> entries = Array.Empty<BackupEntry>();

        /// <param name="encoding">用户为该文件选定的编码；为 null 时自动识别。</param>
        public HistoryDialog(BackupStore store, string filePath, TextEncoding? encoding, string title, Font editorFont)
        {
            this.store = store;
            this.filePath = filePath;
            this.encoding = encoding;

            Text = "存档历史 - " + title;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            MinimizeBox = false;
            Size = Dpi.Scale(900, 560);
            MinimumSize = Dpi.Scale(560, 360);
            preview.Font = editorFont;

            list.Columns.Add("保存时间", Dpi.Scale(170));
            list.Columns.Add("大小", Dpi.Scale(80), HorizontalAlignment.Right);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(list);
            Load += (s, e) => split.SplitterDistance = Dpi.Scale(270);
            split.Panel2.Controls.Add(preview);

            var openFolder = new Button { Text = "打开备份文件夹", AutoSize = true };
            var close = new Button { Text = "关闭", AutoSize = true, MinimumSize = new Size(Dpi.Scale(88), 0), DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = Dpi.Scale(new Padding(6)),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(restore);
            buttons.Controls.Add(openFolder);

            Controls.Add(split);
            Controls.Add(buttons);
            CancelButton = close;

            list.SelectedIndexChanged += (s, e) => ShowPreview();
            restore.Click += (s, e) => { if (Selected != null) DialogResult = DialogResult.OK; };
            openFolder.Click += (s, e) => OpenFolder();
            Load += (s, e) => LoadEntries();
        }

        /// <summary>用户选择恢复的备份。</summary>
        public BackupEntry? Selected => list.SelectedIndices.Count == 1 ? entries[list.SelectedIndices[0]] : null;

        private void LoadEntries()
        {
            entries = store.List(filePath);
            list.BeginUpdate();
            list.Items.Clear();
            foreach (BackupEntry entry in entries)
            {
                var item = new ListViewItem(entry.SavedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                item.SubItems.Add(FormatSize(entry.Size));
                list.Items.Add(item);
            }
            list.EndUpdate();

            if (entries.Count > 0) list.Items[0].Selected = true;
            ShowPreview();
        }

        private void ShowPreview()
        {
            BackupEntry? entry = Selected;
            restore.Enabled = entry != null;
            if (entry == null)
            {
                preview.SetText(entries.Count == 0 ? "暂无存档" : "");
                return;
            }
            try
            {
                preview.SetText(TextFile.Decode(store.Read(entry), encoding).Text);
            }
            catch (IOException ex)
            {
                preview.SetText("无法读取存档：" + ex.Message);
            }
        }

        private void OpenFolder()
        {
            string dir = store.GetDirectory(filePath);
            if (!Directory.Exists(dir))
            {
                MessageBox.Show(this, "这个文件还没有存档。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
