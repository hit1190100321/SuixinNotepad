using System;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using SuixinNotepad.UI;
using Xunit;

namespace SuixinNotepad.Tests
{
    /// <summary>
    /// 验证编辑器对 RichEdit 的几个关键假设：纯文本模式、换行与字符下标对齐、多级撤销。
    /// RichEdit 需要在 STA 线程中创建。
    /// </summary>
    public class NoteEditorTests
    {
        private static void OnSta(Action<NoteEditor> test)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var form = new Form())
                    using (var editor = new NoteEditor { Dock = DockStyle.Fill })
                    {
                        form.Controls.Add(editor);
                        IntPtr unused = editor.Handle;
                        test(editor);
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        [Fact]
        public void 读写全文时换行统一为LF且长度与控件一致()
        {
            OnSta(editor =>
            {
                editor.SetText("第一行\n第二行\n\n末行");
                Assert.Equal("第一行\n第二行\n\n末行", editor.GetText());
                Assert.Equal(editor.GetText().Length, editor.TextLength);
                Assert.False(editor.CanUndo);
            });
        }

        [Fact]
        public void CRLF文本写入后同样按单字符换行处理()
        {
            OnSta(editor =>
            {
                editor.SetText("a\r\nb");
                Assert.Equal("a\nb", editor.GetText());
                Assert.Equal(3, editor.TextLength);
            });
        }

        [Fact]
        public void 光标行列按逻辑行计算()
        {
            OnSta(editor =>
            {
                editor.SetText("abc\nde\nfghij");
                editor.Select(9, 0);
                editor.GetCaretLineColumn(out int line, out int column);
                Assert.Equal(3, line);
                Assert.Equal(3, column);
                Assert.Equal("de", editor.GetTextRange(4, 6));
            });
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [Fact]
        public void 从右往左选中时光标在选区开头()
        {
            OnSta(editor =>
            {
                editor.SetText("abc\nde\nfghij");
                // EM_SETSEL：锚点在第 3 行第 3 列，光标停在第 2 行第 1 列
                SendMessage(editor.Handle, 0x00B1, (IntPtr)9, (IntPtr)4);
                editor.GetCaretLineColumn(out int line, out int column);
                Assert.Equal(2, line);
                Assert.Equal(1, column);

                editor.Select(4, 5);
                editor.GetCaretLineColumn(out line, out column);
                Assert.Equal(3, line);
                Assert.Equal(3, column);
            });
        }

        [Fact]
        public void 自动换行时行列仍按逻辑行计算()
        {
            OnSta(editor =>
            {
                editor.WrapLines = true;
                string longLine = new string('x', 2000);
                editor.SetText(longLine + "\nend");
                editor.Select(1500, 0);
                editor.GetCaretLineColumn(out int line, out int column);
                Assert.Equal(1, line);
                Assert.Equal(1501, column);

                editor.Select(editor.TextLength, 0);
                editor.GetCaretLineColumn(out line, out column);
                Assert.Equal(2, line);
                Assert.Equal(4, column);
            });
        }

        [Fact]
        public void 垂直制表符和替换字符原样保存()
        {
            OnSta(editor =>
            {
                const string text = "a\vb\uFFFDc\td\n\u00A0e";
                Assert.True(NoteEditor.CanHold(text));
                editor.SetText(text);
                Assert.Equal(text, editor.GetText());
                Assert.Equal(text, editor.GetTextRange(0, editor.TextLength));
                Assert.Equal(text.Length, editor.TextLength);
            });
        }

        [Theory]
        [InlineData("\u2028")]
        [InlineData("\u2029")]
        [InlineData("\uFFFC")]
        public void 编辑器会改写的字符能被识别(string special)
        {
            OnSta(editor =>
            {
                string text = "a" + special + "b";
                Assert.False(NoteEditor.CanHold(text));
                // 前提假设：写入控件后确实会被改写，否则就不必只读打开这类文件
                editor.SetText(text);
                Assert.NotEqual(text, editor.GetText());
            });
        }

        [Fact]
        public void 支持多级撤销与重做()
        {
            OnSta(editor =>
            {
                editor.SetText("");
                editor.ReplaceSelection("一");
                editor.Select(editor.TextLength, 0);
                editor.ReplaceSelection("\n二");
                editor.Undo();
                Assert.Equal("一", editor.GetText());
                editor.Undo();
                Assert.Equal("", editor.GetText());
                editor.Redo();
                Assert.Equal("一", editor.GetText());
            });
        }

        [Fact]
        public void 整体替换可以撤销()
        {
            OnSta(editor =>
            {
                editor.SetText("原文");
                editor.ReplaceAllUndoable("新内容\n两行");
                Assert.Equal("新内容\n两行", editor.GetText());
                editor.Undo();
                Assert.Equal("原文", editor.GetText());
            });
        }

        [Fact]
        public void 以RTF开头的内容按纯文本保存()
        {
            OnSta(editor =>
            {
                const string rtf = @"{\rtf1\ansi hello}";
                editor.SetText(rtf);
                Assert.Equal(rtf, editor.GetText());
            });
        }

        private static string FontAt(NoteEditor editor, int index)
        {
            editor.Select(index, 1);
            return editor.SelectionFont?.Name ?? "(混合)";
        }

        [Fact]
        public void 所选字体不含中文时中文使用微软雅黑_拉丁字符使用所选字体()
        {
            OnSta(editor =>
            {
                editor.Font = new Font("Consolas", 11f);
                editor.SetText("abc中文");
                Assert.Equal("Consolas", FontAt(editor, 0));
                Assert.Equal(NoteEditor.DefaultCjkFace, FontAt(editor, 3));

                editor.Select(editor.TextLength, 0);
                editor.ReplaceSelection("汉");
                Assert.Equal(NoteEditor.DefaultCjkFace, FontAt(editor, editor.TextLength - 1));
            });
        }

        [Fact]
        public void 中文字体随所选字体切换且可切回()
        {
            OnSta(editor =>
            {
                editor.Font = new Font("Consolas", 11f);
                editor.SetText("abc中文");

                editor.Font = new Font("KaiTi", 12f);
                Assert.Equal("楷体", editor.CjkFace);
                Assert.Equal("楷体", FontAt(editor, 3));

                editor.Font = new Font("Lucida Console", 12f);
                Assert.Equal("Lucida Console", FontAt(editor, 0));
                Assert.Equal(NoteEditor.DefaultCjkFace, FontAt(editor, 3));
            });
        }
    }
}
