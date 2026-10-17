using System;
using System.IO;

namespace SuixinNotepad.Core
{
    /// <summary>程序数据的存放位置。</summary>
    internal sealed class AppPaths
    {
        public AppPaths(string dataRoot)
        {
            DataRoot = dataRoot;
        }

        /// <summary>%LOCALAPPDATA%\SuixinNotepad：设置、会话、备份。卸载程序时保留。</summary>
        public static AppPaths ForCurrentUser() =>
            new AppPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuixinNotepad"));

        /// <summary>未指定位置的记事本默认存放的文件夹：“文档\随心记事本”。</summary>
        public static string DefaultNotesFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "随心记事本");

        public string DataRoot { get; }
        public string SettingsFile => Path.Combine(DataRoot, "settings.json");
        public string SessionFile => Path.Combine(DataRoot, "session.json");
        public string BackupRoot => Path.Combine(DataRoot, "Backups");
        public string UpdateDownloadDir => Path.Combine(Path.GetTempPath(), "SuixinNotepad-Update");
    }
}
