using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SuixinNotepad.Core
{
    /// <summary>文本编码的语言类别，用于自动识别时判断解码结果是否像正常文字。</summary>
    internal enum EncodingFamily
    {
        Unicode,
        SimplifiedChinese,
        TraditionalChinese,
        Japanese,
        Korean,
        Latin,
        OtherSingleByte,
    }

    /// <summary>程序支持的一种文件编码。所有实例都在 All 中，按引用比较。</summary>
    internal sealed class TextEncoding
    {
        private TextEncoding(string name, string label, int codePage, bool bom, EncodingFamily family)
        {
            Name = name;
            Label = label;
            CodePage = codePage;
            Bom = bom;
            Family = family;

            if (IsUnicode)
            {
                // Unicode 编码能表示所有字符；文本里孤立的代理项按惯例替换为 U+FFFD
                Writer = Encoding.GetEncoding(codePage, new EncoderReplacementFallback("\uFFFD"), DecoderFallback.ExceptionFallback);
            }
            else
            {
                Writer = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            StrictReader = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            LenientReader = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, new DecoderReplacementFallback("\uFFFD"));
            Preamble = bom ? Writer.GetPreamble() : new byte[0];
        }

        /// <summary>状态栏显示和会话中保存的名称。</summary>
        public string Name { get; }

        /// <summary>编码菜单中显示的名称。</summary>
        public string Label { get; }

        public int CodePage { get; }

        /// <summary>写出时是否带 BOM。</summary>
        public bool Bom { get; }

        public EncodingFamily Family { get; }

        public bool IsUnicode => Family == EncodingFamily.Unicode;

        /// <summary>编码写出用；非 Unicode 编码遇到无法表示的字符时抛异常。</summary>
        public Encoding Writer { get; }

        /// <summary>遇到不合法的字节序列时抛异常，用于识别编码。</summary>
        public Encoding StrictReader { get; }

        /// <summary>不合法的字节序列替换为 U+FFFD，用于按指定编码打开。</summary>
        public Encoding LenientReader { get; }

        public byte[] Preamble { get; }

        public static readonly TextEncoding Utf8 = new TextEncoding("UTF-8", "UTF-8", 65001, false, EncodingFamily.Unicode);
        public static readonly TextEncoding Utf8Bom = new TextEncoding("带有 BOM 的 UTF-8", "带有 BOM 的 UTF-8", 65001, true, EncodingFamily.Unicode);
        public static readonly TextEncoding Utf16LE = new TextEncoding("UTF-16 LE", "UTF-16 LE", 1200, true, EncodingFamily.Unicode);
        public static readonly TextEncoding Utf16BE = new TextEncoding("UTF-16 BE", "UTF-16 BE", 1201, true, EncodingFamily.Unicode);
        public static readonly TextEncoding Utf32LE = new TextEncoding("UTF-32 LE", "UTF-32 LE", 12000, true, EncodingFamily.Unicode);
        public static readonly TextEncoding Utf32BE = new TextEncoding("UTF-32 BE", "UTF-32 BE", 12001, true, EncodingFamily.Unicode);

        /// <summary>自动识别出的无 BOM UTF-16 文件，保存时同样不写 BOM。不在菜单中列出。</summary>
        public static readonly TextEncoding Utf16LENoBom = new TextEncoding("UTF-16 LE（无 BOM）", "UTF-16 LE（无 BOM）", 1200, false, EncodingFamily.Unicode);
        public static readonly TextEncoding Utf16BENoBom = new TextEncoding("UTF-16 BE（无 BOM）", "UTF-16 BE（无 BOM）", 1201, false, EncodingFamily.Unicode);

        public static readonly TextEncoding Gb18030 = new TextEncoding("GB18030", "简体中文 (GB18030)", 54936, false, EncodingFamily.SimplifiedChinese);
        public static readonly TextEncoding Gbk = new TextEncoding("GBK", "简体中文 (GBK)", 936, false, EncodingFamily.SimplifiedChinese);
        public static readonly TextEncoding Big5 = new TextEncoding("Big5", "繁体中文 (Big5)", 950, false, EncodingFamily.TraditionalChinese);
        public static readonly TextEncoding ShiftJis = new TextEncoding("Shift-JIS", "日文 (Shift-JIS)", 932, false, EncodingFamily.Japanese);
        public static readonly TextEncoding EucJp = new TextEncoding("EUC-JP", "日文 (EUC-JP)", 51932, false, EncodingFamily.Japanese);
        public static readonly TextEncoding EucKr = new TextEncoding("EUC-KR", "韩文 (EUC-KR)", 949, false, EncodingFamily.Korean);

        public static readonly TextEncoding Windows1252 = new TextEncoding("Windows-1252", "西欧 (Windows-1252)", 1252, false, EncodingFamily.Latin);
        public static readonly TextEncoding Windows1250 = new TextEncoding("Windows-1250", "中欧 (Windows-1250)", 1250, false, EncodingFamily.Latin);
        public static readonly TextEncoding Windows1251 = new TextEncoding("Windows-1251", "西里尔文 (Windows-1251)", 1251, false, EncodingFamily.OtherSingleByte);
        public static readonly TextEncoding Koi8R = new TextEncoding("KOI8-R", "西里尔文 (KOI8-R)", 20866, false, EncodingFamily.OtherSingleByte);
        public static readonly TextEncoding Windows1253 = new TextEncoding("Windows-1253", "希腊文 (Windows-1253)", 1253, false, EncodingFamily.OtherSingleByte);
        public static readonly TextEncoding Windows1254 = new TextEncoding("Windows-1254", "土耳其文 (Windows-1254)", 1254, false, EncodingFamily.Latin);
        public static readonly TextEncoding Windows1255 = new TextEncoding("Windows-1255", "希伯来文 (Windows-1255)", 1255, false, EncodingFamily.OtherSingleByte);
        public static readonly TextEncoding Windows1256 = new TextEncoding("Windows-1256", "阿拉伯文 (Windows-1256)", 1256, false, EncodingFamily.OtherSingleByte);
        public static readonly TextEncoding Windows1257 = new TextEncoding("Windows-1257", "波罗的海文 (Windows-1257)", 1257, false, EncodingFamily.Latin);
        public static readonly TextEncoding Windows1258 = new TextEncoding("Windows-1258", "越南文 (Windows-1258)", 1258, false, EncodingFamily.Latin);
        public static readonly TextEncoding Windows874 = new TextEncoding("Windows-874", "泰文 (Windows-874)", 874, false, EncodingFamily.OtherSingleByte);

        /// <summary>菜单中的编码，按组排列；组之间在菜单里用分隔线隔开。</summary>
        public static readonly IReadOnlyList<IReadOnlyList<TextEncoding>> Groups = new[]
        {
            new[] { Utf8, Utf8Bom, Utf16LE, Utf16BE, Utf32LE, Utf32BE },
            new[] { Gb18030, Gbk, Big5 },
            new[] { ShiftJis, EucJp, EucKr },
            new[] { Windows1252, Windows1250, Windows1251, Koi8R, Windows1253, Windows1254, Windows1255, Windows1256, Windows1257, Windows1258, Windows874 },
        };

        /// <summary>全部编码：菜单中的编码，加上只由自动识别产生的编码。</summary>
        public static readonly IReadOnlyList<TextEncoding> All = Groups.SelectMany(g => g).Concat(new[] { Utf16LENoBom, Utf16BENoBom }).ToArray();

        /// <summary>按名称查找，找不到时返回 null（会话文件来自旧版本或被修改）。</summary>
        public static TextEncoding? FromName(string? name) =>
            string.IsNullOrEmpty(name) ? null : All.FirstOrDefault(e => e.Name == name);

        /// <summary>
        /// 没有 BOM 且不是 UTF-8 的文件优先按哪种编码理解：系统的 ANSI 代码页，
        /// 简体中文系统上用兼容 GBK 的 GB18030。系统代码页为 UTF-8 等不在表中的值时按界面语言选择。
        /// </summary>
        public static TextEncoding SystemLegacy { get; } = FindLegacy(Encoding.Default.CodePage)
            ?? FindLegacy(System.Globalization.CultureInfo.CurrentUICulture.TextInfo.ANSICodePage)
            ?? Windows1252;

        private static TextEncoding? FindLegacy(int codePage) =>
            codePage == Gbk.CodePage ? Gb18030 : All.FirstOrDefault(e => !e.IsUnicode && e.CodePage == codePage);

        public override string ToString() => Name;
    }
}
