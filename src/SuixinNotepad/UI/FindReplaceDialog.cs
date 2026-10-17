using System;
using System.Windows.Forms;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    internal interface IFindHost
    {
        /// <summary>查找并选中下一个匹配；找不到时由宿主提示。</summary>
        void FindNext(FindOptions options);

        /// <summary>当前选区正好是匹配时替换它，然后查找下一个。</summary>
        void Replace(FindOptions options);

        void ReplaceAll(FindOptions options);
    }

    /// <summary>非模态的查找/替换对话框，布局与 Windows 记事本一致。</summary>
    internal sealed class FindReplaceDialog : DialogBase
    {
        private readonly IFindHost host;
        private readonly TextBox findBox = new TextBox { Width = Dpi.Scale(240), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly TextBox replaceBox = new TextBox { Width = Dpi.Scale(240), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly Label replaceLabel = MakeLabel("替换为(&P):");
        private readonly CheckBox matchCase = new CheckBox { Text = "区分大小写(&C)", AutoSize = true };
        private readonly CheckBox wrapAround = new CheckBox { Text = "循环(&R)", AutoSize = true };
        private readonly RadioButton up = new RadioButton { Text = "向上(&U)", AutoSize = true };
        private readonly RadioButton down = new RadioButton { Text = "向下(&D)", AutoSize = true, Checked = true };
        private readonly GroupBox direction = new GroupBox { Text = "方向", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        private readonly Button findNext = MakeButton("查找下一个(&F)");
        private readonly Button replace = MakeButton("替换(&R)");
        private readonly Button replaceAll = MakeButton("全部替换(&A)");
        private readonly Button cancel = MakeButton("取消");

        public FindReplaceDialog(IFindHost host)
        {
            this.host = host;
            KeyPreview = true;

            var fields = MakeTable(2);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.Controls.Add(MakeLabel("查找内容(&N):"), 0, 0);
            fields.Controls.Add(findBox, 1, 0);
            fields.Controls.Add(replaceLabel, 0, 1);
            fields.Controls.Add(replaceBox, 1, 1);

            var directionFlow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            directionFlow.Controls.Add(up);
            directionFlow.Controls.Add(down);
            direction.Controls.Add(directionFlow);

            var checks = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Dpi.Scale(new Padding(0, 8, 12, 0)) };
            checks.Controls.Add(matchCase);
            checks.Controls.Add(wrapAround);

            var options = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            options.Controls.Add(checks);
            options.Controls.Add(direction);

            var left = MakeTable(1);
            left.Controls.Add(fields);
            left.Controls.Add(options);

            var buttons = MakeButtonColumn();
            buttons.Controls.AddRange(new Control[] { findNext, replace, replaceAll, cancel });

            var root = MakeTable(2);
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(buttons, 1, 0);
            Controls.Add(root);

            AcceptButton = findNext;
            CancelButton = cancel;

            findNext.Click += (s, e) => host.FindNext(ReadOptions());
            replace.Click += (s, e) => host.Replace(ReadOptions());
            replaceAll.Click += (s, e) => host.ReplaceAll(ReadOptions());
            cancel.Click += (s, e) => Hide();
            findBox.TextChanged += (s, e) => UpdateButtons();
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            UpdateButtons();
        }

        /// <summary>打开查找（replaceMode=false）或替换对话框，并预填查找内容。</summary>
        public void Open(IWin32Window owner, bool replaceMode, string initialFind, FindOptions remembered)
        {
            Text = replaceMode ? "替换" : "查找";
            replaceLabel.Visible = replaceBox.Visible = replaceMode;
            replace.Visible = replaceAll.Visible = replaceMode;
            direction.Visible = !replaceMode;
            if (replaceMode) down.Checked = true;

            findBox.Text = initialFind.Length > 0 ? initialFind : remembered.Find;
            replaceBox.Text = remembered.Replace;
            matchCase.Checked = remembered.MatchCase;
            wrapAround.Checked = remembered.WrapAround;

            if (!Visible) Show(owner);
            Activate();
            findBox.Focus();
            findBox.SelectAll();
        }

        public FindOptions ReadOptions() => new FindOptions
        {
            Find = findBox.Text,
            Replace = replaceBox.Text,
            MatchCase = matchCase.Checked,
            WrapAround = wrapAround.Checked,
            Up = direction.Visible && up.Checked,
        };

        private void UpdateButtons()
        {
            bool any = findBox.TextLength > 0;
            findNext.Enabled = replace.Enabled = replaceAll.Enabled = any;
        }
    }
}
