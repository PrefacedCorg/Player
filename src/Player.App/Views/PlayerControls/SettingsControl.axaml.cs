using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「设置」控件：打开设置窗口。
/// 设置窗口是独立顶层窗口，期间要撤掉视频区透明触摸层——那是主窗口的职责，
/// 所以这里交给主窗口的方法，而不是自己 new 一个窗口。
/// </summary>
public partial class SettingsControl : UserControl
{
    public SettingsControl() => InitializeComponent();

    private async void OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window)
        {
            await window.OpenSettingsAsync();
        }
    }
}