using System;
using System.Windows.Forms;

namespace SuixinNotepad.UI
{
    /// <summary>单行输入对话框（转到行、重命名）。validate 返回错误信息，null 表示通过。</summary>
    internal sealed class InputDialog : DialogBase
    {
        private readonly TextBox input = new TextBox { Width = Dpi.Scale(280), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly Func<string, string?> validate;

        private InputDialog(string title, string label, string initial, string okText, Func<string, string?> validate)
        {
            this.validate = validate;
            Text = title;

            var table = MakeTable(1);
            table.Controls.Add(MakeLabel(label));
            input.Text = initial;
            table.Controls.Add(input);

            var ok = MakeButton(okText);
            var cancel = MakeButton("取消", DialogResult.Cancel);
            var buttons = MakeButtonRow();
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            table.Controls.Add(buttons);
            Controls.Add(table);

            AcceptButton = ok;
            CancelButton = cancel;
            ok.Click += (s, e) => Confirm();
        }

        public static string? Ask(IWin32Window owner, string title, string label, string initial, string okText,
            Func<string, string?> validate, int selectionLength = -1)
        {
            using (var dialog = new InputDialog(title, label, initial, okText, validate))
            {
                dialog.Shown += (s, e) =>
                {
                    dialog.input.Focus();
                    dialog.input.Select(0, selectionLength < 0 ? initial.Length : selectionLength);
                };
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.input.Text : null;
            }
        }

        private void Confirm()
        {
            string? error = validate(input.Text);
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                input.Focus();
                input.SelectAll();
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }
}
