using System;
using SuixinNotepad.Core;

namespace SuixinNotepad.UI
{
    /// <summary>
    /// 一个标签页对应的记事本。每个记事本始终有一个确定的文件路径：
    /// 用户指定的位置，或默认文件夹中由程序命名的文件（Untitled）。
    /// 编辑器在第一次激活时才创建并读入文件。
    /// </summary>
    internal sealed class NoteDocument
    {
        public const string UntitledTitle = "无标题";
        private const int TitleMaxLength = 30;

        public NoteDocument(string path, bool untitled)
        {
            Path = System.IO.Path.GetFullPath(path);
            Untitled = untitled;
            Title = untitled ? UntitledTitle : System.IO.Path.GetFileName(Path);
        }

        public string Path { get; set; }

        /// <summary>用户未指定位置：文件位于默认文件夹，名称由程序生成，标题取自正文首行。</summary>
        public bool Untitled { get; set; }

        public FileEncoding Encoding { get; set; } = TextFile.DefaultEncoding;
        public LineEnding LineEnding { get; set; } = TextFile.DefaultLineEnding;

        /// <summary>最近一次读写后文件在磁盘上的状态；文件尚未创建时为 null。</summary>
        public FileStamp? DiskStamp { get; set; }

        /// <summary>编辑器内容与磁盘文件不一致，等待自动保存。</summary>
        public bool Dirty { get; set; }

        /// <summary>最近一次保存失败的原因。</summary>
        public string? SaveError { get; set; }

        public NoteEditor? Editor { get; set; }

        /// <summary>编辑器创建前，记住会话中的选区，载入后恢复。</summary>
        public int PendingSelectionStart { get; set; }
        public int PendingSelectionLength { get; set; }

        public string Title { get; private set; }

        public bool IsLoaded => Editor != null;

        public string FileName => System.IO.Path.GetFileName(Path);

        /// <summary>根据正文更新标题。返回标题是否变化。</summary>
        public bool RefreshTitle()
        {
            string title;
            if (!Untitled)
            {
                title = FileName;
            }
            else if (Editor == null)
            {
                title = Title;
            }
            else
            {
                title = UntitledTitle;
                string head = Editor.GetTextRange(0, Math.Min(Editor.TextLength, 400));
                foreach (string line in head.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;
                    title = trimmed.Length > TitleMaxLength ? trimmed.Substring(0, TitleMaxLength) + "…" : trimmed;
                    break;
                }
            }

            if (title == Title) return false;
            Title = title;
            return true;
        }

        /// <summary>会话恢复时使用上次记录的标题，编辑器载入后再按正文刷新。</summary>
        public void RestoreTitle(string title)
        {
            if (Untitled && title.Length > 0) Title = title;
        }
    }
}
