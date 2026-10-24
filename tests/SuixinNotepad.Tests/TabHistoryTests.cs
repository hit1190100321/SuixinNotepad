using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    public class TabHistoryTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 24, 9, 0, 0);

        private static T Parse<T>(string json) => JsonFile.Deserialize<T>(Encoding.UTF8.GetBytes(json));

        [Fact]
        public void 按查看时间从新到旧且每个文件只留一条()
        {
            var history = new TabHistory();
            history.Record(@"C:\a.txt", "a", NoteKind.File, T0);
            history.Record(@"C:\b.txt", "b", NoteKind.File, T0.AddMinutes(1));
            history.Record(@"C:\A.TXT", "a2", NoteKind.File, T0.AddMinutes(2));
            // 关闭一个很久没看的标签页时按它原来的时间插入，不排到最前面
            history.Record(@"C:\c.txt", "c", NoteKind.File, T0.AddMinutes(-5));

            Assert.Equal(new[] { "a2", "b", "c" }, history.Entries.Select(e => e.Title));
        }

        [Fact]
        public void 超出上限时丢弃最早的记录()
        {
            var history = new TabHistory();
            for (int i = 0; i < TabHistory.MaxEntries + 3; i++)
                history.Record(@"C:\n" + i + ".txt", "n" + i, NoteKind.Named, T0.AddMinutes(i));

            Assert.Equal(TabHistory.MaxEntries, history.Entries.Count);
            Assert.Null(history.Find(@"C:\n0.txt"));
            Assert.NotNull(history.Find(@"C:\n3.txt"));
        }

        [Fact]
        public void 改名后记录跟随新路径且保留查看时间()
        {
            var history = new TabHistory();
            history.Record(@"D:\笔记\无标题-1.txt", "会议", NoteKind.Draft, T0);
            history.Record(@"D:\笔记\会议.txt", "旧的同名记录", NoteKind.File, T0.AddMinutes(1));

            history.Move(@"D:\笔记\无标题-1.txt", @"D:\笔记\会议.txt", "会议.txt", NoteKind.Named);

            TabHistoryEntry entry = Assert.Single(history.Entries);
            Assert.Equal(@"D:\笔记\会议.txt", entry.Path);
            Assert.Equal(NoteKind.Named, entry.Kind);
            Assert.Equal(T0, entry.Viewed);
        }

        [Fact]
        public void 只改大小写的改名不丢记录()
        {
            var history = new TabHistory();
            history.Record(@"D:\a.txt", "a.txt", NoteKind.File, T0);
            history.Move(@"D:\a.txt", @"D:\A.txt", "A.txt", NoteKind.File);
            Assert.Equal(@"D:\A.txt", Assert.Single(history.Entries).Path);
        }

        [Fact]
        public void 按标题或路径搜索()
        {
            var history = new TabHistory();
            history.Record(@"D:\工作\周报.txt", "周报.txt", NoteKind.File, T0);
            history.Record(@"D:\生活\Shopping.txt", "Shopping.txt", NoteKind.File, T0.AddMinutes(1));

            Assert.Equal("周报.txt", Assert.Single(history.Search("工作")).Title);
            Assert.Equal("Shopping.txt", Assert.Single(history.Search(" shop ")).Title);
            Assert.Equal(2, history.Search("").Count);
        }

        [Fact]
        public void 读入后保存往返不变()
        {
            var history = new TabHistory();
            history.Record(@"D:\a.txt", "a", NoteKind.Draft, T0);
            TabHistory loaded = JsonFile.Deserialize<TabHistory>(JsonFile.Serialize(history));

            TabHistoryEntry entry = Assert.Single(loaded.Entries);
            Assert.Equal(NoteKind.Draft, entry.Kind);
            Assert.Equal(T0, entry.Viewed);
        }

        [Theory]
        [InlineData("{\"Entries\":null}")]
        [InlineData("{\"Entries\":[null]}")]
        [InlineData("{\"Entries\":[{\"Path\":\"\",\"Title\":\"a\"}]}")]
        [InlineData("{\"Entries\":[{\"Path\":\"D:\\\\a.txt\",\"Title\":\"a\",\"Kind\":9}]}")]
        public void 历史文件内容无效时按损坏处理(string json)
        {
            Assert.Throws<SerializationException>(() => Parse<TabHistory>(json));
        }

        [Fact]
        public void 旧版本会话的未命名标签读作草稿()
        {
            SessionState session = Parse<SessionState>(
                "{\"Tabs\":[{\"Path\":\"D:\\\\a.txt\",\"Untitled\":true},{\"Path\":\"D:\\\\b.txt\",\"Untitled\":false}]}");

            Assert.Equal(NoteKind.Draft, session.Tabs[0].Kind);
            Assert.Equal(NoteKind.File, session.Tabs[1].Kind);
            Assert.Equal(DateTime.MinValue, session.Tabs[0].LastViewed);
        }

        [Fact]
        public void 新会话文件不再写出旧字段()
        {
            var session = new SessionState();
            session.Tabs.Add(new SessionTab { Path = @"D:\a.txt", Kind = NoteKind.Named, LastViewed = T0 });
            string json = Encoding.UTF8.GetString(JsonFile.Serialize(session));

            Assert.DoesNotContain("Untitled", json);
            SessionTab tab = Assert.Single(JsonFile.Deserialize<SessionState>(Encoding.UTF8.GetBytes(json)).Tabs);
            Assert.Equal(NoteKind.Named, tab.Kind);
            Assert.Equal(T0, tab.LastViewed);
        }
    }
}
