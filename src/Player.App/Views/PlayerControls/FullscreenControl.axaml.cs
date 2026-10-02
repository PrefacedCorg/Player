using Avalonia.Controls;
using Avalonia.Interactivity;
using Player.App.Assists;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「全屏」控件：进入 / 退出全屏。切换动作交给主窗口的方法，
/// 按钮文本跟随 ViewModel 的 IsFullscreen（主窗口由 WindowState 同步过去）。
/// 主窗口经 MainWindowLocator 定位：停靠栏里就是宿主窗口，悬浮栏里是触摸层窗口的 Owner。
/// </summary>
public partial class FullscreenControl : UserControl
{
    public FullscreenControl() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e) =>
        MainWindowLocator.Find(this)?.ToggleFullscreen();
}
