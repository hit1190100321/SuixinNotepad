using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SuixinNotepad.UI
{
    /// <summary>
    /// 纯文本编辑器：使用 RichEdit 5.0（msftedit.dll，与 Win11 记事本相同）的纯文本模式，
    /// 支持多级撤销，粘贴时只取纯文本。
    /// 文本读写直接走 EM_GETTEXTEX / EM_REPLACESEL，不经过 .NET RichTextBox 的 RTF 逻辑。
    /// 中文使用 RichEdit 的双字体机制：所选字体含中文字形时中文跟随所选字体，否则中文使用微软雅黑。
    /// 控件内部的换行是单个 '\r'，对外统一为 '\n'，两者长度一致，因此字符下标可以直接对应。
    /// 注意：不要在句柄创建后修改 WordWrap、ScrollBars 等会触发句柄重建的属性，
    /// 重建会丢失纯文本模式与撤销记录。自动换行请用 WrapLines。
    /// </summary>
    internal sealed class NoteEditor : RichTextBox
    {
        public const int MinZoom = 10;
        public const int MaxZoom = 500;

        /// <summary>所选字体不含中文字形时，中文使用的字体。</summary>
        public const string DefaultCjkFace = "Microsoft YaHei UI";

        /// <summary>需要关联中文字体的字符集：GB2312、Big5。</summary>
        private static readonly byte[] CjkCharsets = { 134, 136 };

        private bool wrapLines = true;

        static NoteEditor()
        {
            if (LoadLibrary("msftedit.dll") == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载 msftedit.dll");
        }

        public NoteEditor()
        {
            BorderStyle = BorderStyle.None;
            DetectUrls = false;
            AcceptsTab = true;
            HideSelection = false;
            Multiline = true;
            WordWrap = false;
            ScrollBars = RichTextBoxScrollBars.Both;
            EnableAutoDragDrop = false;
            AllowDrop = true;
            ShortcutsEnabled = true;
            MaxLength = int.MaxValue;
        }

        /// <summary>用户通过 Ctrl+滚轮改变了缩放。</summary>
        public event EventHandler? ZoomChangedByUser;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassName = "RICHEDIT50W";
                return cp;
            }
        }

        public bool WrapLines
        {
            get => wrapLines;
            set
            {
                wrapLines = value;
                if (IsHandleCreated) ApplyWrap();
            }
        }

        public int ZoomPercent
        {
            get
            {
                int num = 0, den = 0;
                SendMessage(Handle, EM_GETZOOM, ref num, ref den);
                if (num == 0 || den == 0) return 100;
                return (int)Math.Round(num * 100.0 / den);
            }
            set
            {
                int p = Math.Max(MinZoom, Math.Min(MaxZoom, value));
                if (p == 100) SendMessage(Handle, EM_SETZOOM, IntPtr.Zero, IntPtr.Zero);
                else SendMessage(Handle, EM_SETZOOM, (IntPtr)p, (IntPtr)100);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            SendMessage(Handle, EM_SETTEXTMODE, (IntPtr)(TM_PLAINTEXT | TM_MULTILEVELUNDO | TM_MULTICODEPAGE), IntPtr.Zero);
            int mode = (int)SendMessage(Handle, EM_GETTEXTMODE, IntPtr.Zero, IntPtr.Zero);
            if ((mode & TM_PLAINTEXT) == 0 || (mode & TM_MULTILEVELUNDO) == 0)
                throw new InvalidOperationException("编辑器无法切换到纯文本模式。");

            // 自动字体绑定 + 双字体：中文字符使用关联的中文字体，其余字符使用所选字体
            int lang = (int)SendMessage(Handle, EM_GETLANGOPTIONS, IntPtr.Zero, IntPtr.Zero);
            lang &= ~IMF_AUTOKEYBOARD;
            lang |= IMF_AUTOFONT | IMF_DUALFONT;
            SendMessage(Handle, EM_SETLANGOPTIONS, IntPtr.Zero, (IntPtr)lang);

            SendMessage(Handle, EM_SETUNDOLIMIT, (IntPtr)1000, IntPtr.Zero);
            int margin = Dpi.Scale(6);
            SendMessage(Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), (IntPtr)(margin | (margin << 16)));
            ApplyWrap();
            // 文本模式切换后重新应用字体
            OnFontChanged(EventArgs.Empty);
        }

        /// <summary>中文字符当前使用的字体名。</summary>
        public string CjkFace { get; private set; } = DefaultCjkFace;

        protected override void OnFontChanged(EventArgs e)
        {
            if (IsHandleCreated)
            {
                CjkFace = HasCjkGlyphs(Font) ? Font.Name : DefaultCjkFace;
                foreach (byte charset in CjkCharsets)
                {
                    var assoc = new CHARFORMAT2
                    {
                        cbSize = Marshal.SizeOf(typeof(CHARFORMAT2)),
                        dwMask = CFM_FACE | CFM_CHARSET,
                        bCharSet = charset,
                        szFaceName = CjkFace,
                    };
                    SendMessage(Handle, EM_SETCHARFORMAT, (IntPtr)SCF_ASSOCIATEFONT, ref assoc);
                }

                // 双字体模式下设置不含中文的字体只作用于非中文字符，
                // 所以先用中文字体刷一遍全文，已有中文才会换成新的中文字体
                if (CjkFace != Font.Name)
                {
                    var all = new CHARFORMAT2
                    {
                        cbSize = Marshal.SizeOf(typeof(CHARFORMAT2)),
                        dwMask = CFM_FACE | CFM_CHARSET | CFM_SIZE,
                        bCharSet = DEFAULT_CHARSET,
                        yHeight = (int)Math.Round(Font.SizeInPoints * 20),
                        szFaceName = CjkFace,
                    };
                    SendMessage(Handle, EM_SETCHARFORMAT, (IntPtr)SCF_ALL, ref all);
                }
            }
            base.OnFontChanged(e);
        }

        /// <summary>字体本身是否包含常用汉字字形。</summary>
        private static bool HasCjkGlyphs(Font font)
        {
            using (var bitmap = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = g.GetHdc();
                IntPtr hfont = font.ToHfont();
                IntPtr old = SelectObject(hdc, hfont);
                try
                {
                    var glyphs = new ushort[2];
                    GetGlyphIndices(hdc, "中文", 2, glyphs, GGI_MARK_NONEXISTING_GLYPHS);
                    return glyphs[0] != 0xFFFF && glyphs[1] != 0xFFFF;
                }
                finally
                {
                    SelectObject(hdc, old);
                    DeleteObject(hfont);
                    g.ReleaseHdc(hdc);
                }
            }
        }

        /// <summary>取全文，换行为 '\n'。</summary>
        public string GetText()
        {
            var lengthEx = new GETTEXTLENGTHEX { flags = GTL_PRECISE | GTL_NUMCHARS, codepage = CP_UNICODE };
            int length = (int)SendMessage(Handle, EM_GETTEXTLENGTHEX, ref lengthEx, IntPtr.Zero);
            if (length <= 0) return string.Empty;

            int bytes = (length + 1) * 2;
            IntPtr buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                var getEx = new GETTEXTEX { cb = bytes, flags = GT_DEFAULT, codepage = CP_UNICODE };
                int copied = (int)SendMessage(Handle, EM_GETTEXTEX, ref getEx, buffer);
                return FromControl(Marshal.PtrToStringUni(buffer, copied));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>替换全文并清空撤销记录（用于打开文件）。</summary>
        public void SetText(string text)
        {
            SelectAll();
            SendMessage(Handle, EM_REPLACESEL, IntPtr.Zero, ToControl(text));
            SendMessage(Handle, EM_EMPTYUNDOBUFFER, IntPtr.Zero, IntPtr.Zero);
            Select(0, 0);
            Modified = false;
        }

        /// <summary>用新文本替换当前选区，可撤销。</summary>
        public void ReplaceSelection(string text)
        {
            SendMessage(Handle, EM_REPLACESEL, (IntPtr)1, ToControl(text));
        }

        /// <summary>替换全文，可撤销（用于全部替换、恢复备份）。</summary>
        public void ReplaceAllUndoable(string text)
        {
            SelectAll();
            ReplaceSelection(text);
        }

        /// <summary>取 [start, end) 范围的文本，换行为 '\n'。</summary>
        public string GetTextRange(int start, int end)
        {
            if (end <= start) return string.Empty;
            IntPtr buffer = Marshal.AllocHGlobal((end - start + 1) * 2);
            try
            {
                var range = new TEXTRANGE { cpMin = start, cpMax = end, lpstrText = buffer };
                int copied = (int)SendMessage(Handle, EM_GETTEXTRANGE, IntPtr.Zero, ref range);
                return FromControl(Marshal.PtrToStringUni(buffer, copied));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>光标所在的逻辑行与列（从 1 开始，不受自动换行影响）。</summary>
        public void GetCaretLineColumn(out int line, out int column)
        {
            int caret = SelectionStart;
            string before = GetTextRange(0, caret);
            line = 1;
            int lineStart = 0;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }
            column = caret - lineStart + 1;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (base.ProcessCmdKey(ref msg, keyData)) return true;
            return IsRichFormattingShortcut(keyData);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_MOUSEWHEEL && (ModifierKeys & Keys.Control) == Keys.Control)
            {
                int zoom = ZoomPercent;
                if (zoom < MinZoom || zoom > MaxZoom) ZoomPercent = zoom;
                ZoomChangedByUser?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// RichEdit 自带的对齐、行距、上下标、大小写切换等格式快捷键在纯文本编辑器里没有意义，
        /// 且会改变整篇文本的显示，全部屏蔽。只放行编辑相关的 Ctrl 组合键。
        /// </summary>
        private static bool IsRichFormattingShortcut(Keys keyData)
        {
            Keys modifiers = keyData & Keys.Modifiers;
            if ((modifiers & Keys.Control) == 0) return false;
            if ((modifiers & Keys.Alt) != 0) return false; // AltGr 输入字符
            bool shift = (modifiers & Keys.Shift) != 0;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Z:
                case Keys.Y:
                case Keys.X:
                case Keys.C:
                case Keys.V:
                case Keys.A:
                    return shift; // Ctrl+Shift+A 等是大小写切换之类的格式键
                case Keys.Insert:
                case Keys.Delete:
                case Keys.Back:
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.ControlKey:
                case Keys.ShiftKey:
                case Keys.Menu:
                    return false;
                default:
                    return true;
            }
        }

        private void ApplyWrap()
        {
            SendMessage(Handle, EM_SETTARGETDEVICE, IntPtr.Zero, (IntPtr)(wrapLines ? 0 : 1));
        }

        private static string FromControl(string text) => text.Replace('\r', '\n').Replace('\v', '\n');

        private static string ToControl(string text) => text.Replace("\r\n", "\r").Replace('\n', '\r');

        #region Win32

        private const int WM_USER = 0x0400;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int EM_REPLACESEL = 0x00C2;
        private const int EM_SETMARGINS = 0x00D3;
        private const int EC_LEFTMARGIN = 1;
        private const int EC_RIGHTMARGIN = 2;
        private const int EM_EMPTYUNDOBUFFER = 0x00CD;
        private const int EM_SETTARGETDEVICE = WM_USER + 72;
        private const int EM_GETTEXTRANGE = WM_USER + 75;
        private const int EM_SETUNDOLIMIT = WM_USER + 82;
        private const int EM_SETTEXTMODE = WM_USER + 89;
        private const int EM_GETTEXTMODE = WM_USER + 90;
        private const int EM_GETTEXTEX = WM_USER + 94;
        private const int EM_GETTEXTLENGTHEX = WM_USER + 95;
        private const int EM_SETLANGOPTIONS = WM_USER + 120;
        private const int EM_GETLANGOPTIONS = WM_USER + 121;
        private const int EM_GETZOOM = WM_USER + 224;
        private const int EM_SETZOOM = WM_USER + 225;

        private const int EM_SETCHARFORMAT = WM_USER + 68;
        private const int TM_PLAINTEXT = 1;
        private const int IMF_DUALFONT = 0x80;
        private const int SCF_ALL = 0x0004;
        private const int SCF_ASSOCIATEFONT = 0x0010;
        private const int CFM_SIZE = unchecked((int)0x80000000);
        private const int CFM_CHARSET = 0x08000000;
        private const int CFM_FACE = 0x20000000;
        private const byte DEFAULT_CHARSET = 1;
        private const int GGI_MARK_NONEXISTING_GLYPHS = 1;
        private const int TM_MULTILEVELUNDO = 8;
        private const int TM_MULTICODEPAGE = 32;
        private const int IMF_AUTOKEYBOARD = 0x0001;
        private const int IMF_AUTOFONT = 0x0002;
        private const int GTL_PRECISE = 2;
        private const int GTL_NUMCHARS = 8;
        private const int GT_DEFAULT = 0;
        private const int CP_UNICODE = 1200;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        private struct CHARFORMAT2
        {
            public int cbSize;
            public int dwMask;
            public int dwEffects;
            public int yHeight;
            public int yOffset;
            public int crTextColor;
            public byte bCharSet;
            public byte bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szFaceName;
            public short wWeight;
            public short sSpacing;
            public int crBackColor;
            public int lcid;
            public int dwCookie;
            public short sStyle;
            public short wKerning;
            public byte bUnderlineType;
            public byte bAnimation;
            public byte bRevAuthor;
            public byte bUnderlineColor;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CHARFORMAT2 lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct GETTEXTLENGTHEX
        {
            public int flags;
            public int codepage;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GETTEXTEX
        {
            public int cb;
            public int flags;
            public int codepage;
            public IntPtr lpDefaultChar;
            public IntPtr lpUsedDefChar;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TEXTRANGE
        {
            public int cpMin;
            public int cpMax;
            public IntPtr lpstrText;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetGlyphIndicesW")]
        private static extern int GetGlyphIndices(IntPtr hdc, string text, int count, [Out] ushort[] glyphs, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, ref int wParam, ref int lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, ref GETTEXTLENGTHEX wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, ref GETTEXTEX wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TEXTRANGE lParam);

        #endregion
    }
}
