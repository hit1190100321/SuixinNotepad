using System;
using System.IO;
using System.Text;

namespace SuixinNotepad.Core
{
    /// <summary>文件编码，与 Windows 记事本可选编码一致。</summary>
    internal enum FileEncoding
    {
        Utf8,
        Utf8Bom,
        Utf16LE,
        Utf16BE,
        Ansi,
    }

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
        public DecodedText(string text, FileEncoding encoding, LineEnding lineEnding)
        {
            Text = text;
            Encoding = encoding;
            LineEnding = lineEnding;
        }

        public string Text { get; }
        public FileEncoding Encoding { get; }
        public LineEnding LineEnding { get; }
    }

    /// <summary>文本无法用 ANSI 编码表示时抛出，调用方应改用 Unicode 编码保存。</summary>
    internal sealed class AnsiUnrepresentableException : Exception
    {
        public AnsiUnrepresentableException() : base("文本中包含 ANSI 编码无法表示的字符。") { }
    }

    internal static class TextFile
    {
        /// <summary>新建文件使用的编码与换行，与 Windows 记事本默认值一致。</summary>
        public const FileEncoding DefaultEncoding = FileEncoding.Utf8;
        public const LineEnding DefaultLineEnding = LineEnding.CrLf;

        public static string DisplayName(FileEncoding encoding)
        {
            switch (encoding)
            {
                case FileEncoding.Utf8: return "UTF-8";
                case FileEncoding.Utf8Bom: return "带有 BOM 的 UTF-8";
                case FileEncoding.Utf16LE: return "UTF-16 LE";
                case FileEncoding.Utf16BE: return "UTF-16 BE";
                case FileEncoding.Ansi: return "ANSI";
                default: throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null);
            }
        }

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

        public static DecodedText Load(string path) => Decode(File.ReadAllBytes(path));

        public static DecodedText Decode(byte[] bytes)
        {
            FileEncoding encoding;
            string raw;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = FileEncoding.Utf8Bom;
                raw = new UTF8Encoding(false, false).GetString(bytes, 3, bytes.Length - 3);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = FileEncoding.Utf16LE;
                raw = new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
            }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = FileEncoding.Utf16BE;
                raw = new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);
            }
            else if (TryDecodeStrictUtf8(bytes, out var utf8))
            {
                encoding = FileEncoding.Utf8;
                raw = utf8;
            }
            else
            {
                encoding = FileEncoding.Ansi;
                raw = Encoding.Default.GetString(bytes);
            }

            return new DecodedText(NormalizeNewlines(raw), encoding, DetectLineEnding(raw));
        }

        /// <summary>把文本按指定编码与换行编码为字节（含 BOM）。text 中换行可以是任意形式。</summary>
        public static byte[] Encode(string text, FileEncoding encoding, LineEnding lineEnding)
        {
            string body = ApplyLineEnding(NormalizeNewlines(text), lineEnding);
            Encoding enc;
            switch (encoding)
            {
                case FileEncoding.Utf8: enc = new UTF8Encoding(false); break;
                case FileEncoding.Utf8Bom: enc = new UTF8Encoding(true); break;
                case FileEncoding.Utf16LE: enc = new UnicodeEncoding(false, true); break;
                case FileEncoding.Utf16BE: enc = new UnicodeEncoding(true, true); break;
                case FileEncoding.Ansi:
                    enc = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null);
            }

            byte[] preamble = enc.GetPreamble();
            byte[] content;
            try
            {
                content = enc.GetBytes(body);
            }
            catch (EncoderFallbackException)
            {
                throw new AnsiUnrepresentableException();
            }

            if (preamble.Length == 0) return content;
            var result = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
            return result;
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

        private static bool TryDecodeStrictUtf8(byte[] bytes, out string text)
        {
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = string.Empty;
                return false;
            }
        }
    }
}
