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
    /// <summary>标签页历史：按查看时间分天列出看过的文件，可搜索，选中一项打开。</summary>
    internal sealed class TabHistoryDialog : Form
    {
        private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

        private readonly TabHistory history;
        private readonly Func<string, bool> isOpen;
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill };
        private readonly ListView list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowGroups = true,
            ShowItemToolTips = true,
            Dock = DockStyle.Fill,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        private readonly Button open = new Button { Text = "打开(&O)", AutoSize = true, MinimumSize = new Size(Dpi.Scale(88), 0) };
        private readonly Button openFolder = new Button { Text = "打开所在文件夹(&D)", AutoSize = true };
        private readonly Button remove = new Button { Text = "从历史中移除(&R)", AutoSize = true };
        private readonly Font boldFont;

        /// <param name="isOpen">判断文件是否已在标签页中打开，已打开的项用粗体显示。</param>
        public TabHistoryDialog(TabHistory history, Func<string, bool> isOpen)
        {
            this.history = history;
            this.isOpen = isOpen;

            Text = "标签页历史";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ShowIcon = false;
            MinimizeBox = false;
            Size = Dpi.Scale(820, 560);
            MinimumSize = Dpi.Scale(520, 340);
            boldFont = new Font(Font, FontStyle.Bold);

            list.Columns.Add("名称", Dpi.Scale(280));
            list.Columns.Add("查看时间", Dpi.Scale(90));
            list.Columns.Add("位置", Dpi.Scale(380));

            var searchRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Padding = Dpi.Scale(new Padding(6, 6, 6, 2)),
            };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.Controls.Add(new Label { Text = "搜索(&S):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            searchRow.Controls.Add(search, 1, 0);

            var listHost = new Panel { Dock = DockStyle.Fill, Padding = Dpi.Scale(new Padding(6, 4, 6, 0)) };
            listHost.Controls.Add(list);

            var close = new Button { Text = "关闭", AutoSize = true, MinimumSize = new Size(Dpi.Scale(88), 0), DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = Dpi.Scale(new Padding(6)),
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(open);
            buttons.Controls.Add(openFolder);
            buttons.Controls.Add(remove);

            Controls.Add(listHost);
            Controls.Add(searchRow);
            Controls.Add(buttons);
            AcceptButton = open;
            CancelButton = close;

            search.TextChanged += (s, e) => Fill();
            search.KeyDown += (s, e) =>
            {
                // 在搜索框中用上下键移动选中项
                if (e.KeyCode != Keys.Down && e.KeyCode != Keys.Up) return;
                MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
                e.Handled = true;
            };
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.ItemActivate += (s, e) => Accept();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            open.Click += (s, e) => Accept();
            openFolder.Click += (s, e) => OpenFolder();
            remove.Click += (s, e) => RemoveSelected();
            Load += (s, e) => Fill();
        }

        /// <summary>用户选择打开的记录。</summary>
        public TabHistoryEntry? Selected => list.SelectedItems.Count == 1 ? (TabHistoryEntry)list.SelectedItems[0].Tag : null;

        /// <summary>对话框中移除过记录，需要保存。</summary>
        public bool HistoryChanged { get; private set; }

        private void Fill()
        {
            List<TabHistoryEntry> entries = history.Search(search.Text);
            DateTime today = DateTime.Today;
            list.BeginUpdate();
            list.Items.Clear();
            list.Groups.Clear();
            ListViewGroup? group = null;
            DateTime groupDay = DateTime.MinValue;
            foreach (TabHistoryEntry entry in entries)
            {
                DateTime day = entry.Viewed.Date;
                if (group == null || day != groupDay)
                {
                    group = new ListViewGroup(DayLabel(day, today));
                    list.Groups.Add(group);
                    groupDay = day;
                }
                var item = new ListViewItem(entry.Title, group) { Tag = entry, ToolTipText = entry.Path };
                item.SubItems.Add(entry.Viewed.ToString("HH:mm", CultureInfo.InvariantCulture));
                item.SubItems.Add(Path.GetDirectoryName(entry.Path));
                if (isOpen(entry.Path)) item.Font = boldFont;
                list.Items.Add(item);
            }
            list.EndUpdate();
            if (list.Items.Count > 0) list.Items[0].Selected = true;
            UpdateButtons();
        }

        private static string DayLabel(DateTime day, DateTime today)
        {
            if (day == today) return "今天";
            if (day == today.AddDays(-1)) return "昨天";
            string format = day.Year == today.Year ? "M月d日 dddd" : "yyyy年M月d日 dddd";
            return day.ToString(format, Chinese);
        }

        private void MoveSelection(int delta)
        {
            if (list.Items.Count == 0) return;
            int index = list.SelectedIndices.Count == 1 ? list.SelectedIndices[0] + delta : 0;
            index = Math.Max(0, Math.Min(list.Items.Count - 1, index));
            list.Items[index].Selected = true;
            list.Items[index].EnsureVisible();
        }

        private void UpdateButtons()
        {
            open.Enabled = openFolder.Enabled = remove.Enabled = Selected != null;
        }

        private void Accept()
        {
            if (Selected != null) DialogResult = DialogResult.OK;
        }

        private void OpenFolder()
        {
            TabHistoryEntry? entry = Selected;
            if (entry == null) return;
            string arguments = File.Exists(entry.Path) ? "/select,\"" + entry.Path + "\"" : "\"" + Path.GetDirectoryName(entry.Path) + "\"";
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", arguments));
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show(this, "无法打开文件夹：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RemoveSelected()
        {
            TabHistoryEntry? entry = Selected;
            if (entry == null) return;
            int index = list.SelectedIndices[0];
            history.Remove(entry.Path);
            HistoryChanged = true;
            Fill();
            if (list.Items.Count > 0)
            {
                index = Math.Min(index, list.Items.Count - 1);
                list.Items[index].Selected = true;
                list.Items[index].EnsureVisible();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) boldFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
