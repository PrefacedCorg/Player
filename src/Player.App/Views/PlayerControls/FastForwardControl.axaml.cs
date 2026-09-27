using Avalonia.Controls;
using Avalonia.Interactivity;
using Player.App.ViewModels;

namespace Player.App.Views.PlayerControls;

/// <summary>控制栏的「快进」控件：点击按组件设置里的秒数快进（默认 10 秒）。</summary>
public partial class FastForwardControl : UserControl
{
    private readonly PlayerControlItem _item;

    public FastForwardControl(PlayerControlItem item)
    {
        _item = item;
        InitializeComponent();
    }

    /// <summary>预览器/设计时用（工厂始终走带 item 的构造）：默认设置兜底为 10 秒。</summary>
    public FastForwardControl() : this(new PlayerControlItem(PlayerControlKind.FastForward, "快进", new SeekStepControlSettings())) { }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        var seconds = _item.Settings is SeekStepControlSettings settings ? settings.Seconds : 10;
        (DataContext as MainWindowViewModel)?.SeekByCommand.Execute(seconds);
    }
}
