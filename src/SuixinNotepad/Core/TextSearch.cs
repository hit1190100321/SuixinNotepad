using System;
using System.Text;

namespace SuixinNotepad.Core
{
    internal sealed class FindOptions
    {
        public string Find { get; set; } = "";
        public string Replace { get; set; } = "";
        public bool MatchCase { get; set; }
        public bool WrapAround { get; set; }
        public bool Up { get; set; }

        public StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }

    /// <summary>与 Windows 记事本一致的查找逻辑。</summary>
    internal static class TextSearch
    {
        /// <summary>
        /// 从当前选区出发查找下一个匹配。向下从选区末尾开始，向上从选区开头之前开始。
        /// 返回匹配起点，找不到返回 -1。
        /// </summary>
        public static int FindNext(string text, FindOptions options, int selectionStart, int selectionLength)
        {
            string query = options.Find;
            if (query.Length == 0 || text.Length == 0) return -1;

            if (!options.Up)
            {
                int from = Math.Min(selectionStart + selectionLength, text.Length);
                int hit = text.IndexOf(query, from, options.Comparison);
                if (hit < 0 && options.WrapAround) hit = text.IndexOf(query, 0, options.Comparison);
                return hit;
            }
            else
            {
                // LastIndexOf 的起点是匹配可以结束的最后位置
                int end = selectionStart - 1;
                int hit = end >= 0 ? text.LastIndexOf(query, end, end + 1, options.Comparison) : -1;
                if (hit < 0 && options.WrapAround) hit = text.LastIndexOf(query, text.Length - 1, text.Length, options.Comparison);
                return hit;
            }
        }

        /// <summary>替换全部匹配，返回新文本与替换次数。</summary>
        public static string ReplaceAll(string text, FindOptions options, out int count)
        {
            count = 0;
            string query = options.Find;
            if (query.Length == 0) return text;

            var sb = new StringBuilder(text.Length);
            int pos = 0;
            while (true)
            {
                int hit = text.IndexOf(query, pos, options.Comparison);
                if (hit < 0) break;
                sb.Append(text, pos, hit - pos).Append(options.Replace);
                pos = hit + query.Length;
                count++;
            }
            if (count == 0) return text;
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }

        /// <summary>第 line 行（从 1 开始）的起始下标；超出总行数返回 -1。</summary>
        public static int LineStart(string text, int line)
        {
            if (line < 1) return -1;
            int index = 0;
            for (int current = 1; current < line; current++)
            {
                int nl = text.IndexOf('\n', index);
                if (nl < 0) return -1;
                index = nl + 1;
            }
            return index;
        }
    }
}
