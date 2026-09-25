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
/// 落点可以是控制栏本身，也可以是某个容器里的子控件列表：跨列表拖动会把控件从源列表移到目标列表，
/// 并且禁止把容器拖进它自己或它的子级里。
/// </para>
/// </summary>
public class ControlBarDropHandler(ControlBarSettingsViewModel viewModel) : DropHandlerBase, IDragHandler
{
    public ControlBarSettingsViewModel ViewModel { get; } = viewModel;

    private ListBox? _sourceListBox;
    private ListBoxItem? _sourceListBoxItem;
    private ListBox? _targetListBox;

    /// <summary>落点索引：命中某一行时看指针在该行左半还是右半，落空则按列表中点决定首尾。</summary>
    private static (int index, bool found) GetTargetIndex(
        ListBox listBox, DragEventArgs e, IList<PlayerControlItem> items)
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
        e.DragEffects = GetEffects(sourceContext, targetContext);
    }

    private DragDropEffects GetEffects(object? sourceContext, object? targetContext)
    {
        if (targetContext is not ObservableCollection<PlayerControlItem> target)
        {
            return DragDropEffects.None;
        }

        return sourceContext switch
        {
            // 与 ClassIsland 一致：组件库来的 = 复制，已在控制栏里的 = 移动
            PlayerControlLibraryEntry => DragDropEffects.Copy,
            PlayerControlDragData { Item: { } item } when ViewModel.CanDropInto(item, target) => DragDropEffects.Move,
            _ => DragDropEffects.None
        };
    }

    public override void Drop(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        if (sender is not ListBox listBox || targetContext is not ObservableCollection<PlayerControlItem> target)
        {
            return;
        }

        _targetListBox = listBox;
        var (targetIndex, foundTargetIndex) = GetTargetIndex(listBox, e, target);
        var insertIndex = foundTargetIndex ? targetIndex + 1 : target.Count;

        switch (sourceContext)
        {
            case PlayerControlLibraryEntry entry:
                // 组件库来的 = 复制一份放进目标列表（容器条目也一样，可以放进容器里）
                InsertItem(target, new PlayerControlItem(entry.Kind, entry.Title), insertIndex);
                break;

            case PlayerControlDragData { Item: { } item, SourceList: { } sourceList }:
                if (!ViewModel.CanDropInto(item, target))
                {
                    return;
                }

                if (ReferenceEquals(sourceList, target))
                {
                    // 同一个列表里移动：沿用原来的索引补偿逻辑
                    var sourceIndex = target.IndexOf(item);
                    if (sourceIndex < 0)
                    {
                        return;
                    }

                    var moveIndex = foundTargetIndex ? targetIndex : target.Count - 1;
                    var newIndex = sourceIndex > moveIndex ? moveIndex + 1 : moveIndex;
                    MoveItem(target, sourceIndex, Math.Clamp(newIndex, 0, target.Count - 1));
                }
                else
                {
                    // 跨列表（控制栏 ↔ 容器）：先从源列表移除，再插进目标列表
                    sourceList.Remove(item);
                    InsertItem(target, item, Math.Clamp(insertIndex, 0, target.Count));
                }

                break;
        }
    }

    public override bool Validate(
        object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state) =>
        GetEffects(sourceContext, targetContext) != DragDropEffects.None;

    public void BeforeDragDrop(object? sender, PointerEventArgs e, object? context)
    {
        if (sender is not ListBoxItem item)
        {
            return;
        }

        _sourceListBoxItem = item;
        _sourceListBox = item.FindAncestorOfType<ListBox>();
    }

    public void AfterDragDrop(object? sender, PointerEventArgs e, object? context)
    {
        ClearTransform(_sourceListBoxItem);
        ClearListTransforms(_sourceListBox);
        ClearListTransforms(_targetListBox);

        _sourceListBoxItem = null;
        _sourceListBox = null;
        _targetListBox = null;
    }

    private static void ClearListTransforms(ListBox? listBox)
    {
        foreach (var control in listBox?.Items
                     .OfType<object>()
                     .Select(x => listBox.ContainerFromItem(x)) ?? [])
        {
            ClearTransform(control);
        }
    }

    private static void ClearTransform(Control? control)
    {
        control?.SetValue(Visual.RenderTransformProperty, new TransformGroup());
    }
}