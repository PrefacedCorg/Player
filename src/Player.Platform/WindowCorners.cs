using System.Runtime.InteropServices;

namespace Player.Platform;

/// <summary>
/// 窗口圆角查询（Win11）：主窗口四角是圆角，悬浮控制栏贴着窗口底边显示，底部圆角必须和它对齐，
/// 否则四角会“突出”到窗口圆角外面。
/// Win10 及更早没有 <c>DWMWA_WINDOW_CORNER_PREFERENCE</c> 属性，查询直接失败 → 按直角返回 0；
/// 也就是说各系统各自成立，不需要按版本号写分支。
/// </summary>
public static class WindowCorners
{
    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE 的属性号（Win11 起可用，Win10 上调用失败）。</summary>
    private const int WindowCornerPreference = 33;

    /// <summary>查询窗口圆角半径（DIP，Win11 普通窗口默认 8）；直角或不支持的系统返回 0。</summary>
    public static double GetRadiusDip(IntPtr windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero)
        {
            return 0d;
        }

        try
        {
            if (DwmGetWindowAttribute(windowHandle, WindowCornerPreference, out var preference, sizeof(int)) != 0)
            {
                // Win10：属性不存在（调用失败）→ 窗口本来就是直角
                return 0d;
            }

            return preference switch
            {
                1 => 0d, // DWMWCP_DONOTROUND
                3 => 4d, // DWMWCP_ROUNDSMALL
                _ => 8d, // DWMWCP_DEFAULT / DWMWCP_ROUND：Win11 普通窗口的默认圆角
            };
        }
        catch
        {
            // 查询失败按直角处理，不能影响主流程
            return 0d;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}