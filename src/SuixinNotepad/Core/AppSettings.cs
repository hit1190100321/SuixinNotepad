using System;
using System.Runtime.Serialization;

namespace SuixinNotepad.Core
{
    [DataContract]
    internal sealed class AppSettings
    {
        public AppSettings()
        {
            SetDefaults();
        }

        /// <summary>未指定位置的记事本保存在这里。</summary>
        [DataMember] public string NotesFolder { get; set; } = "";

        /// <summary>手动保存时自动备份。</summary>
        [DataMember] public bool AutoBackup { get; set; }

        /// <summary>手动保存时自动清理旧备份。自动备份关闭时视同关闭。</summary>
        [DataMember] public bool AutoCleanup { get; set; }

        [DataMember] public bool WordWrap { get; set; }
        [DataMember] public bool ShowStatusBar { get; set; }
        [DataMember] public string FontName { get; set; } = "";
        [DataMember] public float FontSize { get; set; }
        [DataMember] public int FontStyle { get; set; }

        /// <summary>缩放百分比。</summary>
        [DataMember] public int ZoomPercent { get; set; }

        /// <summary>窗口位置与大小（像素）。宽高为 0 表示按屏幕缩放比例使用默认大小。</summary>
        [DataMember] public int WindowX { get; set; }
        [DataMember] public int WindowY { get; set; }
        [DataMember] public int WindowWidth { get; set; }
        [DataMember] public int WindowHeight { get; set; }
        [DataMember] public bool WindowMaximized { get; set; }

        /// <summary>更新地址：网址或文件夹路径，其中需有 latest.json。</summary>
        [DataMember] public string UpdateSource { get; set; } = "";
        [DataMember] public DateTime LastUpdateCheck { get; set; }

        [DataMember] public string FindText { get; set; } = "";
        [DataMember] public string ReplaceText { get; set; } = "";
        [DataMember] public bool FindMatchCase { get; set; }
        [DataMember] public bool FindWrapAround { get; set; }

        public bool EffectiveAutoCleanup => AutoBackup && AutoCleanup;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        /// <summary>字段写成 null 或超出范围时按损坏处理，避免程序带着无法使用的设置启动。</summary>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (NotesFolder == null || FontName == null || UpdateSource == null || FindText == null || ReplaceText == null)
                throw new SerializationException("设置文件中有字段为空。");
            if (NotesFolder.Length == 0 || FontName.Length == 0)
                throw new SerializationException("设置文件中缺少默认保存位置或字体。");
            if (float.IsNaN(FontSize) || FontSize <= 0 || FontSize > MaxFontSize)
                throw new SerializationException("设置文件中的字号无效：" + FontSize);
            if (string.Equals(UpdateSource, FormerUpdateSource, StringComparison.OrdinalIgnoreCase)) UpdateSource = DefaultUpdateSource;
        }

        /// <summary>GDI+ 字体允许的最大字号（磅）。</summary>
        public const float MaxFontSize = 1638f;

        public const string DefaultFontName = "Consolas";
        public const float DefaultFontSize = 11f;

        /// <summary>默认更新地址：GitHub 上最新发布版本的下载目录，其中有 latest.json 和安装包。</summary>
        public const string DefaultUpdateSource = "https://github.com/loding-258/SuixinNotepad/releases/latest/download/";

        /// <summary>GitHub 账号改名前的默认更新地址。1.0.0 把它写进了设置文件，读入时换成新地址。</summary>
        public const string FormerUpdateSource = "https://github.com/hit1190100321/SuixinNotepad/releases/latest/download/";

        /// <summary>反序列化不调用构造函数，旧版本设置文件缺少的字段由此获得默认值。</summary>
        private void SetDefaults()
        {
            NotesFolder = AppPaths.DefaultNotesFolder;
            AutoBackup = true;
            AutoCleanup = true;
            WordWrap = true;
            ShowStatusBar = true;
            FontName = DefaultFontName;
            FontSize = DefaultFontSize;
            FontStyle = 0;
            ZoomPercent = 100;
            WindowWidth = 0;
            WindowHeight = 0;
            WindowX = int.MinValue;
            WindowY = int.MinValue;
            UpdateSource = DefaultUpdateSource;
            FindText = "";
            ReplaceText = "";
            FindWrapAround = true;
        }
    }
}
