using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SuixinNotepad.Core
{
    /// <summary>
    /// 单实例：同一用户会话只运行一个随心记事本。后启动的进程把要打开的文件
    /// 通过命名管道交给已运行的实例后退出，避免两个进程同时改写会话与文件。
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = "SuixinNotepad.Running";

        private readonly Mutex mutex;
        private readonly string pipeName;
        private Thread? listener;
        private volatile bool disposed;

        private SingleInstance(Mutex mutex, string pipeName)
        {
            this.mutex = mutex;
            this.pipeName = pipeName;
        }

        /// <summary>取得单实例所有权；已有实例在运行时返回 null。</summary>
        public static SingleInstance? TryAcquire()
        {
            var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstance(mutex, PipeName());
        }

        /// <summary>把文件列表交给已运行的实例。返回 false 表示对方没有响应。</summary>
        public static bool SendToRunningInstance(string[] paths)
        {
            AllowSetForegroundWindow(-1);
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.Out))
                {
                    client.Connect(5000);
                    byte[] payload = Encoding.UTF8.GetBytes(string.Join("\n", paths));
                    client.Write(payload, 0, payload.Length);
                    client.Flush();
                }
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>开始监听。收到的文件列表在后台线程回调，调用方负责切回界面线程。</summary>
        public void Listen(Action<string[]> onReceived)
        {
            listener = new Thread(() => ListenLoop(onReceived)) { IsBackground = true, Name = "SingleInstancePipe" };
            listener.Start();
        }

        private void ListenLoop(Action<string[]> onReceived)
        {
            while (!disposed)
            {
                using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte))
                {
                    server.WaitForConnection();
                    if (disposed) return;
                    using (var ms = new MemoryStream())
                    {
                        server.CopyTo(ms);
                        string text = Encoding.UTF8.GetString(ms.ToArray());
                        string[] paths = text.Length == 0 ? new string[0] : text.Split('\n');
                        onReceived(paths);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // 用一次空连接唤醒阻塞在 WaitForConnection 的监听线程
            if (listener != null)
            {
                try
                {
                    using (var wake = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                        wake.Connect(200);
                }
                catch (TimeoutException) { }
                catch (IOException) { }
            }
            mutex.ReleaseMutex();
            mutex.Dispose();
        }

        private static string PipeName() =>
            "SuixinNotepad." + Environment.UserName + "." + Process.GetCurrentProcess().SessionId;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
