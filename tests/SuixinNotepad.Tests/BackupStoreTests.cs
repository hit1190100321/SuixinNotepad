using System;
using System.IO;
using System.Linq;
using System.Text;
using SuixinNotepad.Core;
using Xunit;

namespace SuixinNotepad.Tests
{
    public class BackupStoreTests : IDisposable
    {
        private readonly TempDir temp = new TempDir();
        private DateTime now = new DateTime(2026, 10, 17, 9, 0, 0);
        private readonly BackupStore store;

        public BackupStoreTests()
        {
            store = new BackupStore(temp.File("Backups"), () => now);
        }

        public void Dispose() => temp.Dispose();

        private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public void 备份按时间从新到旧列出且内容一致()
        {
            string note = temp.File("笔记.txt");
            store.Create(note, Bytes("第一版"));
            now = now.AddMinutes(1);
            store.Create(note, Bytes("第二版"));

            var list = store.List(note);
            Assert.Equal(2, list.Count);
            Assert.Equal("第二版", Encoding.UTF8.GetString(store.Read(list[0])));
            Assert.Equal("第一版", Encoding.UTF8.GetString(store.Read(list[1])));
            Assert.True(list[0].SavedAt > list[1].SavedAt);
        }

        [Fact]
        public void 备份跟随完整路径_同名不同目录互不影响()
        {
            Directory.CreateDirectory(temp.File("a"));
            Directory.CreateDirectory(temp.File("b"));
            store.Create(temp.File(@"a\同名.txt"), Bytes("A"));
            store.Create(temp.File(@"b\同名.txt"), Bytes("B"));

            Assert.Single(store.List(temp.File(@"a\同名.txt")));
            Assert.Single(store.List(temp.File(@"b\同名.txt")));
            Assert.Equal("A", Encoding.UTF8.GetString(store.Read(store.List(temp.File(@"a\同名.txt"))[0])));
        }

        [Fact]
        public void 内容与最近一份相同时不重复备份()
        {
            string note = temp.File("n.txt");
            Assert.True(store.Create(note, Bytes("x")));
            now = now.AddSeconds(5);
            Assert.False(store.Create(note, Bytes("x")));
            now = now.AddSeconds(5);
            Assert.True(store.Create(note, Bytes("y")));
            Assert.Equal(2, store.List(note).Count);
        }

        [Fact]
        public void 同一毫秒的多次备份不会互相覆盖()
        {
            string note = temp.File("n.txt");
            store.Create(note, Bytes("1"));
            store.Create(note, Bytes("2"));
            store.Create(note, Bytes("3"));
            var list = store.List(note);
            Assert.Equal(new[] { "3", "2", "1" }, list.Select(e => Encoding.UTF8.GetString(store.Read(e))).ToArray());
        }

        [Fact]
        public void 清理删除一年以前的备份()
        {
            string note = temp.File("n.txt");
            store.Create(note, Bytes("很旧"));
            now = now.AddDays(200);
            store.Create(note, Bytes("较旧"));
            now = now.AddDays(200);
            store.Create(note, Bytes("最新"));

            Assert.Equal(1, store.Cleanup(note));
            var left = store.List(note).Select(e => Encoding.UTF8.GetString(store.Read(e))).ToArray();
            Assert.Equal(new[] { "最新", "较旧" }, left);
        }

        [Fact]
        public void 清理把数量压到上限并删除最旧的()
        {
            string note = temp.File("n.txt");
            for (int i = 0; i < BackupStore.MaxCount + 7; i++)
            {
                store.Create(note, Bytes("v" + i));
                now = now.AddMinutes(1);
            }

            Assert.Equal(7, store.Cleanup(note));
            var list = store.List(note);
            Assert.Equal(BackupStore.MaxCount, list.Count);
            Assert.Equal("v" + (BackupStore.MaxCount + 6), Encoding.UTF8.GetString(store.Read(list[0])));
            Assert.Equal("v7", Encoding.UTF8.GetString(store.Read(list[list.Count - 1])));
        }

        [Fact]
        public void 迁移历史后旧路径无备份_新路径合并全部备份()
        {
            string oldPath = temp.File("old.txt");
            string newPath = temp.File("new.txt");
            store.Create(oldPath, Bytes("旧1"));
            now = now.AddMinutes(1);
            store.Create(oldPath, Bytes("旧2"));
            now = now.AddMinutes(1);
            store.Create(newPath, Bytes("新1"));

            store.MoveHistory(oldPath, newPath);

            Assert.Empty(store.List(oldPath));
            Assert.False(Directory.Exists(store.GetDirectory(oldPath)));
            var merged = store.List(newPath).Select(e => Encoding.UTF8.GetString(store.Read(e))).ToArray();
            Assert.Equal(new[] { "新1", "旧2", "旧1" }, merged);
        }
    }
}
