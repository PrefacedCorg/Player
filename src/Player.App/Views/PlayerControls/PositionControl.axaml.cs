using Avalonia.Controls;
using Player.App.ViewModels;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「进度条」控件。PositionBar 内部已处理拖动事件与像素对齐，
/// 这里只把拖动开始/结束转给 ViewModel。
/// </summary>
public partial class PositionControl : UserControl
{
    public PositionControl()
    {
        InitializeComponent();

        // DataContext 在事件发生时再取，避免控件构造顺序影响
        Bar.ScrubStarted += (_, _) => (DataContext as MainWindowViewModel)?.BeginScrub();
        Bar.ScrubCompleted += (_, _) => (DataContext as MainWindowViewModel)?.EndScrub();
    }
}