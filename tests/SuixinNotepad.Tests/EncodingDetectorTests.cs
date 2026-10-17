using System.Linq;
using System.Text;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    public class EncodingDetectorTests
    {
        private const string Simplified = "随心记事本是一个多标签页的纯文本编辑器，支持自动保存和存档历史。今天天气很好，我们去公园散步吧。";
        private const string Traditional = "隨心記事本是一個多標籤頁的純文字編輯器，支援自動儲存與存檔歷史。今天天氣很好，我們去公園散步吧。";
        private const string Japanese = "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所でニャーニャー泣いていた事だけは記憶している。";
        private const string Korean = "대한민국은 민주공화국이다. 대한민국의 주권은 국민에게 있고, 모든 권력은 국민으로부터 나온다. 오늘 날씨가 정말 좋네요.";
        private const string Western = "Le café est très chaud. Die Größe der Straße ist schön. ¿Dónde está el niño? Olá, coração.";

        public static TheoryData<string, string> Samples() => new TheoryData<string, string>
        {
            { Simplified, "GB18030" },
            { "第一章\r\n\r\n　　话说天下大势，分久必合，合久必分。", "GB18030" },
            { "张三 李四 王五 赵六 钱七 孙八", "GB18030" },
            { "我用 Windows 系统，打开 Word 文档，然后保存到 D 盘的 Notes 文件夹里。", "GB18030" },
            { "安装 Git 之后 运行 git init 命令 就能 建立 仓库", "GB18030" },
            { "안녕하세요. 만나서 반갑습니다. 오늘은 날씨가 좋네요.", "EUC-KR" },
            { Traditional, "Big5" },
            { Japanese, "Shift-JIS" },
            { Korean, "EUC-KR" },
            { Western, "Windows-1252" },
        };

        [Theory]
        [MemberData(nameof(Samples))]
        public void 简体中文系统上识别各地常用编码(string text, string expected)
        {
            byte[] bytes = TextEncoding.FromName(expected)!.Writer.GetBytes(text);
            Assert.Equal(expected, EncodingDetector.Detect(bytes, TextEncoding.Gb18030).Name);
        }

        [Theory]
        [InlineData(Simplified, "GB18030")]
        [InlineData(Traditional, "Big5")]
        [InlineData(Japanese, "Shift-JIS")]
        [InlineData(Korean, "EUC-KR")]
        [InlineData(Western, "Windows-1252")]
        public void 西欧系统上同样识别中日韩编码(string text, string expected)
        {
            byte[] bytes = TextEncoding.FromName(expected)!.Writer.GetBytes(text);
            Assert.Equal(expected, EncodingDetector.Detect(bytes, TextEncoding.Windows1252).Name);
        }

        [Fact]
        public void 繁体中文系统上识别韩文()
        {
            Assert.Same(TextEncoding.EucKr, EncodingDetector.Detect(TextEncoding.EucKr.Writer.GetBytes(Korean), TextEncoding.Big5));
            Assert.Same(TextEncoding.Big5, EncodingDetector.Detect(TextEncoding.Big5.Writer.GetBytes(Traditional), TextEncoding.Big5));
        }

        [Fact]
        public void 很短的中文按系统默认编码理解()
        {
            Assert.Same(TextEncoding.Gb18030, EncodingDetector.Detect(TextEncoding.Gbk.Writer.GetBytes("你好"), TextEncoding.Gb18030));
            Assert.Same(TextEncoding.Big5, EncodingDetector.Detect(TextEncoding.Big5.Writer.GetBytes("你好"), TextEncoding.Big5));
        }

        [Fact]
        public void GBK特有的欧元符号退回GBK()
        {
            // 0x80 在 GBK（代码页 936）中是欧元符号，在 GB18030 中不合法
            byte[] bytes = TextEncoding.Gbk.Writer.GetBytes("价格100€，含税");
            Assert.Same(TextEncoding.Gbk, EncodingDetector.Detect(bytes, TextEncoding.Gb18030));
        }

        [Fact]
        public void UTF8优先于旧编码()
        {
            Assert.Same(TextEncoding.Utf8, EncodingDetector.Detect(Encoding.UTF8.GetBytes(Simplified), TextEncoding.Gb18030));
        }

        [Theory]
        [InlineData(1200, "UTF-16 LE（无 BOM）")]
        [InlineData(1201, "UTF-16 BE（无 BOM）")]
        public void 识别无BOM的UTF16且保存时仍不加BOM(int codePage, string expected)
        {
            byte[] bytes = Encoding.GetEncoding(codePage).GetBytes("hello 世界\r\nsecond line\r\n");
            DecodedText decoded = TextFile.Decode(bytes);
            Assert.Equal(expected, decoded.Encoding.Name);
            Assert.Equal(bytes, TextFile.Encode(decoded.Text, decoded.Encoding, decoded.LineEnding));
        }

        [Fact]
        public void 很长的单行中文识别耗时随长度线性增长()
        {
            // 曾经每个字符都要重新扫描所在的整段非 ASCII 字符，80 KB 单行要 1.5 秒
            EncodingDetector.Detect(Encoding.GetEncoding(936).GetBytes(Simplified), TextEncoding.Gb18030);
            var sb = new StringBuilder();
            while (sb.Length < 40000) sb.Append("中华人民共和国国家标准信息交换用汉字编码字符集");
            byte[] bytes = Encoding.GetEncoding(936).GetBytes(sb.ToString());

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.Same(TextEncoding.Gb18030, EncodingDetector.Detect(bytes, TextEncoding.Gb18030));
            Assert.True(watch.ElapsedMilliseconds < 300, "识别耗时 " + watch.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void 含NUL的二进制文件被拒绝()
        {
            // zip（epub、docx 都是 zip）文件头
            byte[] zip = { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x8A, 0x5B, 0x51, 0x59, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02, 0x03 };
            Assert.Throws<NotTextFileException>(() => EncodingDetector.Detect(zip, TextEncoding.Gb18030));

            byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
            Assert.Throws<NotTextFileException>(() => EncodingDetector.Detect(png, TextEncoding.Gb18030));
        }

        [Fact]
        public void 所有旧编码都无法完整解码时退回系统默认编码()
        {
            byte[] bytes = TextEncoding.Gbk.Writer.GetBytes("中文").Concat(new byte[] { 0x81, 0x20 }).ToArray();
            DecodedText decoded = TextFile.Decode(bytes);
            Assert.Same(TextEncoding.SystemLegacy, decoded.Encoding);
            Assert.False(decoded.Lossless);
        }
    }
}
