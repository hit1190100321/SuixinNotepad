using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SuixinNotepad.Core
{
    /// <summary>磁盘上一个文件的身份：卷序列号加文件编号，与访问它所用的路径写法无关。</summary>
    internal readonly struct FileId : IEquatable<FileId>
    {
        public FileId(uint volume, ulong index, uint links)
        {
            Volume = volume;
            Index = index;
            Links = links;
        }

        public uint Volume { get; }
        public ulong Index { get; }

        /// <summary>硬链接数。大于 1 时同一份内容还有别的文件名。</summary>
        public uint Links { get; }

        public bool Equals(FileId other) => Volume == other.Volume && Index == other.Index;
        public override bool Equals(object? obj) => obj is FileId other && Equals(other);
        public override int GetHashCode() => Volume.GetHashCode() ^ Index.GetHashCode();
    }

    /// <summary>
    /// 路径规范化与文件身份比较。同一个文件可以有多种路径写法（大小写、8.3 短文件名、
    /// 映射盘符与 UNC、符号链接、目录联接、硬链接），只比较字符串会把它当成不同的文件。
    /// </summary>
    internal static class FilePath
    {
        /// <summary>完整路径；文件或目录存在时把 8.3 短文件名展开为长文件名。</summary>
        public static string Normalize(string path)
        {
            string full = Path.GetFullPath(path);
            var buffer = new StringBuilder(full.Length + 260);
            int length = GetLongPathName(full, buffer, buffer.Capacity);
            if (length >= buffer.Capacity)
            {
                buffer.Capacity = length;
                length = GetLongPathName(full, buffer, buffer.Capacity);
            }
            return length > 0 && length < buffer.Capacity ? buffer.ToString() : full;
        }

        /// <summary>两个路径是否指向同一个文件：路径相同，或两者都存在且文件身份相同。</summary>
        public static bool SameFile(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            FileId? x = Identify(a);
            return x != null && x.Equals(Identify(b));
        }

        /// <summary>读取文件身份，符号链接按其指向的文件计算；文件不存在或无法打开时返回 null。</summary>
        public static FileId? Identify(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero))
            {
                if (handle.IsInvalid || !GetFileInformationByHandle(handle, out ByHandleFileInformation info)) return null;
                return new FileId(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, info.NumberOfLinks);
            }
        }

        /// <summary>
        /// 符号链接最终指向的文件路径；不是链接时返回规范化的原路径。文件不存在时原样返回完整路径。
        /// </summary>
        public static string ResolveLinks(string path)
        {
            string full = Path.GetFullPath(path);
            using (SafeFileHandle handle = CreateFile(full, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero))
            {
                if (handle.IsInvalid) return full;
                var buffer = new StringBuilder(full.Length + 260);
                int length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                if (length >= buffer.Capacity)
                {
                    buffer.Capacity = length;
                    length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                }
                if (length <= 0 || length >= buffer.Capacity) throw new IOException("无法解析文件的实际位置：" + full);
                string final = buffer.ToString();
                if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + final.Substring(8);
                if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) return final.Substring(4);
                return final;
            }
        }

        private const uint FileShareAll = 0x1 | 0x2 | 0x4;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;

        // FILETIME 只按 4 字节对齐，Pack = 4 才能与原生结构一致
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern int GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int bufferLength, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int bufferLength);
    }
}
