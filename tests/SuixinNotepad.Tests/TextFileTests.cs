using System.IO;
using System.Text;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    public class TextFileTests
    {
        private const string Sample = "第一行 hello\n第二行\n";

        [Theory]
        [InlineData((int)FileEncoding.Utf8)]
        [InlineData((int)FileEncoding.Utf8Bom)]
        [InlineData((int)FileEncoding.Utf16LE)]
        [InlineData((int)FileEncoding.Utf16BE)]
        [InlineData((int)FileEncoding.Ansi)]
        public void 各编码写出后能识别并原样读回(int encodingValue)
        {
            var encoding = (FileEncoding)encodingValue;
            byte[] bytes = TextFile.Encode(Sample, encoding, LineEnding.CrLf);
            DecodedText decoded = TextFile.Decode(bytes);

            Assert.Equal(Sample, decoded.Text);
            Assert.Equal(LineEnding.CrLf, decoded.LineEnding);
            // 系统 ANSI 代码页为 UTF-8 时，ANSI 文件与无 BOM 的 UTF-8 无法区分
            if (encoding == FileEncoding.Ansi && Encoding.Default.CodePage == 65001)
                Assert.Equal(FileEncoding.Utf8, decoded.Encoding);
            else
                Assert.Equal(encoding, decoded.Encoding);
        }

        [Theory]
        [InlineData((int)LineEnding.CrLf, "a\r\nb")]
        [InlineData((int)LineEnding.Lf, "a\nb")]
        [InlineData((int)LineEnding.Cr, "a\rb")]
        public void 换行符被识别并在保存时保持(int lineEndingValue, string raw)
        {
            var lineEnding = (LineEnding)lineEndingValue;
            DecodedText decoded = TextFile.Decode(Encoding.UTF8.GetBytes(raw));
            Assert.Equal("a\nb", decoded.Text);
            Assert.Equal(lineEnding, decoded.LineEnding);
            Assert.Equal(raw, Encoding.UTF8.GetString(TextFile.Encode(decoded.Text, decoded.Encoding, decoded.LineEnding)));
        }

        [Fact]
        public void ANSI无法表示的字符会报错而不是变成问号()
        {
            if (Encoding.Default.CodePage == 65001) return;
            Assert.Throws<AnsiUnrepresentableException>(() => TextFile.Encode("emoji 😀", FileEncoding.Ansi, LineEnding.CrLf));
        }

        [Fact]
        public void 原子写入覆盖已有文件且不留临时文件()
        {
            using (var temp = new TempDir())
            {
                string path = temp.File("a.txt");
                File.WriteAllText(path, "old");
                TextFile.WriteAtomic(path, Encoding.UTF8.GetBytes("new"));
                TextFile.WriteAtomic(temp.File("b.txt"), Encoding.UTF8.GetBytes("fresh"));

                Assert.Equal("new", File.ReadAllText(path));
                Assert.Equal("fresh", File.ReadAllText(temp.File("b.txt")));
                Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
            }
        }
    }
}
