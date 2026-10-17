using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuixinNotepad.UI
{
    /// <summary>
    /// 按系统 DPI 换算界面尺寸。程序以系统级 DPI 感知运行，此时 .NET Framework 的
    /// DeviceDpi / LogicalToDeviceUnits 不做换算，代码中写的像素尺寸统一经过这里。
    /// </summary>
    internal static class Dpi
    {
        public static readonly float Factor = ReadFactor();

        public static int Scale(int logical) => (int)Math.Round(logical * Factor);

        public static Size Scale(int width, int height) => new Size(Scale(width), Scale(height));

        public static Padding Scale(Padding p) => new Padding(Scale(p.Left), Scale(p.Top), Scale(p.Right), Scale(p.Bottom));

        private static float ReadFactor()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                return g.DpiX / 96f;
            }
        }
    }
}
