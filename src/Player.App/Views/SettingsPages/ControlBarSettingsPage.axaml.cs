using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Player.App.ViewModels;

namespace Player.App.Views.SettingsPages;

/// <summary>
/// 控制栏设置页。行操作命令放在页面代码后置上（样式里的右键菜单用
/// <c>$parent[pages:ControlBarSettingsPage].XxxCommand</c> 绑定）——与 ClassIsland
/// 把 MoveComponentToPreviousLineCommand 等命令放在 ComponentsSettingsPage 上的做法一致。
/// 删除走 Click 处理器（他们的 ButtonRemoveSelectedComponent_OnClick 同款）。
/// </summary>
public partial class ControlBarSettingsPage : UserControl
{
    public ControlBarSettingsPage() => InitializeComponent();

    private ControlBarSettingsViewModel? ViewModel => DataContext as ControlBarSettingsViewModel;

    [RelayCommand]
    private void MovePrevious(PlayerControlItem? item) => ViewModel?.MovePrevious(item!);

    [RelayCommand]
    private void MoveNext(PlayerControlItem? item) => ViewModel?.MoveNext(item!);

    [RelayCommand]
    private void Duplicate(PlayerControlItem? item) => ViewModel?.Duplicate(item!);

    /// <summary>选中行上的红色删除按钮。</summary>
    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PlayerControlItem item })
        {
            ViewModel?.Remove(item);
        }
    }
}