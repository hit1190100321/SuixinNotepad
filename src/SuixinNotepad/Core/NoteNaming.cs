using System;
using System.IO;
using System.Text;

namespace SuixinNotepad.Core
{
    /// <summary>标签页对应文件的来历，决定标题、自动命名、另存为和关闭时的处理。</summary>
    internal enum NoteKind
    {
        /// <summary>默认文件夹中的新笔记：文件名是临时的“无标题-时间”，标签显示正文首行，首行写完后自动命名。</summary>
        Draft = 0,

        /// <summary>默认文件夹中已命名的笔记：自动命名、在默认文件夹中重命名，或退出时另存的恢复文件。</summary>
        Named = 1,

        /// <summary>用户打开或指定位置的文件。</summary>
        File = 2,
    }

    /// <summary>由正文生成标题和文件名。</summary>
    internal static class NoteNaming
    {
        public const string UntitledStem = "无标题";

        /// <summary>自动命名的文件名（不含扩展名）最多保留的字符数。</summary>
        public const int MaxStemLength = 50;

        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        private static readonly string[] DeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>第一个非空行（去掉首尾空白），不要求这一行已经结束；全是空白时返回 null。</summary>
        public static string? FirstLine(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
            return null;
        }

        /// <summary>第一个非空行已经写完（其后出现了换行）时返回这一行（去掉首尾空白），否则返回 null。</summary>
        public static string? CompletedFirstLine(string text)
        {
            int start = 0;
            while (true)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0) return null;
                string line = text.Substring(start, end - start).Trim();
                if (line.Length > 0) return line;
                start = end + 1;
            }
        }

        /// <summary>
        /// 把一段文字变成可用的文件名（不含扩展名）：非法字符换成下划线，去掉结尾的点和空格，
        /// 截到 MaxStemLength 个字符（不拆开代理对），避开 CON、NUL 等设备名。结果为空时用“无标题”。
        /// </summary>
        public static string ToFileStem(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
                sb.Append(Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c);
            string stem = sb.ToString().Trim();

            if (stem.Length > MaxStemLength)
            {
                int cut = char.IsHighSurrogate(stem[MaxStemLength - 1]) ? MaxStemLength - 1 : MaxStemLength;
                stem = stem.Substring(0, cut);
            }
            stem = stem.TrimEnd('.', ' ').TrimStart(' ');
            if (stem.Length == 0) return UntitledStem;

            // Windows 把“CON”“con.abc”之类都当作设备名
            int dot = stem.IndexOf('.');
            string device = dot < 0 ? stem : stem.Substring(0, dot);
            if (Array.Exists(DeviceNames, d => string.Equals(d, device.TrimEnd(' '), StringComparison.OrdinalIgnoreCase)))
                stem = "_" + stem;
            return stem;
        }
    }
}
