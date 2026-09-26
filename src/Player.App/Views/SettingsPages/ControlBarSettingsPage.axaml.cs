using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    /// <summary>右键菜单"向上移动一行"（行 ListBox 上的命令入口）。</summary>
    [RelayCommand]
    private void MoveLineUp(PlayerControlLine? line) => ViewModel?.MoveLinePrevious(line!);

    /// <summary>右键菜单"向下移动一行"。</summary>
    [RelayCommand]
    private void MoveLineDown(PlayerControlLine? line) => ViewModel?.MoveLineNext(line!);

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

    /// <summary>
    /// 所有控件列表（每行一个 + 子组件视图）共用：把选中的控件交给 ViewModel，供右侧两个标签页使用。
    /// <para>
    /// 只响应"选中了某项"，取消选择不传播：往下滚动页面时行容器会被回收、行内的列表随之卸载并
    /// 清空选择，那种取消不能动 ViewModel——否则高级设置标签页会跟着消失（分组内的控件不受影响，
    /// 因为它们显示在行模板之外的子组件视图列表里）。
    /// </para>
    /// </summary>
    private void SelectorComponents_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel
            || sender is not ListBox listBox
            || listBox.SelectedItem is not PlayerControlItem item)
        {
            return;
        }

        viewModel.SelectedItem = item;

        // 每行是独立的列表、子组件视图又是一个：清掉其它列表里的高亮，避免多行同时有选中项。
        // 被清的列表会以 SelectedItem=null 再进来一次，直接被上面的判空挡住，不会递归。
        foreach (var other in this.GetVisualDescendants().OfType<ListBox>()
                     .Where(static l => l.Classes.Contains("component-listBox"))
                     .Where(l => !ReferenceEquals(l, listBox)))
        {
            other.SelectedItem = null;
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

    /// <summary>行上的"在下方插入一行"按钮。</summary>
    private void ButtonInsertLineBelow_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PlayerControlLine line })
        {
            ViewModel?.AddLine(line);
        }
    }

    /// <summary>行上的"删除行"按钮。</summary>
    private void ButtonRemoveLine_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PlayerControlLine line })
        {
            ViewModel?.RemoveLine(line);
        }
    }
}