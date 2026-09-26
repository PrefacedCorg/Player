using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Player.App.Views.PlayerControls;

/// <summary>控制栏的「最小化」控件：最小化主窗口（窗口操作交给主窗口的方法处理）。</summary>
public partial class MinimizeControl : UserControl
{
    public MinimizeControl() => InitializeComponent();

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window)
        {
            window.MinimizeWindow();
        }
    }
}
