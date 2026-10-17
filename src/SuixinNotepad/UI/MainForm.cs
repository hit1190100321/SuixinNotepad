using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    /// <summary>主窗口：界面搭建、菜单与快捷键。文档与命令逻辑见其余 partial 文件。</summary>
    internal sealed partial class MainForm : Form
    {
        public const string AppName = "随心记事本";

        /// <summary>状态栏单元格高度（逻辑像素）。</summary>
        private const int StatusHeight = 24;

        private readonly AppPaths paths;
        private readonly AppSettings settings;
        private readonly BackupStore backups;
        private readonly List<NoteDocument> docs = new List<NoteDocument>();
        private readonly TabStrip tabStrip = new TabStrip { Dock = DockStyle.Top };
        private readonly Panel editorHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, AllowDrop = true };
        private readonly MenuStrip menu = new MenuStrip();
        private readonly StatusStrip status = new StatusStrip { SizingGrip = true, AutoSize = false, Height = Dpi.Scale(StatusHeight + 2) };
        private readonly ToolStripStatusLabel messageLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel updateLabel = new ToolStripStatusLabel { IsLink = true, Visible = false };
        private readonly ToolStripStatusLabel positionLabel = StatusCell(140);
        private readonly ToolStripStatusLabel charsLabel = StatusCell(110);
        private readonly ToolStripStatusLabel zoomLabel = StatusCell(56);
        private readonly ToolStripDropDownButton lineEndingButton = new ToolStripDropDownButton { ShowDropDownArrow = false, AutoSize = false, Size = Dpi.Scale(120, StatusHeight) };
        private readonly ToolStripDropDownButton encodingButton = new ToolStripDropDownButton { ShowDropDownArrow = false, AutoSize = false, Size = Dpi.Scale(170, StatusHeight) };
        private readonly Timer autosaveTimer = new Timer { Interval = 1500 };
        private readonly Timer retryTimer = new Timer { Interval = 10000 };
        private readonly Timer statusTimer = new Timer { Interval = 120 };
        private readonly Timer messageTimer = new Timer { Interval = 5000 };
        private readonly ContextMenuStrip editorMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip tabMenu = new ContextMenuStrip();
        private readonly TextPrinter printer = new TextPrinter();

        private NoteDocument? active;
        private Font editorFont;
        private int tabMenuIndex = -1;
        private FindReplaceDialog? findDialog;
        private string lastSessionJson = "";
        private UpdateInfo? pendingUpdate;
        private bool suppressTextEvents;
        private bool askingExternalChange;
        private bool shutdownDone;
        private bool installingUpdate;
        private FormWindowState lastVisibleState = FormWindowState.Normal;

        private ToolStripMenuItem wordWrapItem = null!;
        private ToolStripMenuItem statusBarItem = null!;

        public MainForm(AppPaths paths, AppSettings settings, SessionState session, string[] startupFiles)
        {
            this.paths = paths;
            this.settings = settings;
            settings.ZoomPercent = Math.Max(NoteEditor.MinZoom, Math.Min(NoteEditor.MaxZoom, settings.ZoomPercent));
            backups = new BackupStore(paths.BackupRoot);
            editorFont = CreateEditorFont(settings);

            Text = AppName;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            AllowDrop = true;
            KeyPreview = false;
            using (Stream? iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SuixinNotepad.app.ico"))
            {
                if (iconStream == null) throw new InvalidOperationException("缺少嵌入的程序图标。");
                Icon = new Icon(iconStream);
            }

            BuildMenus();
            BuildStatusBar();
            BuildContextMenus();

            menu.Renderer = new ToolStripProfessionalRenderer(new LightColorTable());
            status.Renderer = menu.Renderer;
            menu.BackColor = Color.White;
            status.BackColor = Color.FromArgb(243, 243, 243);
            tabStrip.Font = Font;

            Controls.Add(editorHost);
            Controls.Add(tabStrip);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;
            status.Visible = settings.ShowStatusBar;

            tabStrip.TabClicked += (s, e) => ActivateDocument(docs[e.Index]);
            tabStrip.CloseRequested += (s, e) => CloseTab(e.Index);
            tabStrip.NewTabRequested += (s, e) => NewTab();
            tabStrip.TabMoved += (s, e) => MoveDocument(e.From, e.To);
            tabStrip.TabRightClicked += (s, e) =>
            {
                tabMenuIndex = e.Index;
                tabMenu.Show(tabStrip, e.Location);
            };

            foreach (Control target in new Control[] { this, tabStrip, editorHost })
            {
                target.DragEnter += OnFileDragEnter;
                target.DragDrop += OnFileDragDrop;
            }

            autosaveTimer.Tick += (s, e) => AutosaveTick();
            retryTimer.Tick += (s, e) => AutosaveTick();
            statusTimer.Tick += (s, e) => { statusTimer.Stop(); UpdateStatus(); };
            messageTimer.Tick += (s, e) => { messageTimer.Stop(); messageLabel.Text = ""; };

            RestoreWindowBounds();
            // 先建立上次的标签，再打开命令行中的文件；都没有激活任何标签时才激活上次的活动标签
            int preferred = RestoreSession(session);
            OpenFiles(startupFiles);
            if (active == null) ActivateNearest(preferred);
        }

        private static ToolStripStatusLabel StatusCell(int width) => new ToolStripStatusLabel
        {
            AutoSize = false,
            Size = Dpi.Scale(width, StatusHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            BorderSides = ToolStripStatusLabelBorderSides.Left,
            BorderStyle = Border3DStyle.Etched,
        };

        private static Font CreateEditorFont(AppSettings s)
        {
            Font? font;
            try
            {
                font = new Font(s.FontName, s.FontSize, (FontStyle)s.FontStyle);
            }
            catch (ArgumentException)
            {
                // 字体不支持设置中的字形（如只有粗体的字体选了常规）
                font = null;
            }
            // 字体已被卸载时 GDI+ 会替换成其他字体，此时回退到默认字体并记入设置
            if (font == null || !string.Equals(font.Name, s.FontName, StringComparison.OrdinalIgnoreCase))
            {
                font?.Dispose();
                font = new Font(AppSettings.DefaultFontName, AppSettings.DefaultFontSize);
                s.FontName = font.Name;
                s.FontSize = font.SizeInPoints;
                s.FontStyle = 0;
            }
            return font;
        }

        #region 菜单

        private static ToolStripMenuItem Item(string text, EventHandler onClick, Keys keys = Keys.None, string? keyDisplay = null)
        {
            var item = new ToolStripMenuItem(text, null, onClick);
            if (keys != Keys.None) item.ShortcutKeys = keys;
            if (keyDisplay != null) item.ShortcutKeyDisplayString = keyDisplay;
            return item;
        }

        private void BuildMenus()
        {
            var file = new ToolStripMenuItem("文件(&F)");
            file.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("新建标签页(&T)", (s, e) => NewTab(), Keys.Control | Keys.N),
                Item("打开(&O)...", (s, e) => OpenWithDialog(), Keys.Control | Keys.O),
                Item("保存(&S)", (s, e) => ManualSave(), Keys.Control | Keys.S),
                Item("另存为(&A)...", (s, e) => SaveAs(), Keys.Control | Keys.Shift | Keys.S),
                Item("全部保存(&L)", (s, e) => SaveAll(), Keys.Control | Keys.Alt | Keys.S),
                new ToolStripSeparator(),
                Item("存档历史(&H)...", (s, e) => ShowHistory(active), Keys.Control | Keys.Shift | Keys.H),
                Item("重命名(&M)...", (s, e) => Rename(active)),
                Item("打开所在文件夹(&D)", (s, e) => OpenContainingFolder(active)),
                new ToolStripSeparator(),
                Item("页面设置(&U)...", (s, e) => printer.PageSetup(this)),
                Item("打印(&P)...", (s, e) => Print(), Keys.Control | Keys.P),
                new ToolStripSeparator(),
                Item("关闭标签页(&C)", (s, e) => { if (active != null) CloseTab(docs.IndexOf(active)); }, Keys.Control | Keys.W),
                Item("清空标签页(&R)", (s, e) => ClearAllTabs()),
                new ToolStripSeparator(),
                Item("设置(&G)...", (s, e) => ShowSettings()),
                new ToolStripSeparator(),
                Item("退出(&X)", (s, e) => Close()),
            });

            var undo = Item("撤消(&U)", (s, e) => Editor?.Undo(), Keys.None, "Ctrl+Z");
            var redo = Item("重做(&R)", (s, e) => Editor?.Redo(), Keys.None, "Ctrl+Y");
            var cut = Item("剪切(&T)", (s, e) => Editor?.Cut(), Keys.None, "Ctrl+X");
            var copy = Item("复制(&C)", (s, e) => Editor?.Copy(), Keys.None, "Ctrl+C");
            var paste = Item("粘贴(&P)", (s, e) => Editor?.PastePlainText(), Keys.None, "Ctrl+V");
            var delete = Item("删除(&L)", (s, e) => Editor?.ReplaceSelection(""), Keys.None, "Del");
            var search = Item("使用 Bing 搜索(&S)...", (s, e) => SearchWithBing(), Keys.Control | Keys.E);
            var edit = new ToolStripMenuItem("编辑(&E)");
            edit.DropDownItems.AddRange(new ToolStripItem[]
            {
                undo, redo,
                new ToolStripSeparator(),
                cut, copy, paste, delete,
                new ToolStripSeparator(),
                search,
                Item("查找(&F)...", (s, e) => OpenFind(false), Keys.Control | Keys.F),
                Item("查找下一个(&N)", (s, e) => FindAgain(false), Keys.F3),
                Item("查找上一个(&V)", (s, e) => FindAgain(true), Keys.Shift | Keys.F3),
                Item("替换(&R)...", (s, e) => OpenFind(true), Keys.Control | Keys.H),
                Item("转到(&G)...", (s, e) => GoToLine(), Keys.Control | Keys.G),
                new ToolStripSeparator(),
                Item("全选(&A)", (s, e) => Editor?.SelectAll(), Keys.None, "Ctrl+A"),
                Item("时间/日期(&D)", (s, e) => InsertDateTime(), Keys.F5),
            });
            edit.DropDownOpening += (s, e) =>
            {
                NoteEditor? ed = Editor;
                bool hasSelection = ed != null && ed.SelectionLength > 0;
                undo.Enabled = ed != null && ed.CanUndo;
                redo.Enabled = ed != null && ed.CanRedo;
                cut.Enabled = copy.Enabled = delete.Enabled = search.Enabled = hasSelection;
                paste.Enabled = ed != null && !ed.ReadOnly && ClipboardHasText();
            };
            // 菜单关闭后恢复可用，保证带快捷键的菜单项始终响应
            edit.DropDownClosed += (s, e) =>
            {
                foreach (ToolStripItem item in edit.DropDownItems) item.Enabled = true;
            };

            wordWrapItem = Item("自动换行(&W)", (s, e) => SetWordWrap(!settings.WordWrap));
            wordWrapItem.Checked = settings.WordWrap;
            var format = new ToolStripMenuItem("格式(&O)");
            format.DropDownItems.AddRange(new ToolStripItem[]
            {
                wordWrapItem,
                Item("字体(&F)...", (s, e) => ChooseFont()),
            });

            var zoom = new ToolStripMenuItem("缩放(&Z)");
            zoom.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("放大(&I)", (s, e) => ChangeZoom(+10), Keys.Control | Keys.Oemplus, "Ctrl+加号"),
                Item("缩小(&O)", (s, e) => ChangeZoom(-10), Keys.Control | Keys.OemMinus, "Ctrl+减号"),
                Item("恢复默认缩放(&R)", (s, e) => SetZoom(100), Keys.Control | Keys.D0, "Ctrl+0"),
            });
            statusBarItem = Item("状态栏(&S)", (s, e) => SetStatusBarVisible(!settings.ShowStatusBar));
            statusBarItem.Checked = settings.ShowStatusBar;
            var view = new ToolStripMenuItem("查看(&V)");
            view.DropDownItems.AddRange(new ToolStripItem[] { zoom, statusBarItem });

            var help = new ToolStripMenuItem("帮助(&H)");
            help.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("检查更新(&U)...", (s, e) => CheckForUpdates(true)),
                new ToolStripSeparator(),
                Item("关于随心记事本(&A)", (s, e) => ShowAbout()),
            });

            menu.Items.AddRange(new ToolStripItem[] { file, edit, format, view, help });
        }

        /// <summary>编码子菜单：所有支持的编码，按语言分组，组间用分隔线隔开。</summary>
        private static ToolStripMenuItem EncodingMenu(string text, Action<TextEncoding> onClick)
        {
            var menu = new ToolStripMenuItem(text);
            foreach (IReadOnlyList<TextEncoding> group in TextEncoding.Groups)
            {
                if (menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
                foreach (TextEncoding encoding in group)
                {
                    var item = new ToolStripMenuItem(encoding.Label) { Tag = encoding };
                    item.Click += (s, e) => onClick(encoding);
                    menu.DropDownItems.Add(item);
                }
            }
            return menu;
        }

        private void BuildStatusBar()
        {
            var reopen = EncodingMenu("通过编码重新打开(&R)", ReopenWithEncoding);
            var saveAs = EncodingMenu("通过编码保存(&S)", SaveWithEncoding);
            encodingButton.DropDownItems.Add(reopen);
            encodingButton.DropDownItems.Add(saveAs);
            foreach (LineEnding le in Enum.GetValues(typeof(LineEnding)))
            {
                LineEnding value = le;
                var item = new ToolStripMenuItem(TextFile.DisplayName(value)) { Tag = value };
                item.Click += (s, e) => ChangeLineEnding(value);
                lineEndingButton.DropDownItems.Add(item);
            }
            encodingButton.DropDownOpening += (s, e) =>
            {
                reopen.Enabled = active != null && File.Exists(active.Path);
                saveAs.Enabled = active != null && !active.Lossy;
                foreach (ToolStripMenuItem menu in new[] { reopen, saveAs })
                {
                    foreach (ToolStripItem item in menu.DropDownItems)
                    {
                        if (item is ToolStripMenuItem entry) entry.Checked = active != null && entry.Tag == active.Encoding;
                    }
                }
            };
            lineEndingButton.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripMenuItem item in lineEndingButton.DropDownItems)
                    item.Checked = active != null && (LineEnding)item.Tag == active.LineEnding;
            };
            updateLabel.Click += (s, e) => { if (pendingUpdate != null) InstallUpdate(pendingUpdate); };

            status.Items.AddRange(new ToolStripItem[] { messageLabel, updateLabel, positionLabel, charsLabel, zoomLabel, lineEndingButton, encodingButton });
        }

        private void BuildContextMenus()
        {
            var undo = Item("撤消(&U)", (s, e) => Editor?.Undo());
            var cut = Item("剪切(&T)", (s, e) => Editor?.Cut());
            var copy = Item("复制(&C)", (s, e) => Editor?.Copy());
            var paste = Item("粘贴(&P)", (s, e) => Editor?.PastePlainText());
            var delete = Item("删除(&D)", (s, e) => Editor?.ReplaceSelection(""));
            var search = Item("使用 Bing 搜索(&S)", (s, e) => SearchWithBing());
            editorMenu.Items.AddRange(new ToolStripItem[]
            {
                undo,
                new ToolStripSeparator(),
                cut, copy, paste, delete,
                new ToolStripSeparator(),
                Item("全选(&A)", (s, e) => Editor?.SelectAll()),
                new ToolStripSeparator(),
                search,
            });
            editorMenu.Opening += (s, e) =>
            {
                NoteEditor? ed = Editor;
                bool hasSelection = ed != null && ed.SelectionLength > 0;
                undo.Enabled = ed != null && ed.CanUndo;
                cut.Enabled = copy.Enabled = delete.Enabled = search.Enabled = hasSelection;
                paste.Enabled = ed != null && !ed.ReadOnly && ClipboardHasText();
            };

            var closeRight = Item("关闭右侧标签页", (s, e) => CloseTabsWhere(i => i > tabMenuIndex));
            tabMenu.Items.AddRange(new ToolStripItem[]
            {
                Item("关闭标签页", (s, e) => CloseTab(tabMenuIndex)),
                Item("关闭其他标签页", (s, e) => CloseTabsWhere(i => i != tabMenuIndex)),
                closeRight,
                new ToolStripSeparator(),
                Item("重命名...", (s, e) => Rename(TabMenuDocument)),
                Item("复制完整路径", (s, e) => CopyPath(TabMenuDocument)),
                Item("打开所在文件夹", (s, e) => OpenContainingFolder(TabMenuDocument)),
                new ToolStripSeparator(),
                Item("存档历史...", (s, e) => ShowHistory(TabMenuDocument)),
            });
            tabMenu.Opening += (s, e) => closeRight.Enabled = tabMenuIndex < docs.Count - 1;
        }

        private void CopyPath(NoteDocument? doc)
        {
            if (doc == null) return;
            try
            {
                Clipboard.SetText(doc.Path);
            }
            catch (ExternalException)
            {
                ShowMessage("剪贴板正被其他程序占用，请稍后再试");
            }
        }

        /// <summary>剪贴板中是否有文本。剪贴板被其他程序占用时当作没有。</summary>
        private static bool ClipboardHasText()
        {
            try
            {
                return Clipboard.ContainsText();
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private NoteDocument? TabMenuDocument => tabMenuIndex >= 0 && tabMenuIndex < docs.Count ? docs[tabMenuIndex] : null;

        private NoteEditor? Editor => active?.Editor;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Tab:
                case Keys.Control | Keys.PageDown:
                    SwitchTab(+1);
                    return true;
                case Keys.Control | Keys.Shift | Keys.Tab:
                case Keys.Control | Keys.PageUp:
                    SwitchTab(-1);
                    return true;
                case Keys.Control | Keys.T:
                    NewTab();
                    return true;
                case Keys.Control | Keys.Add:
                    ChangeZoom(+10);
                    return true;
                case Keys.Control | Keys.Subtract:
                    ChangeZoom(-10);
                    return true;
                case Keys.Control | Keys.NumPad0:
                    SetZoom(100);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        #endregion

        #region 窗口

        private void RestoreWindowBounds()
        {
            StartPosition = FormStartPosition.Manual;
            int width = settings.WindowWidth > 0 ? settings.WindowWidth : Dpi.Scale(960);
            int height = settings.WindowHeight > 0 ? settings.WindowHeight : Dpi.Scale(680);
            var bounds = new Rectangle(settings.WindowX, settings.WindowY, Math.Max(Dpi.Scale(400), width), Math.Max(Dpi.Scale(300), height));
            bool visible = settings.WindowX != int.MinValue && Screen.AllScreens.Length > 0 &&
                           Array.Exists(Screen.AllScreens, sc => sc.WorkingArea.IntersectsWith(new Rectangle(bounds.X, bounds.Y, 200, 60)));
            if (!visible)
            {
                Rectangle area = Screen.PrimaryScreen!.WorkingArea;
                bounds.Width = Math.Min(bounds.Width, area.Width);
                bounds.Height = Math.Min(bounds.Height, area.Height);
                bounds.X = area.Left + (area.Width - bounds.Width) / 2;
                bounds.Y = area.Top + (area.Height - bounds.Height) / 2;
            }
            Bounds = bounds;
            if (settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        private void RememberWindowBounds()
        {
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.WindowX = b.X;
            settings.WindowY = b.Y;
            settings.WindowWidth = b.Width;
            settings.WindowHeight = b.Height;
            settings.WindowMaximized = WindowState == FormWindowState.Maximized ||
                                       (WindowState == FormWindowState.Minimized && lastVisibleState == FormWindowState.Maximized);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState != FormWindowState.Minimized) lastVisibleState = WindowState;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Editor?.Focus();
            if (settings.UpdateSource.Length > 0 && (DateTime.Now - settings.LastUpdateCheck).TotalHours >= 24)
                CheckForUpdates(false);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            CheckExternalChanges();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || shutdownDone) return;
            if (!Shutdown())
            {
                e.Cancel = true;
                return;
            }
            shutdownDone = true;
        }

        /// <summary>由其他进程启动时转交的文件。</summary>
        public void OpenFromOtherInstance(string[] files)
        {
            OpenFiles(files);
            if (WindowState == FormWindowState.Minimized) WindowState = lastVisibleState;
            Activate();
        }

        private void ShowAbout()
        {
            MessageBox.Show(this, AppName + "\r\n版本 " + UpdateService.CurrentVersion, "关于" + AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowMessage(string text)
        {
            messageLabel.Text = text;
            messageTimer.Stop();
            messageTimer.Start();
        }

        private void OnFileDragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnFileDragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) OpenFiles(files);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                autosaveTimer.Dispose();
                retryTimer.Dispose();
                statusTimer.Dispose();
                messageTimer.Dispose();
                editorFont.Dispose();
                findDialog?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        /// <summary>菜单与状态栏的浅色配色，接近 Windows 11 记事本。</summary>
        private sealed class LightColorTable : ProfessionalColorTable
        {
            private static readonly Color Hover = Color.FromArgb(229, 241, 251);
            private static readonly Color HoverBorder = Color.FromArgb(204, 228, 247);

            public override Color MenuStripGradientBegin => Color.White;
            public override Color MenuStripGradientEnd => Color.White;
            public override Color StatusStripGradientBegin => Color.FromArgb(243, 243, 243);
            public override Color StatusStripGradientEnd => Color.FromArgb(243, 243, 243);
            public override Color ToolStripDropDownBackground => Color.White;
            public override Color ImageMarginGradientBegin => Color.White;
            public override Color ImageMarginGradientMiddle => Color.White;
            public override Color ImageMarginGradientEnd => Color.White;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemBorder => HoverBorder;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Hover;
            public override Color MenuItemPressedGradientEnd => Hover;
            public override Color MenuBorder => Color.FromArgb(204, 204, 204);
            public override Color ButtonSelectedHighlight => Hover;
            public override Color ButtonSelectedGradientBegin => Hover;
            public override Color ButtonSelectedGradientEnd => Hover;
            public override Color ButtonSelectedBorder => HoverBorder;
            public override Color ButtonPressedGradientBegin => HoverBorder;
            public override Color ButtonPressedGradientEnd => HoverBorder;
            public override Color SeparatorDark => Color.FromArgb(220, 220, 220);
            public override Color SeparatorLight => Color.White;
        }
    }
}
