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
        public DecodedText(string text, TextEncoding encoding, LineEnding lineEnding, bool mixedLineEndings, bool lossless)
        {
            Text = text;
            Encoding = encoding;
            LineEnding = lineEnding;
            MixedLineEndings = mixedLineEndings;
            Lossless = lossless;
        }

        public string Text { get; }
        public TextEncoding Encoding { get; }

        /// <summary>文件中第一处换行的形式，保存时所有换行都写成这种形式。</summary>
        public LineEnding LineEnding { get; }

        /// <summary>文件混用了多种换行符，保存时会统一为 LineEnding。</summary>
        public bool MixedLineEndings { get; }

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

    /// <summary>文件超过能打开的大小上限。</summary>
    internal sealed class FileTooLargeException : IOException
    {
        public FileTooLargeException(long size)
            : base("文件有 " + (size / 1024 / 1024) + " MB，超过了能打开的上限 " + (TextFile.MaxFileSize / 1024 / 1024) + " MB。") { }
    }

    internal static class TextFile
    {
        /// <summary>新建文件使用的编码与换行，与 Windows 记事本默认值一致。</summary>
        public static readonly TextEncoding DefaultEncoding = TextEncoding.Utf8;
        public const LineEnding DefaultLineEnding = LineEnding.CrLf;

        /// <summary>
        /// 能打开的最大文件。识别编码、解码和编辑器各要保存完整的副本，
        /// 更大的文件会让界面长时间无响应，甚至耗尽内存。
        /// </summary>
        public const long MaxFileSize = 64L * 1024 * 1024;

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

        /// <summary>读取文件的全部字节。允许其他程序同时写入（如正在写的日志），超过大小上限时抛 FileTooLargeException。</summary>
        public static byte[] ReadBytes(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long length = fs.Length;
                if (length > MaxFileSize) throw new FileTooLargeException(length);
                var bytes = new byte[length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = fs.Read(bytes, read, bytes.Length - read);
                    if (n == 0) throw new IOException("读取过程中文件被其他程序截短：" + path);
                    read += n;
                }
                return bytes;
            }
        }

        /// <summary>读取文件。encoding 为 null 时自动识别；不是文本文件时抛 NotTextFileException。</summary>
        public static DecodedText Load(string path, TextEncoding? encoding = null) => Decode(ReadBytes(path), encoding);

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
            LineEnding lineEnding = AnalyzeLineEndings(raw, out bool mixed);
            return new DecodedText(NormalizeNewlines(raw), chosen, lineEnding, mixed, lossless);
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
        /// 写入文件，写入中途失败或断电时原文件保持完整：先写同目录的临时文件，再替换目标文件。
        /// 目标是符号链接时替换它指向的文件。以下情况无法替换，只能直接覆盖原文件：
        /// 文件有多个硬链接（替换会断开链接）、目录不允许新建文件但文件本身可写、文件系统不支持替换。
        /// </summary>
        public static void WriteAtomic(string path, byte[] bytes)
        {
            string target = File.Exists(path) ? FilePath.ResolveLinks(path) : Path.GetFullPath(path);
            bool exists = File.Exists(target);
            if (exists && FilePath.Identify(target) is FileId id && id.Links > 1)
            {
                WriteInPlace(target, bytes);
                return;
            }

            // 临时文件名保持很短，原文件名接近 255 个字符时也不会超长
            string temp = Path.Combine(Path.GetDirectoryName(target)!, "~sx" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                try
                {
                    using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (UnauthorizedAccessException) when (exists)
                {
                    WriteInPlace(target, bytes);
                    return;
                }

                if (!exists)
                {
                    File.Move(temp, target);
                    return;
                }
                try
                {
                    File.Replace(temp, target, null, true);
                }
                catch (IOException ex) when (ReplaceUnsupported(ex))
                {
                    WriteInPlace(target, bytes);
                }
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        /// <summary>直接覆盖已有文件的内容，文件本身（硬链接、权限、属性）保持不变。</summary>
        private static void WriteInPlace(string path, byte[] bytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.SetLength(bytes.Length);
                fs.Flush(true);
            }
        }

        /// <summary>文件系统不支持 ReplaceFile（部分网络共享、FAT 卷）。共享冲突等其他错误照常抛出。</summary>
        private static bool ReplaceUnsupported(IOException ex)
        {
            const int ErrorInvalidFunction = 1;
            const int ErrorNotSupported = 50;
            int code = ex.HResult & 0xFFFF;
            return code == ErrorInvalidFunction || code == ErrorNotSupported;
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

        /// <summary>以文件中出现的第一个换行为准，没有换行时使用默认值；同时检查是否混用了其他换行。</summary>
        private static LineEnding AnalyzeLineEndings(string raw, out bool mixed)
        {
            LineEnding? first = null;
            mixed = false;
            for (int i = 0; i < raw.Length; i++)
            {
                LineEnding current;
                if (raw[i] == '\n')
                {
                    current = LineEnding.Lf;
                }
                else if (raw[i] != '\r')
                {
                    continue;
                }
                else if (i + 1 < raw.Length && raw[i + 1] == '\n')
                {
                    current = LineEnding.CrLf;
                    i++;
                }
                else
                {
                    current = LineEnding.Cr;
                }

                if (first == null)
                {
                    first = current;
                }
                else if (first != current)
                {
                    mixed = true;
                    break;
                }
            }
            return first ?? DefaultLineEnding;
        }
    }
}
