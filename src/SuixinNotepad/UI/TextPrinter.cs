using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace SuixinNotepad.UI
{
    /// <summary>打印纯文本：自动折行，页眉为文件名，页脚为页码。</summary>
    internal sealed class TextPrinter
    {
        private readonly PageSettings pageSettings = new PageSettings { Margins = new Margins(75, 75, 100, 100) };

        public void PageSetup(IWin32Window owner)
        {
            using (var dialog = new PageSetupDialog { PageSettings = pageSettings, EnableMetric = true })
            {
                dialog.ShowDialog(owner);
            }
        }

        public void Print(IWin32Window owner, string text, Font font, string title)
        {
            using (var document = new PrintDocument { DocumentName = title, DefaultPageSettings = (PageSettings)pageSettings.Clone() })
            using (var dialog = new PrintDialog { Document = document, UseEXDialog = true, AllowSomePages = false })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;

                string remaining = text.Replace("\n", "\r\n");
                int page = 0;
                using (var format = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word })
                using (var small = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f))
                using (var header = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath })
                using (var footer = new StringFormat { Alignment = StringAlignment.Center })
                {
                    format.FormatFlags |= StringFormatFlags.LineLimit | StringFormatFlags.MeasureTrailingSpaces;
                    format.SetTabStops(0, new[] { 4 * MeasureSpace(font) });

                    document.PrintPage += (s, e) =>
                    {
                        page++;
                        Graphics g = e.Graphics!;
                        Rectangle bounds = e.MarginBounds;
                        float headerHeight = small.GetHeight(g);

                        g.DrawString(title, small, Brushes.Black, new RectangleF(bounds.Left, bounds.Top - headerHeight * 2, bounds.Width, headerHeight), header);
                        g.DrawString("第 " + page + " 页", small, Brushes.Black, new RectangleF(bounds.Left, bounds.Bottom + headerHeight, bounds.Width, headerHeight), footer);

                        g.MeasureString(remaining, font, bounds.Size, format, out int chars, out _);
                        g.DrawString(remaining.Substring(0, chars), font, Brushes.Black, bounds, format);
                        remaining = remaining.Substring(chars);
                        e.HasMorePages = remaining.Length > 0 && chars > 0;
                    };
                    document.Print();
                }
            }
        }

        private static float MeasureSpace(Font font)
        {
            using (var bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                return g.MeasureString(" ", font, PointF.Empty, StringFormat.GenericTypographic).Width;
            }
        }
    }
}
