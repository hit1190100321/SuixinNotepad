using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SuixinNotepad.Core
{
    /// <summary>
    /// 标签页历史：每个文件一条，记录最近一次查看的时间，按时间从新到旧排列，用于找回已关闭的标签页。
    /// 路径都是 FilePath.Normalize 之后的完整路径，按不区分大小写的字符串比较。
    /// </summary>
    [DataContract]
    internal sealed class TabHistory
    {
        public const int MaxEntries = 500;

        [DataMember] public List<TabHistoryEntry> Entries { get; set; } = new List<TabHistoryEntry>();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Entries = new List<TabHistoryEntry>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Entries == null || Entries.Contains(null!)) throw new SerializationException("标签页历史中的列表无效。");
            Entries.Sort((a, b) => b.Viewed.CompareTo(a.Viewed));
        }

        /// <summary>记下一次查看。同一文件只保留一条，超出上限时丢弃最早的记录。</summary>
        public void Record(string path, string title, NoteKind kind, DateTime viewed)
        {
            Remove(path);
            var entry = new TabHistoryEntry { Path = path, Title = title, Kind = kind, Viewed = viewed };
            int index = Entries.FindIndex(e => e.Viewed <= viewed);
            Entries.Insert(index < 0 ? Entries.Count : index, entry);
            if (Entries.Count > MaxEntries) Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);
        }

        /// <summary>文件改名或移动后，把原记录改到新路径上，查看时间不变。原路径没有记录时不做任何事。</summary>
        public void Move(string oldPath, string newPath, string title, NoteKind kind)
        {
            TabHistoryEntry? entry = Find(oldPath);
            if (entry == null) return;
            Entries.RemoveAll(e => e != entry && SamePath(e.Path, newPath));
            entry.Path = newPath;
            entry.Title = title;
            entry.Kind = kind;
        }

        public void Remove(string path) => Entries.RemoveAll(e => SamePath(e.Path, path));

        public TabHistoryEntry? Find(string path) => Entries.Find(e => SamePath(e.Path, path));

        /// <summary>标题或路径中含有 query 的记录（不区分大小写），按查看时间从新到旧。query 为空时返回全部。</summary>
        public List<TabHistoryEntry> Search(string query)
        {
            query = query.Trim();
            if (query.Length == 0) return new List<TabHistoryEntry>(Entries);
            return Entries.FindAll(e =>
                e.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                e.Path.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    [DataContract]
    internal sealed class TabHistoryEntry
    {
        [DataMember] public string Path { get; set; } = "";
        [DataMember] public string Title { get; set; } = "";
        [DataMember] public NoteKind Kind { get; set; }

        /// <summary>最近一次查看的时间（本地时间）。</summary>
        [DataMember] public DateTime Viewed { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Path = "";
            Title = "";
            Kind = NoteKind.File;
            Viewed = DateTime.MinValue;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (string.IsNullOrEmpty(Path) || Title == null || !Enum.IsDefined(typeof(NoteKind), Kind))
                throw new SerializationException("标签页历史中的记录无效。");
        }
    }
}
