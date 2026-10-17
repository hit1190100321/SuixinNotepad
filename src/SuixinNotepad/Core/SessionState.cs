using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SuixinNotepad.Core
{
    /// <summary>上次打开的标签页。只有“清空标签页”才会清掉。</summary>
    [DataContract]
    internal sealed class SessionState
    {
        [DataMember] public List<SessionTab> Tabs { get; set; } = new List<SessionTab>();
        [DataMember] public int ActiveIndex { get; set; }

        /// <summary>反序列化不调用构造函数，文件缺少的字段由此获得默认值。</summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Tabs = new List<SessionTab>();
            ActiveIndex = 0;
        }

        /// <summary>字段写成 null 等无法使用的内容时按损坏处理。</summary>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Tabs == null || Tabs.Contains(null!)) throw new SerializationException("会话文件中的标签页列表无效。");
        }
    }

    [DataContract]
    internal sealed class SessionTab
    {
        [DataMember] public string Path { get; set; } = "";

        /// <summary>用户未指定位置，文件位于默认文件夹且名称由程序生成。</summary>
        [DataMember] public bool Untitled { get; set; }

        /// <summary>标签标题，启动时无需读取文件即可显示。</summary>
        [DataMember] public string Title { get; set; } = "";

        [DataMember] public int SelectionStart { get; set; }
        [DataMember] public int SelectionLength { get; set; }

        /// <summary>用户选定的编码名称；为空时载入文件自动识别编码。</summary>
        [DataMember] public string Encoding { get; set; } = "";

        /// <summary>反序列化不调用构造函数，旧版本会话文件缺少的字段由此获得默认值。</summary>
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Path = "";
            Untitled = false;
            Title = "";
            SelectionStart = 0;
            SelectionLength = 0;
            Encoding = "";
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Path == null || Title == null || Encoding == null) throw new SerializationException("会话文件中的标签页记录无效。");
        }
    }
}
