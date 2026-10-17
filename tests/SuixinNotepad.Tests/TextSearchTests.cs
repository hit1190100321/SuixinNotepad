using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    public class TextSearchTests
    {
        private const string Text = "abc ABC abc";

        [Fact]
        public void 向下查找从选区末尾开始_可循环()
        {
            var o = new FindOptions { Find = "abc", MatchCase = true, WrapAround = true };
            Assert.Equal(8, TextSearch.FindNext(Text, o, 0, 3));
            Assert.Equal(0, TextSearch.FindNext(Text, o, 8, 3));
            o.WrapAround = false;
            Assert.Equal(-1, TextSearch.FindNext(Text, o, 8, 3));
        }

        [Fact]
        public void 向上查找从选区开头之前开始()
        {
            var o = new FindOptions { Find = "abc", MatchCase = false, Up = true, WrapAround = false };
            Assert.Equal(4, TextSearch.FindNext(Text, o, 8, 3));
            Assert.Equal(0, TextSearch.FindNext(Text, o, 4, 3));
            Assert.Equal(-1, TextSearch.FindNext(Text, o, 0, 3));
            o.WrapAround = true;
            Assert.Equal(8, TextSearch.FindNext(Text, o, 0, 3));
        }

        [Fact]
        public void 全部替换区分大小写并返回次数()
        {
            string result = TextSearch.ReplaceAll(Text, new FindOptions { Find = "abc", Replace = "x", MatchCase = true }, out int count);
            Assert.Equal("x ABC x", result);
            Assert.Equal(2, count);
            TextSearch.ReplaceAll(Text, new FindOptions { Find = "abc", Replace = "x" }, out count);
            Assert.Equal(3, count);
        }

        [Fact]
        public void 行首位置()
        {
            Assert.Equal(0, TextSearch.LineStart("a\nbb\nc", 1));
            Assert.Equal(5, TextSearch.LineStart("a\nbb\nc", 3));
            Assert.Equal(-1, TextSearch.LineStart("a\nbb\nc", 4));
        }
    }
}
