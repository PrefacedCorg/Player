using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.DragAndDrop;
using Player.App.ViewModels;
using Visual = Avalonia.Visual;

namespace Player.App.Views.SettingsPages;

/// <summary>
/// 控制栏设置页的拖放处理器。结构与 ClassIsland 的 ComponentsSettingsPageDropHandler 一致：
/// 落点索引按指针落在目标的左半/右半决定、载荷类型决定是复制（组件库）还是移动（控制栏内），
/// 并在拖动结束时清掉行上的 RenderTransform。
/// <para>
/// 唯一的本地增补：拖到组件库上时把控件从控制栏移除——ClassIsland 用右键菜单"移除"，
/// 我们没有那个菜单，因此把"拖回组件库"作为移除入口。
/// </para>
/// </summary>
public class ControlBarDropHandler(ControlBarSettingsViewModel viewModel) : DropHandlerBase, IDragHandler
{
    public ControlBarSettingsViewModel ViewModel { get; } = viewModel;

    private ObservableCollection<PlayerControlItem>? _sourceCollection;
    private ListBox? _sourceListBox;
    private ListBoxItem? _sourceListBoxItem;

    /// <summary>落点索引：命中某一行时看指针在该行左半还是右半，落空则按列表中点决定首尾。</summary>
    private static (int index, bool found) GetTargetIndex(
        ListBox listBox, DragEventArgs e, IList<PlayerControlItem> items, ListBoxItem? explicitTarget)
    {
        var pos = e.GetPosition(listBox);

        if (listBox.GetVisualAt(pos) is Control targetControl
            && targetControl.FindAncestorOfType<ListBoxItem>() is { } listBoxItem
            && listBoxItem.DataContext is PlayerControlItem targetItem)
        {
            var rPos = e.GetPosition(listBoxItem);
            var index = items.IndexOf(targetItem);
            if (index >= 0)
            {
                return (rPos.X <= listBoxItem.Bounds.Width / 2 ? index - 1 : index, true);
            }
        }

        var half = pos.X > listBox.Bounds.Width / 2;
        return (items.Count > 0 ? (half ? items.Count - 1 : -1) : -1, items.Count > 0);
    }

    public override void Enter(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        e.DragEffects = sourceContext switch
        {
            // 与 ClassIsland 一致：组件库来的 = 复制，已在控制栏里的 = 移动
            PlayerControlLibraryEntry => DragDropEffects.Copy,
            PlayerControlDragData => DragDropEffects.Move,
            _ => DragDropEffects.None
        };
    }

    public override void Drop(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        if (sender is not ListBox listBox || targetContext is not ObservableCollection<PlayerControlItem> components)
        {
            return;
        }

        var (targetIndex, foundTargetIndex) = GetTargetIndex(listBox, e, components, null);
        var insertIndex = foundTargetIndex ? targetIndex + 1 : components.Count;

        switch (sourceContext)
        {
            case PlayerControlLibraryEntry entry:
                // 组件库来的 = 复制一份放进控制栏
                InsertItem(components, new PlayerControlItem(entry.Kind, entry.Title), insertIndex);
                break;

            case PlayerControlDragData { Item: { } item }:
                var sourceIndex = components.IndexOf(item);
                if (sourceIndex < 0)
                {
                    return;
                }

                var moveIndex = foundTargetIndex ? targetIndex : components.Count - 1;
                var newIndex = sourceIndex > moveIndex ? moveIndex + 1 : moveIndex;
                MoveItem(components, sourceIndex, Math.Clamp(newIndex, 0, components.Count - 1));
                break;
        }
    }

    public override bool Validate(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state) =>
        sourceContext is PlayerControlLibraryEntry or PlayerControlDragData;

    public void BeforeDragDrop(object? sender, PointerEventArgs e, object? context)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        _sourceListBoxItem = item;
        var listBox = _sourceListBox = item.FindAncestorOfType<ListBox>();
        if (listBox?.ItemsSource is ObservableCollection<PlayerControlItem> collection)
        {
            _sourceCollection = collection;
        }
    }

    public void AfterDragDrop(object? sender, PointerEventArgs e, object? context)
    {
        ClearTransform(_sourceListBoxItem);

        foreach (var control in _sourceListBox?.Items
                     .OfType<object>()
                     .Select(x => _sourceListBox.ContainerFromItem(x)) ?? [])
        {
            ClearTransform(control);
        }

        _sourceCollection = null;
        _sourceListBoxItem = null;
        _sourceListBox = null;
    }

    private static void ClearTransform(Control? control)
    {
        control?.SetValue(Visual.RenderTransformProperty, new TransformGroup());
    }
}