using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SuixinNotepad.UI
{
    internal sealed class TabIndexEventArgs : EventArgs
    {
        public TabIndexEventArgs(int index, Point location)
        {
            Index = index;
            Location = location;
        }

        public int Index { get; }
        public Point Location { get; }
    }

    internal sealed class TabMovedEventArgs : EventArgs
    {
        public TabMovedEventArgs(int from, int to)
        {
            From = from;
            To = to;
        }

        public int From { get; }
        public int To { get; }
    }

    /// <summary>自绘标签栏：关闭按钮、中键关闭、拖动排序、滚轮横向滚动、“+”新建。</summary>
    internal sealed class TabStrip : Control
    {
        public sealed class Tab
        {
            public string Text { get; set; } = "";
            public string ToolTip { get; set; } = "";

            /// <summary>保存失败时以红色标题提示。</summary>
            public bool Error { get; set; }
        }

        private static readonly Color BarColor = Color.FromArgb(243, 243, 243);
        private static readonly Color SelectedColor = Color.White;
        private static readonly Color HoverColor = Color.FromArgb(232, 232, 232);
        private static readonly Color BorderColor = Color.FromArgb(220, 220, 220);
        private static readonly Color TextColor = Color.FromArgb(32, 32, 32);
        private static readonly Color ErrorColor = Color.FromArgb(196, 43, 28);

        private readonly List<Tab> tabs = new List<Tab>();
        private readonly List<Rectangle> tabRects = new List<Rectangle>();
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 600, ReshowDelay = 200 };
        private Rectangle plusRect;
        private int selectedIndex = -1;
        private int hoverIndex = -1;
        private bool hoverClose;
        private bool hoverPlus;
        private int scrollOffset;
        private int maxScroll;
        private int dragIndex = -1;
        private Point dragStart;
        private bool dragging;
        private string currentToolTip = "";

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = BarColor;
            Height = Dpi.Scale(34);
        }

        public event EventHandler? SelectedIndexChanged;
        public event EventHandler<TabIndexEventArgs>? CloseRequested;
        public event EventHandler? NewTabRequested;
        public event EventHandler<TabMovedEventArgs>? TabMoved;
        public event EventHandler<TabIndexEventArgs>? TabRightClicked;

        public int Count => tabs.Count;

        public Tab this[int index] => tabs[index];

        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                if (value < -1 || value >= tabs.Count) throw new ArgumentOutOfRangeException(nameof(value));
                if (selectedIndex == value) return;
                selectedIndex = value;
                LayoutTabs();
                EnsureVisible(value);
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Insert(int index, Tab tab)
        {
            tabs.Insert(index, tab);
            if (selectedIndex >= index) selectedIndex++;
            RefreshTabs();
        }

        public void RemoveAt(int index)
        {
            tabs.RemoveAt(index);
            if (selectedIndex > index || selectedIndex >= tabs.Count) selectedIndex--;
            hoverIndex = -1;
            RefreshTabs();
        }

        /// <summary>修改标签内容后调用以重绘。</summary>
        public void RefreshTabs()
        {
            LayoutTabs();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutTabs();
            EnsureVisible(selectedIndex);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RefreshTabs();
        }

        private void LayoutTabs()
        {
            tabRects.Clear();
            int left = Dpi.Scale(6);
            int top = Dpi.Scale(5);
            int height = Height - top;
            int plusWidth = Dpi.Scale(36);
            int available = Math.Max(0, Width - left - plusWidth - Dpi.Scale(6));
            int minWidth = Dpi.Scale(96);
            int maxWidth = Dpi.Scale(240);

            var widths = new int[tabs.Count];
            int total = 0;
            for (int i = 0; i < tabs.Count; i++)
            {
                int textWidth = TextRenderer.MeasureText(tabs[i].Text, Font).Width;
                widths[i] = Math.Max(minWidth, Math.Min(maxWidth, textWidth + Dpi.Scale(52)));
                total += widths[i];
            }

            // 放不下时先等比压缩到最小宽度，仍放不下再横向滚动
            if (total > available && tabs.Count > 0)
            {
                int each = Math.Max(minWidth, available / tabs.Count);
                total = 0;
                for (int i = 0; i < widths.Length; i++)
                {
                    widths[i] = Math.Min(widths[i], each);
                    total += widths[i];
                }
            }

            maxScroll = Math.Max(0, total - available);
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, maxScroll));

            int x = left - scrollOffset;
            for (int i = 0; i < tabs.Count; i++)
            {
                tabRects.Add(new Rectangle(x, top, widths[i], height));
                x += widths[i];
            }

            int plusX = Math.Min(x, left + available) + Dpi.Scale(4);
            plusRect = new Rectangle(plusX, top + Dpi.Scale(3), Dpi.Scale(28), height - Dpi.Scale(8));
        }

        private void EnsureVisible(int index)
        {
            if (index < 0 || index >= tabRects.Count) return;
            int left = Dpi.Scale(6);
            int right = plusRect.Left - Dpi.Scale(4);
            Rectangle r = tabRects[index];
            if (r.Left < left) scrollOffset -= left - r.Left;
            else if (r.Right > right) scrollOffset += r.Right - right;
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, maxScroll));
            LayoutTabs();
            Invalidate();
        }

        private Rectangle CloseRect(Rectangle tab)
        {
            int size = Dpi.Scale(20);
            return new Rectangle(tab.Right - size - Dpi.Scale(8), tab.Top + (tab.Height - size) / 2, size, size);
        }

        private int HitTab(Point p)
        {
            int clipRight = plusRect.Left;
            for (int i = 0; i < tabRects.Count; i++)
            {
                if (tabRects[i].Contains(p) && p.X < clipRight) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BarColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int clipRight = plusRect.Left - Dpi.Scale(2);
            using (var border = new Pen(BorderColor))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.DrawLine(border, 0, Height - 1, Width, Height - 1);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                g.SetClip(new Rectangle(0, 0, Math.Max(0, clipRight), Height));
                for (int i = 0; i < tabs.Count; i++)
                {
                    if (i != selectedIndex) PaintTab(g, i, border);
                }
                if (selectedIndex >= 0) PaintTab(g, selectedIndex, border);
                g.ResetClip();
            }

            PaintPlus(g);
        }

        private void PaintTab(Graphics g, int i, Pen border)
        {
            Rectangle r = tabRects[i];
            bool selected = i == selectedIndex;
            bool hover = i == hoverIndex;

            if (selected || hover)
            {
                int radius = Dpi.Scale(6);
                using (GraphicsPath path = TopRoundedRect(new Rectangle(r.X + 1, r.Y, r.Width - 2, r.Height + 1), radius))
                using (var fill = new SolidBrush(selected ? SelectedColor : HoverColor))
                {
                    g.FillPath(fill, path);
                    if (selected) g.DrawPath(border, path);
                }
            }
            else if (i + 1 != selectedIndex && i < tabs.Count - 1)
            {
                // 未选中标签之间的分隔线
                int sepTop = r.Top + r.Height / 4;
                g.DrawLine(border, r.Right - 1, sepTop, r.Right - 1, r.Bottom - r.Height / 4);
            }

            Rectangle close = CloseRect(r);
            var textRect = new Rectangle(r.X + Dpi.Scale(12), r.Y, close.Left - r.X - Dpi.Scale(14), r.Height);
            Color color = tabs[i].Error ? ErrorColor : TextColor;
            TextRenderer.DrawText(g, tabs[i].Text, Font, textRect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            if (selected || hover)
            {
                if (hover && hoverClose)
                {
                    using (var b = new SolidBrush(Color.FromArgb(220, 220, 220)))
                        g.FillEllipse(b, close);
                }
                int pad = Dpi.Scale(6);
                using (var pen = new Pen(Color.FromArgb(80, 80, 80), Math.Max(1f, Dpi.Scale(1) * 1.2f)))
                {
                    g.DrawLine(pen, close.Left + pad, close.Top + pad, close.Right - pad, close.Bottom - pad);
                    g.DrawLine(pen, close.Right - pad, close.Top + pad, close.Left + pad, close.Bottom - pad);
                }
            }
        }

        private void PaintPlus(Graphics g)
        {
            if (hoverPlus)
            {
                using (GraphicsPath path = RoundedRect(plusRect, Dpi.Scale(5)))
                using (var b = new SolidBrush(HoverColor))
                    g.FillPath(b, path);
            }
            int cx = plusRect.Left + plusRect.Width / 2;
            int cy = plusRect.Top + plusRect.Height / 2;
            int arm = Dpi.Scale(6);
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), Math.Max(1f, Dpi.Scale(1) * 1.3f)))
            {
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            }
        }

        private static GraphicsPath TopRoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (dragIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && Math.Abs(e.X - dragStart.X) > SystemInformation.DragSize.Width) dragging = true;
                if (dragging)
                {
                    int target = HitTab(new Point(e.X, tabRects[dragIndex].Top + 1));
                    if (target >= 0 && target != dragIndex)
                    {
                        int from = dragIndex;
                        Tab tab = tabs[from];
                        tabs.RemoveAt(from);
                        tabs.Insert(target, tab);
                        if (selectedIndex == from) selectedIndex = target;
                        else if (from < selectedIndex && target >= selectedIndex) selectedIndex--;
                        else if (from > selectedIndex && target <= selectedIndex) selectedIndex++;
                        dragIndex = target;
                        LayoutTabs();
                        Invalidate();
                        TabMoved?.Invoke(this, new TabMovedEventArgs(from, target));
                    }
                    return;
                }
            }

            int hit = HitTab(e.Location);
            bool overClose = hit >= 0 && CloseRect(tabRects[hit]).Contains(e.Location);
            bool overPlus = plusRect.Contains(e.Location);
            if (hit != hoverIndex || overClose != hoverClose || overPlus != hoverPlus)
            {
                hoverIndex = hit;
                hoverClose = overClose;
                hoverPlus = overPlus;
                Invalidate();
            }

            string tip = hit >= 0 ? tabs[hit].ToolTip : overPlus ? "新建标签页 (Ctrl+N)" : "";
            if (tip != currentToolTip)
            {
                currentToolTip = tip;
                toolTip.SetToolTip(this, tip);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            hoverClose = false;
            hoverPlus = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = HitTab(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                if (plusRect.Contains(e.Location))
                {
                    NewTabRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }
                if (hit >= 0 && !CloseRect(tabRects[hit]).Contains(e.Location))
                {
                    SelectedIndex = hit;
                    dragIndex = hit;
                    dragStart = e.Location;
                    dragging = false;
                }
            }
            else if (e.Button == MouseButtons.Right && hit >= 0)
            {
                TabRightClicked?.Invoke(this, new TabIndexEventArgs(hit, e.Location));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int hit = HitTab(e.Location);
            if (e.Button == MouseButtons.Left && !dragging && hit >= 0 && CloseRect(tabRects[hit]).Contains(e.Location))
                CloseRequested?.Invoke(this, new TabIndexEventArgs(hit, e.Location));
            else if (e.Button == MouseButtons.Middle && hit >= 0)
                CloseRequested?.Invoke(this, new TabIndexEventArgs(hit, e.Location));
            dragIndex = -1;
            dragging = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && HitTab(e.Location) < 0 && !plusRect.Contains(e.Location))
                NewTabRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (maxScroll == 0) return;
            scrollOffset = Math.Max(0, Math.Min(maxScroll, scrollOffset - e.Delta / 2));
            LayoutTabs();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) toolTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
