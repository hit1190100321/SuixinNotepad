using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SuixinNotepad.Core
{
    /// <summary>
    /// .txt 文件的默认打开程序。Windows 不允许程序自行改写默认程序，
    /// 只能弹出系统的“选择打开方式”对话框，由用户确认“始终使用此应用”。
    /// 程序的注册信息（ProgId、打开方式、默认应用功能）由安装包写入。
    /// </summary>
    internal static class DefaultApp
    {
        public const string Extension = ".txt";

        /// <summary>安装包在用户勾选“设为默认程序”后用这个参数启动本程序。</summary>
        public const string ChooseArgument = "--choose-default-txt";

        /// <summary>本程序是否已是 .txt 的默认打开程序。</summary>
        public static bool IsCurrent()
        {
            string? exe = Query(AssocStr.Executable);
            return exe != null && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>当前默认打开程序的显示名称，没有默认程序时返回 null。</summary>
        public static string? CurrentName() => Query(AssocStr.FriendlyAppName) ?? Query(AssocStr.Executable);

        /// <summary>
        /// 弹出系统的“选择打开方式”对话框，用户关闭对话框后返回。
        /// 对话框关闭时无论用户是否更改都返回成功，结果要用 IsCurrent 重新查询。
        /// </summary>
        public static void Choose(IntPtr owner)
        {
            // 安装包刚写入的注册信息要先通知资源管理器，对话框里才会列出本程序
            SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);

            // 对话框只按扩展名决定文件类型，文件本身不需要存在
            var info = new OpenAsInfo
            {
                File = "随心记事本" + Extension,
                Class = null,
                Flags = OaifAllowRegistration | OaifRegisterExt,
            };
            int hr = SHOpenWithDialog(owner, ref info);
            if (hr != 0 && hr != HresultCancelled) Marshal.ThrowExceptionForHR(hr);
        }

        private static string? Query(AssocStr what)
        {
            uint length = 1024;
            var buffer = new StringBuilder((int)length);
            int hr = AssocQueryString(AssocfInitIgnoreUnknown, what, Extension, null, buffer, ref length);
            if (hr != 0 || buffer.Length == 0) return null;
            return buffer.ToString();
        }

        private enum AssocStr
        {
            Executable = 2,
            FriendlyAppName = 4,
        }

        private const int AssocfInitIgnoreUnknown = 0x400;
        private const int OaifAllowRegistration = 0x1;
        private const int OaifRegisterExt = 0x2;
        private const int ShcneAssocChanged = 0x08000000;
        private const int HresultCancelled = unchecked((int)0x800704C7);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenAsInfo
        {
            public string File;
            public string? Class;
            public int Flags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(int flags, AssocStr what, string assoc, string? extra, StringBuilder output, ref uint length);
    }
}
