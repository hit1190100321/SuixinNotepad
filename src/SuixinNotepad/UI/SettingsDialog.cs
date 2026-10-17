using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    internal sealed class SettingsDialog : DialogBase
    {
        private readonly AppSettings settings;
        private readonly TextBox notesFolder = new TextBox { Width = Dpi.Scale(340), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly CheckBox autoBackup = new CheckBox { Text = "手动保存时自动备份", AutoSize = true, Margin = Dpi.Scale(new Padding(0, 6, 0, 3)) };
        private readonly CheckBox autoCleanup = new CheckBox
        {
            Text = "自动清理旧备份（保留一年以内、每个文件最多 " + BackupStore.MaxCount + " 份）",
            AutoSize = true,
            Margin = Dpi.Scale(new Padding(20, 3, 0, 3)),
        };
        private readonly TextBox updateSource = new TextBox { Width = Dpi.Scale(340), Anchor = AnchorStyles.Left | AnchorStyles.Right };

        public SettingsDialog(AppSettings settings, string backupRoot)
        {
            this.settings = settings;
            Text = "设置";

            var table = MakeTable(3);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var browse = MakeButton("浏览...");
            table.Controls.Add(MakeLabel("默认保存位置:"), 0, 0);
            table.Controls.Add(notesFolder, 1, 0);
            table.Controls.Add(browse, 2, 0);

            table.Controls.Add(autoBackup, 0, 1);
            table.SetColumnSpan(autoBackup, 3);
            table.Controls.Add(autoCleanup, 0, 2);
            table.SetColumnSpan(autoCleanup, 3);

            var backupPath = new TextBox { Text = backupRoot, ReadOnly = true, Width = Dpi.Scale(340), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            var openBackups = MakeButton("打开");
            table.Controls.Add(MakeLabel("备份位置:"), 0, 3);
            table.Controls.Add(backupPath, 1, 3);
            table.Controls.Add(openBackups, 2, 3);

            table.Controls.Add(MakeLabel("更新地址:"), 0, 4);
            table.Controls.Add(updateSource, 1, 4);
            table.SetColumnSpan(updateSource, 2);

            var ok = MakeButton("确定");
            var cancel = MakeButton("取消", DialogResult.Cancel);
            var buttons = MakeButtonRow();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            table.Controls.Add(buttons, 0, 5);
            table.SetColumnSpan(buttons, 3);
            Controls.Add(table);

            AcceptButton = ok;
            CancelButton = cancel;

            notesFolder.Text = settings.NotesFolder;
            autoBackup.Checked = settings.AutoBackup;
            autoCleanup.Checked = settings.AutoCleanup;
            updateSource.Text = settings.UpdateSource;
            SyncCleanup();

            autoBackup.CheckedChanged += (s, e) => SyncCleanup();
            browse.Click += (s, e) => BrowseFolder();
            openBackups.Click += (s, e) =>
            {
                Directory.CreateDirectory(backupRoot);
                Process.Start("explorer.exe", "\"" + backupRoot + "\"");
            };
            ok.Click += (s, e) => Confirm();
        }

        /// <summary>关闭自动备份时，自动清理随之失效。</summary>
        private void SyncCleanup() => autoCleanup.Enabled = autoBackup.Checked;

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择默认保存位置", SelectedPath = notesFolder.Text, ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) notesFolder.Text = dialog.SelectedPath;
            }
        }

        private void Confirm()
        {
            string folder = notesFolder.Text.Trim();
            try
            {
                if (folder.Length == 0 || !Path.IsPathRooted(folder)) throw new ArgumentException("请填写完整的文件夹路径。");
                folder = Path.GetFullPath(folder);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                MessageBox.Show(this, "无法使用这个文件夹：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                notesFolder.Focus();
                return;
            }

            settings.NotesFolder = folder;
            settings.AutoBackup = autoBackup.Checked;
            settings.AutoCleanup = autoCleanup.Checked;
            settings.UpdateSource = updateSource.Text.Trim();
            DialogResult = DialogResult.OK;
        }
    }
}
