using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    public class TextFileTests
    {
        private const string Sample = "第一行 hello\n第二行\n";

        public static TheoryData<string> UnicodeEncodings()
        {
            var data = new TheoryData<string>();
            foreach (TextEncoding e in TextEncoding.All.Where(e => e.IsUnicode)) data.Add(e.Name);
            return data;
        }

        [Theory]
        [MemberData(nameof(UnicodeEncodings))]
        public void Unicode编码写出后能自动识别并原样读回(string name)
        {
            TextEncoding encoding = TextEncoding.FromName(name)!;
            byte[] bytes = TextFile.Encode(Sample, encoding, LineEnding.CrLf);
            DecodedText decoded = TextFile.Decode(bytes);

            Assert.Equal(Sample, decoded.Text);
            Assert.Same(encoding, decoded.Encoding);
            Assert.Equal(LineEnding.CrLf, decoded.LineEnding);
            Assert.True(decoded.Lossless);
        }

        [Fact]
        public void 每种编码按指定编码读回与写出一致()
        {
            foreach (TextEncoding encoding in TextEncoding.All)
            {
                const string text = "Hello, world 123\n";
                byte[] bytes = TextFile.Encode(text, encoding, LineEnding.Lf);
                DecodedText decoded = TextFile.Decode(bytes, encoding);
                Assert.Equal(text, decoded.Text);
                Assert.True(decoded.Lossless, encoding.Name);
            }
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
        public void 编码无法表示的字符会报错而不是变成问号()
        {
            Assert.Throws<UnrepresentableTextException>(() => TextFile.Encode("emoji 😀", TextEncoding.Gbk, LineEnding.CrLf));
            Assert.Throws<UnrepresentableTextException>(() => TextFile.Encode("中文", TextEncoding.Windows1252, LineEnding.CrLf));
            // GB18030 能表示全部 Unicode 字符
            Assert.Equal("emoji 😀", TextFile.Decode(TextFile.Encode("emoji 😀", TextEncoding.Gb18030, LineEnding.CrLf), TextEncoding.Gb18030).Text);
        }

        [Fact]
        public void 含无法识别字节的文件标记为有损()
        {
            byte[] gbk = TextEncoding.Gbk.Writer.GetBytes("中文内容");
            byte[] broken = gbk.Concat(new byte[] { 0xFF, 0x41 }).ToArray();

            DecodedText decoded = TextFile.Decode(broken, TextEncoding.Gb18030);
            Assert.False(decoded.Lossless);
            Assert.Contains('\uFFFD', decoded.Text);

            // UTF-8 文件按 Big5 打开
            Assert.False(TextFile.Decode(Encoding.UTF8.GetBytes("中文内容"), TextEncoding.Big5).Lossless);
        }

        [Fact]
        public void 文件本身含有替换字符时不算有损()
        {
            DecodedText decoded = TextFile.Decode(Encoding.UTF8.GetBytes("a\uFFFDb"));
            Assert.Same(TextEncoding.Utf8, decoded.Encoding);
            Assert.True(decoded.Lossless);
        }

        [Fact]
        public void 指定编码时BOM优先()
        {
            byte[] bytes = TextFile.Encode("中文", TextEncoding.Utf8Bom, LineEnding.CrLf);
            DecodedText decoded = TextFile.Decode(bytes, TextEncoding.Big5);
            Assert.Same(TextEncoding.Utf8Bom, decoded.Encoding);
            Assert.Equal("中文", decoded.Text);
        }

        [Theory]
        [InlineData("a\r\nb\nc", true)]
        [InlineData("a\r\nb\rc", true)]
        [InlineData("a\r\nb\r\nc", false)]
        [InlineData("abc", false)]
        public void 混用换行符被标记(string raw, bool mixed)
        {
            DecodedText decoded = TextFile.Decode(Encoding.UTF8.GetBytes(raw));
            Assert.Equal(mixed, decoded.MixedLineEndings);
        }

        [Fact]
        public void 超过大小上限的文件拒绝读取()
        {
            using (var temp = new TempDir())
            {
                string path = temp.File("big.log");
                using (var fs = new FileStream(path, FileMode.CreateNew)) fs.SetLength(TextFile.MaxFileSize + 1);
                Assert.Throws<FileTooLargeException>(() => TextFile.ReadBytes(path));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr security);

        [Fact]
        public void 写入有硬链接的文件时链接保持()
        {
            using (var temp = new TempDir())
            {
                string a = temp.File("a.txt");
                string b = temp.File("b.txt");
                File.WriteAllText(a, "old");
                Assert.True(CreateHardLink(b, a, IntPtr.Zero));
                Assert.True(FilePath.SameFile(a, b));

                TextFile.WriteAtomic(a, Encoding.UTF8.GetBytes("new"));

                Assert.Equal("new", File.ReadAllText(b));
                Assert.True(FilePath.SameFile(a, b));
                Assert.Equal(2, Directory.GetFiles(temp.Path).Length);
            }
        }

        [Fact]
        public void 不同写法的同一路径视为同一文件()
        {
            using (var temp = new TempDir())
            {
                string a = temp.File("Note.txt");
                File.WriteAllText(a, "x");
                Assert.True(FilePath.SameFile(a, temp.File("note.TXT")));
                Assert.True(FilePath.SameFile(a, Path.Combine(temp.Path, "sub", "..", "Note.txt")));
                Assert.False(FilePath.SameFile(a, temp.File("other.txt")));
            }
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
