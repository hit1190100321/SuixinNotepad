using System;
using System.IO;
using System.Text;

namespace SuixinNotepad.Core
{
    internal enum LineEnding
    {
        CrLf,
        Lf,
        Cr,
    }

    /// <summary>文件在磁盘上的状态，用于判断文件是否被外部程序修改。</summary>
    internal readonly struct FileStamp : IEquatable<FileStamp>
    {
        public FileStamp(DateTime lastWriteUtc, long length)
        {
            LastWriteUtc = lastWriteUtc;
            Length = length;
        }

        public DateTime LastWriteUtc { get; }
        public long Length { get; }

        /// <summary>读取文件当前状态；文件不存在时返回 null。</summary>
        public static FileStamp? Of(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            return new FileStamp(info.LastWriteTimeUtc, info.Length);
        }

        public bool Equals(FileStamp other) => LastWriteUtc == other.LastWriteUtc && Length == other.Length;
        public override bool Equals(object? obj) => obj is FileStamp other && Equals(other);
        public override int GetHashCode() => LastWriteUtc.GetHashCode() ^ Length.GetHashCode();
    }

    /// <summary>解码后的文本。Text 中的换行统一为 '\n'。</summary>
    internal sealed class DecodedText
    {
        public DecodedText(string text, TextEncoding encoding, LineEnding lineEnding, bool lossless)
        {
            Text = text;
            Encoding = encoding;
            LineEnding = lineEnding;
            Lossless = lossless;
        }

        public string Text { get; }
        public TextEncoding Encoding { get; }
        public LineEnding LineEnding { get; }

        /// <summary>
        /// 按 Encoding 重新编码能得到与文件完全相同的字节（不计 BOM 和换行符统一）。
        /// 为 false 时文件中有无法按该编码识别的字节，写回会破坏原文件。
        /// </summary>
        public bool Lossless { get; }
    }

    /// <summary>文本含有目标编码无法表示的字符时抛出，调用方应改用 UTF-8 保存。</summary>
    internal sealed class UnrepresentableTextException : Exception
    {
        public UnrepresentableTextException(TextEncoding encoding) : base("文本中包含 " + encoding.Name + " 无法表示的字符。") { }
    }

    internal static class TextFile
    {
        /// <summary>新建文件使用的编码与换行，与 Windows 记事本默认值一致。</summary>
        public static readonly TextEncoding DefaultEncoding = TextEncoding.Utf8;
        public const LineEnding DefaultLineEnding = LineEnding.CrLf;

        public static string DisplayName(LineEnding lineEnding)
        {
            switch (lineEnding)
            {
                case LineEnding.CrLf: return "Windows (CRLF)";
                case LineEnding.Lf: return "Unix (LF)";
                case LineEnding.Cr: return "Macintosh (CR)";
                default: throw new ArgumentOutOfRangeException(nameof(lineEnding), lineEnding, null);
            }
        }

        /// <summary>读取文件。encoding 为 null 时自动识别；不是文本文件时抛 NotTextFileException。</summary>
        public static DecodedText Load(string path, TextEncoding? encoding = null) => Decode(File.ReadAllBytes(path), encoding);

        /// <summary>
        /// 解码。encoding 为 null 时自动识别编码，不是文本时抛 NotTextFileException；
        /// 指定编码时按该编码解码，不合法的字节替换为 U+FFFD。两种情况下文件开头的 BOM 都优先。
        /// </summary>
        public static DecodedText Decode(byte[] bytes, TextEncoding? encoding = null)
        {
            TextEncoding? bom = EncodingDetector.DetectBom(bytes);
            TextEncoding chosen = bom ?? encoding ?? EncodingDetector.Detect(bytes, TextEncoding.SystemLegacy);
            int offset = bom != null ? bom.Preamble.Length : 0;

            string raw = chosen.LenientReader.GetString(bytes, offset, bytes.Length - offset);
            bool lossless = IsLossless(chosen, raw, bytes, offset);
            return new DecodedText(NormalizeNewlines(raw), chosen, DetectLineEnding(raw), lossless);
        }

        /// <summary>把文本按指定编码与换行编码为字节（含 BOM）。text 中换行可以是任意形式。</summary>
        public static byte[] Encode(string text, TextEncoding encoding, LineEnding lineEnding)
        {
            string body = ApplyLineEnding(NormalizeNewlines(text), lineEnding);
            byte[] content;
            try
            {
                content = encoding.Writer.GetBytes(body);
            }
            catch (EncoderFallbackException)
            {
                throw new UnrepresentableTextException(encoding);
            }

            byte[] preamble = encoding.Preamble;
            if (preamble.Length == 0) return content;
            var result = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
            return result;
        }

        /// <summary>重新编码后逐字节比较。解码时被替换为 U+FFFD 的字节无法还原，比较必然不等。</summary>
        private static bool IsLossless(TextEncoding encoding, string raw, byte[] bytes, int offset)
        {
            byte[] again;
            try
            {
                again = encoding.Writer.GetBytes(raw);
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
            if (again.Length != bytes.Length - offset) return false;
            for (int i = 0; i < again.Length; i++)
                if (again[i] != bytes[offset + i]) return false;
            return true;
        }

        /// <summary>
        /// 原子写入：先写同目录临时文件，再替换目标文件。
        /// 写入中途失败或断电时，原文件保持完整。
        /// </summary>
        public static void WriteAtomic(string path, byte[] bytes)
        {
            string fullPath = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(fullPath)!;
            string temp = Path.Combine(dir, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(bytes, 0, bytes.Length);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temp, fullPath, null, true);
                }
                else
                {
                    File.Move(temp, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public static string NormalizeNewlines(string text)
        {
            if (text.IndexOf('\r') < 0) return text;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string ApplyLineEnding(string normalized, LineEnding lineEnding)
        {
            switch (lineEnding)
            {
                case LineEnding.CrLf: return normalized.Replace("\n", "\r\n");
                case LineEnding.Lf: return normalized;
                case LineEnding.Cr: return normalized.Replace('\n', '\r');
                default: throw new ArgumentOutOfRangeException(nameof(lineEnding), lineEnding, null);
            }
        }

        /// <summary>以文件中出现的第一个换行为准；没有换行时使用默认值。</summary>
        private static LineEnding DetectLineEnding(string raw)
        {
            int i = raw.IndexOfAny(new[] { '\r', '\n' });
            if (i < 0) return DefaultLineEnding;
            if (raw[i] == '\n') return LineEnding.Lf;
            return i + 1 < raw.Length && raw[i + 1] == '\n' ? LineEnding.CrLf : LineEnding.Cr;
        }
    }
}
