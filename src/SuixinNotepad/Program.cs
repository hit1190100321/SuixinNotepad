using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using SuixinNotepad.Core;
using SuixinNotepad.UI;

namespace SuixinNotepad
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 相对路径按本进程的当前目录解析后再交给已运行的实例
            string[] files = args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(Path.GetFullPath).ToArray();

            using (SingleInstance? instance = SingleInstance.TryAcquire())
            {
                if (instance == null)
                {
                    if (!SingleInstance.SendToRunningInstance(files))
                        MessageBox.Show(MainForm.AppName + "已在运行，但没有响应。", MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                AppPaths paths = AppPaths.ForCurrentUser();
                Directory.CreateDirectory(paths.DataRoot);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ReportCrash(paths, e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(paths, (Exception)e.ExceptionObject);

                AppSettings settings = JsonFile.LoadOrDefault(paths.SettingsFile, () => new AppSettings(), out string? brokenSettings);
                SessionState session = JsonFile.LoadOrDefault(paths.SessionFile, () => new SessionState(), out string? brokenSession);

                using (var form = new MainForm(paths, settings, session, files))
                {
                    form.Load += (s, e) => instance.Listen(received =>
                    {
                        if (form.IsDisposed || !form.IsHandleCreated) return;
                        form.BeginInvoke(new Action(() => form.OpenFromOtherInstance(received)));
                    });
                    form.Shown += (s, e) =>
                    {
                        foreach (string? broken in new[] { brokenSettings, brokenSession })
                        {
                            if (broken != null)
                                MessageBox.Show(form, "配置文件已损坏，已改用默认内容。原文件保留为：\r\n" + broken, MainForm.AppName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    };
                    Application.Run(form);
                }
            }
        }

        private static int reporting;

        /// <summary>记录未处理的异常并告知用户。已打开的内容由自动保存保护。</summary>
        private static void ReportCrash(AppPaths paths, Exception ex)
        {
            if (Interlocked.Exchange(ref reporting, 1) == 1) return;
            try
            {
                string log = Path.Combine(paths.DataRoot, "error.log");
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n", Encoding.UTF8);
                MessageBox.Show("程序出现错误：" + ex.Message + "\r\n\r\n详细信息已记录到：\r\n" + log, MainForm.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Interlocked.Exchange(ref reporting, 0);
            }
        }
    }
}
