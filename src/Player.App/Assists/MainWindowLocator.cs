using Avalonia;
using Avalonia.Controls;
using Player.App.Views;

namespace Player.App.Assists;

/// <summary>
/// 定位控件所属的主窗口。停靠控制栏直接挂在主窗口里，顶层就是 <see cref="MainWindow"/>；
/// 悬浮控制栏在视频区触摸层（<see cref="VideoTouchOverlay"/>，独立顶层窗口）里，
/// 顶层不是 MainWindow，但它以主窗口为 Owner（Show(mainWindow)），据此找回主窗口——
/// 「全屏 / 最小化 / 关闭 / 设置」这些窗口级控件在悬浮栏上也要能用。
/// </summary>
public static class MainWindowLocator
{
    /// <summary>找控件所在层级里的主窗口（找不到返回 null）。</summary>
    public static MainWindow? Find(Visual visual) => TopLevel.GetTopLevel(visual) switch
    {
        MainWindow window => window,
        Window window => window.Owner as MainWindow,
        _ => null,
    };
}