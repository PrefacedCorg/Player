using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Player.App.ViewModels;

namespace Player.App.Views.SettingsPages;

/// <summary>
/// 控制栏设置页。控件行的操作命令放在页面代码后置上（样式里的右键菜单用
/// <c>$parent[pages:ControlBarSettingsPage].XxxCommand</c> 绑定）——与 ClassIsland 把
/// CreateContainerComponent、MoveToCurrentContainerComponent 等命令放在 ComponentsSettingsPage 上的做法一致。
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

    /// <summary>右键菜单"包裹到新容器"：参数是选中的容器类型条目，被包裹的是当前选中的控件。</summary>
    [RelayCommand]
    private void CreateContainerComponent(PlayerControlLibraryEntry? entry)
    {
        if (entry is not null && ViewModel?.SelectedItem is { } item)
        {
            ViewModel.WrapIntoContainer(item, entry.Kind);
        }
    }

    /// <summary>右键菜单"移动到选中的容器"。</summary>
    [RelayCommand]
    private void MoveToCurrentContainer(PlayerControlItem? item) => ViewModel?.MoveToCurrentContainer(item!);

    /// <summary>右键菜单"从容器组件移出"。</summary>
    [RelayCommand]
    private void MoveComponentsToMainLines(PlayerControlItem? item) => ViewModel?.MoveOutOfCurrentContainer(item!);

    /// <summary>行上的"查看子组件"按钮。</summary>
    private void ButtonShowChildrenComponents_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PlayerControlItem item })
        {
            ViewModel?.EnterContainer(item);
        }
    }

    /// <summary>两个列表（控制栏 / 容器子控件）共用：把选中的控件交给 ViewModel，供右侧两个标签页使用。</summary>
    private void SelectorComponents_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is ListBox listBox)
        {
            viewModel.SelectedItem = listBox.SelectedItem as PlayerControlItem;
        }
    }

    /// <summary>子组件视图的"返回上一层级"。</summary>
    private void ButtonNavigateUp_OnClick(object? sender, RoutedEventArgs e) => ViewModel?.NavigateBack();

    /// <summary>子组件视图的"关闭"。</summary>
    private void ButtonChildrenViewClose_OnClick(object? sender, RoutedEventArgs e) => ViewModel?.CloseChildrenView();

    /// <summary>选中行上的红色删除按钮。</summary>
    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PlayerControlItem item })
        {
            ViewModel?.Remove(item);
        }
    }
}