using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SuixinNotepad.Core
{
    internal sealed class BackupEntry
    {
        public BackupEntry(string backupPath, DateTime savedAt, long size)
        {
            BackupPath = backupPath;
            SavedAt = savedAt;
            Size = size;
        }

        /// <summary>备份文件本身的路径。</summary>
        public string BackupPath { get; }

        /// <summary>手动保存的本地时间。</summary>
        public DateTime SavedAt { get; }

        public long Size { get; }
    }

    /// <summary>
    /// 手动保存的备份。备份跟随 txt 的完整路径：同一路径的所有备份放在一个目录中，
    /// 目录名由文件名和路径哈希组成，目录内的 source.txt 记录原始路径。
    /// 备份文件名即保存时间，按文件名排序就是时间顺序。
    /// </summary>
    internal sealed class BackupStore
    {
        public const int MaxCount = 50;
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);

        private const string SourceFileName = "source.txt";
        private const string TimeFormat = "yyyy-MM-dd_HH-mm-ss-fff";
        private const string BackupExtension = ".txt";

        private readonly Func<DateTime> clock;

        public BackupStore(string root, Func<DateTime>? clock = null)
        {
            Root = root;
            this.clock = clock ?? (() => DateTime.Now);
        }

        public string Root { get; }

        /// <summary>某个 txt 对应的备份目录（不保证已存在）。</summary>
        public string GetDirectory(string filePath)
        {
            string full = Path.GetFullPath(filePath);
            string name = Path.GetFileName(full);
            if (name.Length > 40) name = name.Substring(0, 40);
            return Path.Combine(Root, name + "_" + PathHash(full));
        }

        /// <summary>
        /// 记录一次手动保存。内容与最近一份备份完全相同时不再重复存储，
        /// 避免连续按 Ctrl+S 用相同内容挤掉更早的有效版本。返回本次是否新建了备份。
        /// </summary>
        public bool Create(string filePath, byte[] content)
        {
            string dir = GetDirectory(filePath);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, SourceFileName), Path.GetFullPath(filePath), Encoding.UTF8);

            BackupEntry? latest = List(filePath).FirstOrDefault();
            if (latest != null && latest.Size == content.Length && File.ReadAllBytes(latest.BackupPath).SequenceEqual(content))
                return false;

            DateTime now = clock();
            string target = UniqueBackupPath(dir, now);
            File.WriteAllBytes(target, content);
            return true;
        }

        /// <summary>按保存时间从新到旧列出备份。</summary>
        public IReadOnlyList<BackupEntry> List(string filePath)
        {
            string dir = GetDirectory(filePath);
            if (!Directory.Exists(dir)) return Array.Empty<BackupEntry>();

            var entries = new List<BackupEntry>();
            foreach (string path in Directory.GetFiles(dir, "*" + BackupExtension))
            {
                if (TryParseTime(path, out DateTime savedAt))
                    entries.Add(new BackupEntry(path, savedAt, new FileInfo(path).Length));
            }
            entries.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b.BackupPath), Path.GetFileName(a.BackupPath)));
            return entries;
        }

        public byte[] Read(BackupEntry entry) => File.ReadAllBytes(entry.BackupPath);

        /// <summary>
        /// 删除一年以前的备份，再把数量压到上限以内（删除最旧的）。返回删除的数量。
        /// keep 是正在恢复的那份备份，无论新旧都保留，下一次清理时再按规则处理。
        /// </summary>
        public int Cleanup(string filePath, string? keep = null)
        {
            DateTime cutoff = clock() - MaxAge;
            int removed = 0;
            IReadOnlyList<BackupEntry> entries = List(filePath);
            for (int i = 0; i < entries.Count; i++)
            {
                if (keep != null && string.Equals(entries[i].BackupPath, keep, StringComparison.OrdinalIgnoreCase)) continue;
                if (entries[i].SavedAt < cutoff || i >= MaxCount)
                {
                    File.Delete(entries[i].BackupPath);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>文件改名或移动后，把备份历史合并到新路径名下。</summary>
        public void MoveHistory(string oldPath, string newPath)
        {
            string oldDir = GetDirectory(oldPath);
            string newDir = GetDirectory(newPath);
            if (!Directory.Exists(oldDir) || string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)) return;

            Directory.CreateDirectory(newDir);
            foreach (BackupEntry entry in List(oldPath))
            {
                File.Move(entry.BackupPath, UniqueBackupPath(newDir, entry.SavedAt));
            }
            File.WriteAllText(Path.Combine(newDir, SourceFileName), Path.GetFullPath(newPath), Encoding.UTF8);
            Directory.Delete(oldDir, true);
        }

        private static string UniqueBackupPath(string dir, DateTime time)
        {
            string stamp = time.ToString(TimeFormat, CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, stamp + BackupExtension);
            for (int n = 2; File.Exists(path); n++)
                path = Path.Combine(dir, stamp + "~" + n.ToString(CultureInfo.InvariantCulture) + BackupExtension);
            return path;
        }

        private static bool TryParseTime(string backupPath, out DateTime time)
        {
            string name = Path.GetFileNameWithoutExtension(backupPath);
            int tilde = name.IndexOf('~');
            if (tilde >= 0) name = name.Substring(0, tilde);
            return DateTime.TryParseExact(name, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }

        private static string PathHash(string fullPath)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
