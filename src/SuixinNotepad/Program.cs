using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
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

            // 安装包请求设为 .txt 默认程序：只弹出系统对话框，不打开窗口
            if (args.Length == 1 && args[0] == DefaultApp.ChooseArgument)
            {
                if (DefaultApp.IsCurrent()) return;
                try
                {
                    DefaultApp.Choose(IntPtr.Zero);
                }
                catch (COMException ex)
                {
                    MessageBox.Show("无法打开默认程序设置：" + ex.Message, MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            string[] files = ParseFiles(args);

            using (SingleInstance? instance = SingleInstance.TryAcquire())
            {
                if (instance == null)
                {
                    if (!SingleInstance.SendToRunningInstance(files))
                        MessageBox.Show(MainForm.AppName + "已在运行，但没有响应。", MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 立即开始接收其他实例转交的文件，窗口就绪前先存起来。
                // 否则启动时弹出的提示框没关之前，其他实例会等不到响应
                var relay = new FileRelay();
                instance.Listen(relay.Receive);

                AppPaths paths = AppPaths.ForCurrentUser();
                AppSettings settings;
                SessionState session;
                TabHistory history;
                string? brokenSettings, brokenSession, brokenHistory;
                try
                {
                    Directory.CreateDirectory(paths.DataRoot);
                    settings = JsonFile.LoadOrDefault(paths.SettingsFile, () => new AppSettings(), out brokenSettings);
                    session = JsonFile.LoadOrDefault(paths.SessionFile, () => new SessionState(), out brokenSession);
                    history = JsonFile.LoadOrDefault(paths.HistoryFile, () => new TabHistory(), out brokenHistory);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 用默认内容继续运行的话，退出时会覆盖用户原有的设置和标签页记录
                    MessageBox.Show("无法读取配置：" + ex.Message + "\r\n\r\n请关闭占用该文件的程序后重新启动。", MainForm.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ReportCrash(paths, e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(paths, (Exception)e.ExceptionObject);

                using (var form = new MainForm(paths, settings, session, history, files))
                {
                    form.Load += (s, e) => relay.Attach(form);
                    form.FormClosed += (s, e) => relay.Detach();
                    form.Shown += (s, e) =>
                    {
                        foreach (string? broken in new[] { brokenSettings, brokenSession, brokenHistory })
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

        /// <summary>
        /// 命令行中的文件按本进程的当前目录解析为完整路径，再交给窗口或已运行的实例。
        /// 不是合法路径的参数（含非法字符、网址等）逐个告知用户后忽略。
        /// </summary>
        private static string[] ParseFiles(string[] args)
        {
            var files = new List<string>();
            var invalid = new List<string>();
            foreach (string arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                try
                {
                    files.Add(Path.GetFullPath(arg));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is SecurityException)
                {
                    invalid.Add(arg);
                }
            }
            if (invalid.Count > 0)
            {
                MessageBox.Show("以下参数不是有效的文件路径，已忽略：\r\n" + string.Join("\r\n", invalid), MainForm.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return files.ToArray();
        }

        /// <summary>把监听线程收到的文件列表交给窗口。窗口就绪前收到的先存起来，窗口关闭后收到的丢弃。</summary>
        private sealed class FileRelay
        {
            private readonly object gate = new object();
            private readonly List<string[]> pending = new List<string[]>();
            private MainForm? target;

            /// <summary>在监听线程上调用，不能抛出异常。</summary>
            public void Receive(string[] files)
            {
                MainForm? form;
                lock (gate)
                {
                    if (target == null)
                    {
                        pending.Add(files);
                        return;
                    }
                    form = target;
                }
                Post(form, files);
            }

            /// <summary>窗口句柄已创建（Load 事件中调用），交付之前存下的文件。</summary>
            public void Attach(MainForm form)
            {
                string[][] queued;
                lock (gate)
                {
                    target = form;
                    queued = pending.ToArray();
                    pending.Clear();
                }
                foreach (string[] files in queued) Post(form, files);
            }

            public void Detach()
            {
                lock (gate) target = null;
            }

            private static void Post(MainForm form, string[] files)
            {
                try
                {
                    form.BeginInvoke(new Action(() => form.OpenFromOtherInstance(files)));
                }
                catch (InvalidOperationException)
                {
                    // 窗口正在关闭，句柄已经销毁
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
