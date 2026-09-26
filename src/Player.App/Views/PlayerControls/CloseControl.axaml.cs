using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「关闭」控件：关闭应用。窗口操作属于主窗口的职责，
/// 与「设置」控件一样交给主窗口的方法处理。
/// </summary>
public partial class CloseControl : UserControl
{
    public CloseControl() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window)
        {
            window.CloseApp();
        }
    }
}
