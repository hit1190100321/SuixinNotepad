using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    /// <summary>草稿在首行写完时自动命名：何时触发、生成的文件名是否在 Windows 上可用。</summary>
    public class NoteNamingTests
    {
        [Theory]
        [InlineData("会议记录", null)]
        [InlineData("", null)]
        [InlineData("\n\n  \n", null)]
        [InlineData("会议记录\n", "会议记录")]
        [InlineData("  会议记录  \n第二行", "会议记录")]
        [InlineData("\n  \n购物清单\n牛奶", "购物清单")]
        [InlineData("\n\n还没写完", null)]
        public void 第一个非空行写完后才命名(string text, string? expected)
        {
            Assert.Equal(expected, NoteNaming.CompletedFirstLine(text));
        }

        [Theory]
        [InlineData("2026/10/24 周会", "2026_10_24 周会")]
        [InlineData("a:b*c?d\"e<f>g|h\\i", "a_b_c_d_e_f_g_h_i")]
        [InlineData("结尾的点...", "结尾的点")]
        [InlineData("...", "无标题")]
        [InlineData("   ", "无标题")]
        [InlineData("CON", "_CON")]
        [InlineData("nul.备注", "_nul.备注")]
        [InlineData("COM10", "COM10")]
        [InlineData("Console", "Console")]
        public void 生成可用的文件名(string line, string expected)
        {
            Assert.Equal(expected, NoteNaming.ToFileStem(line));
        }

        [Fact]
        public void 过长的首行截断且不拆开代理对()
        {
            string longLine = new string('字', NoteNaming.MaxStemLength + 20);
            Assert.Equal(NoteNaming.MaxStemLength, NoteNaming.ToFileStem(longLine).Length);

            // 第 50 个字符是代理对的前半，截断时整个字符都不要
            string emoji = new string('a', NoteNaming.MaxStemLength - 1) + "😀尾";
            string stem = NoteNaming.ToFileStem(emoji);
            Assert.Equal(new string('a', NoteNaming.MaxStemLength - 1), stem);
        }
    }
}
