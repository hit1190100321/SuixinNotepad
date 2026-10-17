using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SuixinNotepad.Core
{
    /// <summary>文件含有文本中不会出现的内容（如 NUL 字节），按二进制文件处理，拒绝打开。</summary>
    internal sealed class NotTextFileException : IOException
    {
        public NotTextFileException() : base("这不是文本文件。") { }
    }

    /// <summary>
    /// 识别文件编码。顺序：BOM → 含 NUL 时判断无 BOM 的 UTF-16，否则视为二进制 → 严格 UTF-8 →
    /// 各地常用的旧编码。旧编码中能完整解码的候选按“解出来像不像正常文字”打分，
    /// 系统默认编码优先，只有别的编码分数明显更高时才改用它。
    /// </summary>
    internal static class EncodingDetector
    {
        /// <summary>其他编码的分数要比系统默认编码高出这么多才会被采用，避免短文本上的误判。</summary>
        private const double PreferredMargin = 0.2;

        /// <summary>打分只看文件开头这么多个字符。</summary>
        private const int ScoreSampleLength = 64 * 1024;

        private static readonly TextEncoding[] LegacyCandidates =
        {
            TextEncoding.Gb18030, TextEncoding.Gbk, TextEncoding.Big5, TextEncoding.ShiftJis,
            TextEncoding.EucKr, TextEncoding.Windows1252,
        };

        /// <summary>按文件开头的 BOM 判断编码；没有 BOM 时返回 null。</summary>
        public static TextEncoding? DetectBom(byte[] bytes)
        {
            if (StartsWith(bytes, 0xEF, 0xBB, 0xBF)) return TextEncoding.Utf8Bom;
            // UTF-32 LE 的 BOM 以 UTF-16 LE 的 BOM 开头，先判断
            if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00) && bytes.Length % 4 == 0) return TextEncoding.Utf32LE;
            if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF)) return TextEncoding.Utf32BE;
            if (StartsWith(bytes, 0xFF, 0xFE)) return TextEncoding.Utf16LE;
            if (StartsWith(bytes, 0xFE, 0xFF)) return TextEncoding.Utf16BE;
            return null;
        }

        /// <summary>识别编码。文件不是文本时抛 NotTextFileException。</summary>
        public static TextEncoding Detect(byte[] bytes, TextEncoding preferredLegacy)
        {
            TextEncoding? bom = DetectBom(bytes);
            if (bom != null) return bom;

            if (Array.IndexOf(bytes, (byte)0) >= 0)
                return DetectUtf16WithoutBom(bytes) ?? throw new NotTextFileException();

            if (TryDecode(TextEncoding.Utf8, bytes, out _)) return TextEncoding.Utf8;
            return DetectLegacy(bytes, preferredLegacy);
        }

        /// <summary>
        /// 无 BOM 的 UTF-16：ASCII 字符的高位字节是 0，NUL 集中出现在奇数位（LE）或偶数位（BE）。
        /// 纯中文等不含 ASCII 的无 BOM UTF-16 看不出这个规律，只能由用户手动选择编码。
        /// </summary>
        private static TextEncoding? DetectUtf16WithoutBom(byte[] bytes)
        {
            if (bytes.Length % 2 != 0) return null;
            int even = 0, odd = 0;
            for (int i = 0; i < bytes.Length; i += 2)
            {
                if (bytes[i] == 0) even++;
                if (bytes[i + 1] == 0) odd++;
            }
            int units = bytes.Length / 2;
            TextEncoding? guess = null;
            if (odd >= units / 10 && even <= odd / 10) guess = TextEncoding.Utf16LE;
            else if (even >= units / 10 && odd <= even / 10) guess = TextEncoding.Utf16BE;
            if (guess == null || !TryDecode(guess, bytes, out string text)) return null;
            // 解出来仍有大量 NUL 或控制字符，说明不是 UTF-16 文本
            return text.Count(c => c < ' ' && c != '\t' && c != '\r' && c != '\n') > text.Length / 100 ? null : guess;
        }

        private static TextEncoding DetectLegacy(byte[] bytes, TextEncoding preferred)
        {
            var scores = new List<KeyValuePair<TextEncoding, double>>();
            foreach (TextEncoding candidate in new[] { preferred }.Concat(LegacyCandidates).Distinct())
            {
                if (TryDecode(candidate, bytes, out string text))
                    scores.Add(new KeyValuePair<TextEncoding, double>(candidate, Score(candidate, text)));
            }
            if (scores.Count == 0) return preferred;

            // 系统默认编码解不开时，别的编码也要解出大半正常文字才采用；否则仍按默认编码打开，由有损保护兜底
            double preferredScore = scores.Where(s => s.Key == preferred).Select(s => s.Value).DefaultIfEmpty(0.5 - PreferredMargin).First();
            // 分数相同时取候选列表中靠前的
            KeyValuePair<TextEncoding, double> best = scores.Aggregate((a, b) => b.Value > a.Value ? b : a);
            return best.Value >= preferredScore + PreferredMargin ? best.Key : preferred;
        }

        private static bool TryDecode(TextEncoding encoding, byte[] bytes, out string text)
        {
            try
            {
                text = encoding.StrictReader.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = string.Empty;
                return false;
            }
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (bytes[i] != prefix[i]) return false;
            return true;
        }

        #region 打分

        /// <summary>非 ASCII 字符中，属于该编码对应语言常用字符的比例（0 到 1）。</summary>
        internal static double Score(TextEncoding encoding, string text)
        {
            if (text.Length > ScoreSampleLength) text = text.Substring(0, ScoreSampleLength);
            HashSet<char>? koreanShadow =
                encoding.Family == EncodingFamily.SimplifiedChinese ? GbKoreanShadow.Value :
                encoding.Family == EncodingFamily.TraditionalChinese ? Big5KoreanShadow.Value : null;
            int nonAscii = 0, good = 0, spaces = 0, kana = 0, hangul = 0, ideographs = 0, inShadow = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ') spaces++;
                if (c < 0x80) continue;

                nonAscii++;
                if (IsKana(c)) kana++;
                if (IsHangul(c)) hangul++;
                if (IsIdeograph(c))
                {
                    ideographs++;
                    if (koreanShadow != null && koreanShadow.Contains(c)) inShadow++;
                }
                if (IsTypical(encoding.Family, c, text, i)) good++;
            }
            if (nonAscii == 0) return 1;
            double score = (double)good / nonAscii;

            switch (encoding.Family)
            {
                case EncodingFamily.SimplifiedChinese:
                case EncodingFamily.TraditionalChinese:
                    // 韩文常用音节在 EUC-KR 中只占 B0A1–C8FE，按中文解码时汉字全部落在这一段；
                    // 正常中文只有一半多一点的字在这一段，连续多个字都落在里面几乎不可能
                    if (ideographs >= 8 && inShadow * 100 >= ideographs * 97) score *= 0.5;
                    break;
                case EncodingFamily.Japanese:
                    // 日文必然夹杂假名
                    if (kana * 10 < nonAscii) score = 0;
                    break;
                case EncodingFamily.Korean:
                    // 韩文用空格分词
                    if (hangul == 0 || spaces * 10 < hangul) score = 0;
                    break;
            }
            return score;
        }

        private static bool IsTypical(EncodingFamily family, char c, string text, int index)
        {
            switch (family)
            {
                case EncodingFamily.SimplifiedChinese: return Gb2312Hanzi.Value.Contains(c) || IsCjkPunctuation(c);
                case EncodingFamily.TraditionalChinese: return Big5CommonHanzi.Value.Contains(c) || IsCjkPunctuation(c);
                case EncodingFamily.Japanese: return IsKana(c) || JisLevel1Kanji.Value.Contains(c) || IsCjkPunctuation(c);
                case EncodingFamily.Korean: return IsHangul(c) || IsCjkPunctuation(c);
                case EncodingFamily.Latin:
                    // 拉丁字母文字里带音调的字母零散出现在 ASCII 字母之间；连续一长串说明是别的文字被误解码
                    return (char.IsLetter(c) || char.IsPunctuation(c) || char.IsSymbol(c) || c == '\u00A0') && NonAsciiRunLength(text, index) <= 3;
                case EncodingFamily.OtherSingleByte: return char.IsLetter(c) || char.IsPunctuation(c);
                default: return true;
            }
        }

        private static int NonAsciiRunLength(string text, int index)
        {
            int start = index, end = index;
            while (start > 0 && text[start - 1] >= 0x80) start--;
            while (end + 1 < text.Length && text[end + 1] >= 0x80) end++;
            return end - start + 1;
        }

        private static bool IsKana(char c) => c >= '\u3040' && c <= '\u30FF';

        private static bool IsHangul(char c) => c >= '\uAC00' && c <= '\uD7A3';

        private static bool IsIdeograph(char c) => c >= '\u4E00' && c <= '\u9FFF';

        private static bool IsCjkPunctuation(char c) =>
            (c >= '\u3000' && c <= '\u303F') || (c >= '\uFF01' && c <= '\uFF5E') ||
            (c >= '\u2010' && c <= '\u2027') || c == '\u00B7' || c == '\u00D7' || c == '\u2030' || c == '\u203B';

        // 各语言的常用汉字集合，从对应编码表中常用字所在的区段解码得到

        /// <summary>GB2312 收录的 6763 个汉字（区段 B0A1–F7FE）。</summary>
        private static readonly Lazy<HashSet<char>> Gb2312Hanzi = new Lazy<HashSet<char>>(() => DecodeRange(936, 0xB0, 0xF7, 0xA1, 0xFE));

        /// <summary>Big5 常用字（区段 A440–C67E）。</summary>
        private static readonly Lazy<HashSet<char>> Big5CommonHanzi = new Lazy<HashSet<char>>(() =>
        {
            HashSet<char> set = DecodeRange(950, 0xA4, 0xC6, 0x40, 0x7E);
            set.UnionWith(DecodeRange(950, 0xA4, 0xC5, 0xA1, 0xFE));
            return set;
        });

        /// <summary>JIS 第一水准汉字（Shift-JIS 区段 889F–9872）。</summary>
        private static readonly Lazy<HashSet<char>> JisLevel1Kanji = new Lazy<HashSet<char>>(() =>
        {
            HashSet<char> set = DecodeRange(932, 0x88, 0x98, 0x40, 0x7E);
            set.UnionWith(DecodeRange(932, 0x88, 0x98, 0x80, 0xFC));
            return set;
        });

        /// <summary>韩文常用音节在 EUC-KR 中的区段 B0A1–C8FE，按 GBK 解码得到的汉字。</summary>
        private static readonly Lazy<HashSet<char>> GbKoreanShadow = new Lazy<HashSet<char>>(() => DecodeRange(936, 0xB0, 0xC8, 0xA1, 0xFE));

        /// <summary>同一区段按 Big5 解码得到的汉字。</summary>
        private static readonly Lazy<HashSet<char>> Big5KoreanShadow = new Lazy<HashSet<char>>(() => DecodeRange(950, 0xB0, 0xC8, 0xA1, 0xFE));

        private static HashSet<char> DecodeRange(int codePage, int leadFrom, int leadTo, int trailFrom, int trailTo)
        {
            Encoding encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, new DecoderReplacementFallback("\uFFFD"));
            var set = new HashSet<char>();
            var pair = new byte[2];
            for (int lead = leadFrom; lead <= leadTo; lead++)
            {
                for (int trail = trailFrom; trail <= trailTo; trail++)
                {
                    pair[0] = (byte)lead;
                    pair[1] = (byte)trail;
                    string s = encoding.GetString(pair);
                    if (s.Length == 1 && s[0] >= '\u4E00' && s[0] <= '\u9FFF') set.Add(s[0]);
                }
            }
            return set;
        }

        #endregion
    }
}
