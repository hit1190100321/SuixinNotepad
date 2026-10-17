using System.Drawing;
using System.Windows.Forms;

namespace SuixinNotepad.UI
{
    /// <summary>对话框的公共外观：系统对话框字体、固定边框、居中于父窗口。</summary>
    internal class DialogBase : Form
    {
        public DialogBase()
        {
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Dpi.Scale(new Padding(10));
        }

        protected static Button MakeButton(string text, DialogResult result = DialogResult.None)
        {
            return new Button
            {
                Text = text,
                DialogResult = result,
                AutoSize = true,
                MinimumSize = new Size(Dpi.Scale(88), 0),
                Margin = Dpi.Scale(new Padding(4, 3, 0, 3)),
            };
        }

        protected static Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = Dpi.Scale(new Padding(0, 6, 6, 6)),
            };
        }

        protected static TableLayoutPanel MakeTable(int columns)
        {
            return new TableLayoutPanel
            {
                ColumnCount = columns,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
            };
        }

        protected static FlowLayoutPanel MakeButtonColumn()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = Dpi.Scale(new Padding(8, 0, 0, 0)),
            };
        }

        protected static FlowLayoutPanel MakeButtonRow()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = Dpi.Scale(new Padding(0, 8, 0, 0)),
            };
        }
    }
}
