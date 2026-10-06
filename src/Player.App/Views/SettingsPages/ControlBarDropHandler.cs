using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactions.DragAndDrop;
using Player.App.Behaviors;
using Player.App.ViewModels;

namespace Player.App.Views.SettingsPages;

/// <summary>
/// 控制栏设置页的拖放处理器：落点索引按「被拖拽控件整体的中心」落在哪两个控件的中心之间决定，
/// 载荷类型决定是复制（组件库）还是移动（控制栏内）。
/// <para>
/// 避让采用「空洞」模型：被拖控件在源列表里留下一个洞，洞随拖拽移动到目标缝隙，
/// 沿途控件整体向洞的原位置平移一格，因此方向始终单一、不会左右横跳。
/// </para>
/// <para>
/// 落点可以是控制栏本身，也可以是某个容器里的子控件列表：跨列表拖动会把控件从源列表移到目标列表，
/// 并且禁止把容器拖进它自己或它的子级里。
/// </para>
/// </summary>
public class ControlBarDropHandler(ControlBarSettingsViewModel viewModel) : DropHandlerBase, IDragHandler
{
    /// <summary>默认控件宽度（拿不到真实宽度时的兜底）。</summary>
    private const double FallbackControlWidth = 60;

    public ControlBarSettingsViewModel ViewModel { get; } = viewModel;

    private ListBox? _sourceListBox;
    private ListBoxItem? _sourceListBoxItem;
    private ListBox? _targetListBox;
    private ListBox? _lastDodgedListBox;
    private int _lastDodgeGap = -1;
    private int _committedGap = -1;
    private ListBox? _committedGapListBox;
    private double _sourceWidth;
    private double _sourceGrabOffsetX = -1;

    /// <summary>被拖控件宽度（拿不到时用兜底值）。</summary>
    private double DraggedWidth => _sourceWidth > 0 ? _sourceWidth : FallbackControlWidth;

    /// <summary>迟滞余量：越界这么多像素才真正切换缝隙，避免在边界上左右横跳。</summary>
    private double HysteresisMargin => Math.Max(8, DraggedWidth * 0.3);

    public override void Enter(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        e.DragEffects = GetEffects(sourceContext, targetContext);
        if (sender is ListBox listBox && targetContext is ObservableCollection<PlayerControlItem> target)
        {
            ApplyDodge(listBox, target, e);
        }
    }

    public override void Over(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        e.DragEffects = GetEffects(sourceContext, targetContext);
        if (sender is ListBox listBox && targetContext is ObservableCollection<PlayerControlItem> target)
        {
            ApplyDodge(listBox, target, e);
        }
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
        var gap = Math.Clamp(ResolveGap(listBox, target, GetDraggedCenterX(listBox, e)), 0, target.Count);

        switch (sourceContext)
        {
            case PlayerControlLibraryEntry entry:
                // 组件库来的 = 复制一份放进目标列表（容器条目也一样，可以放进容器里）
                InsertItem(target, new PlayerControlItem(entry.Kind, entry.Title), gap);
                break;

            case PlayerControlDragData { Item: { } item, SourceList: { } sourceList }:
                if (!ViewModel.CanDropInto(item, target))
                {
                    ClearDodgeForList(listBox);
                    return;
                }

                if (ReferenceEquals(sourceList, target))
                {
                    // 同一个列表里移动：缝隙下标就是被拖控件最终所在的位置
                    var sourceIndex = target.IndexOf(item);
                    if (sourceIndex < 0)
                    {
                        ClearDodgeForList(listBox);
                        return;
                    }

                    MoveItem(target, sourceIndex, Math.Clamp(gap, 0, target.Count - 1));
                }
                else
                {
                    // 跨列表（控制栏 ↔ 容器）：先从源列表移除，再插进目标列表
                    sourceList.Remove(item);
                    InsertItem(target, item, Math.Clamp(gap, 0, target.Count));
                }

                break;
        }

        // 落位后清除避让位移
        ClearDodgeForList(listBox);
        ResetDodgeState();
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

        // 记录源控件尺寸与鼠标在控件内的抓取点，用于按「整个控件」计算落点
        _sourceWidth = item.Bounds.Width;
        _sourceGrabOffsetX = e.GetPosition(item).X;

        ResetDodgeState();
    }

    public void AfterDragDrop(object? sender, PointerEventArgs e, object? context)
    {
        if (_sourceListBox is not null)
        {
            ClearDodgeForList(_sourceListBox);
        }

        if (_targetListBox is not null)
        {
            ClearDodgeForList(_targetListBox);
        }

        if (_lastDodgedListBox is not null)
        {
            ClearDodgeForList(_lastDodgedListBox);
        }

        ResetDodgeState();

        _sourceListBoxItem = null;
        _sourceListBox = null;
        _targetListBox = null;
        _sourceWidth = 0;
        _sourceGrabOffsetX = -1;
    }

    private void ResetDodgeState()
    {
        _lastDodgedListBox = null;
        _lastDodgeGap = -1;
        _committedGap = -1;
        _committedGapListBox = null;
    }

    /// <summary>
    /// 被拖控件整体在目标列表坐标系里的中心 X。取不到抓取点时退化为指针位置。
    /// </summary>
    private double GetDraggedCenterX(ListBox listBox, DragEventArgs e)
    {
        var pointerX = e.GetPosition(listBox).X;

        if (_sourceGrabOffsetX < 0 || _sourceWidth <= 0)
        {
            return pointerX;
        }

        // 预览控件跟随指针且保持抓取点相对位置：左边缘 = 指针 - 抓取点偏移
        return pointerX - _sourceGrabOffsetX + _sourceWidth / 2;
    }

    /// <summary>
    /// 计算被拖控件应落入的缝隙下标（0..count）。
    /// 缝隙 g 表示被拖控件最终位于第 g 个位置（g == count 表示落到末尾）。
    /// 判定依据是被拖控件整体的中心越过了哪条缝隙边界（相邻控件中心的中点），
    /// 并带迟滞余量：越界足够多才真正切换缝隙，从而消除左右横跳。
    /// </summary>
    private int ResolveGap(ListBox listBox, IList<PlayerControlItem> items, double draggedCenterX)
    {
        var count = items.Count;
        if (count == 0)
        {
            _committedGap = 0;
            _committedGapListBox = listBox;
            return 0;
        }

        var centers = GetItemCenters(listBox, count);

        var raw = 0;
        for (var k = 1; k <= count; k++)
        {
            if (draggedCenterX >= BoundaryAt(centers, k))
            {
                raw = k;
            }
        }

        if (!ReferenceEquals(_committedGapListBox, listBox))
        {
            _committedGap = raw;
            _committedGapListBox = listBox;
            return _committedGap;
        }

        _committedGap = Math.Clamp(_committedGap, 0, count);

        if (raw > _committedGap)
        {
            // 向右推进：必须越过右侧那条边界一段余量才认
            if (draggedCenterX >= BoundaryAt(centers, _committedGap + 1) + HysteresisMargin)
            {
                _committedGap = raw;
            }
        }
        else if (raw < _committedGap)
        {
            // 向左推进：必须越过左侧那条边界一段余量才认
            if (draggedCenterX < BoundaryAt(centers, _committedGap) - HysteresisMargin)
            {
                _committedGap = raw;
            }
        }

        return _committedGap;
    }

    /// <summary>分隔缝隙 k-1 与缝隙 k 的边界位置（k 取 1..count）。</summary>
    private static double BoundaryAt(double[] centers, int k)
    {
        var count = centers.Length;
        if (k <= 0)
        {
            return double.NegativeInfinity;
        }

        // 末尾那条边界取最后一个控件的中心，这样拖到最后一个控件右侧才算落到末尾
        if (k >= count)
        {
            return centers[count - 1];
        }

        return (centers[k - 1] + centers[k]) / 2;
    }

    /// <summary>
    /// 各条目在 ListBox 坐标系里的中心 X（使用布局坐标，不含 RenderTransform，避免避让位移反馈成抖动）。
    /// </summary>
    private static double[] GetItemCenters(ListBox listBox, int count)
    {
        var centers = new double[count];

        var originX = 0d;
        if (listBox.ContainerFromIndex(0) is Visual first
            && first.GetVisualParent() is Visual panel
            && panel.TranslatePoint(new Point(0, 0), listBox) is { } panelOrigin)
        {
            originX = panelOrigin.X;
        }

        for (var i = 0; i < count; i++)
        {
            if (listBox.ContainerFromIndex(i) is Control container && container.Bounds.Width > 0)
            {
                centers[i] = originX + container.Bounds.X + container.Bounds.Width / 2;
            }
            else
            {
                centers[i] = double.NaN;
            }
        }

        return centers;
    }

    /// <summary>
    /// 按「空洞」模型对目标列表施加避让：被拖控件留下一个洞，洞移动到目标缝隙，
    /// 沿途控件整体朝洞的原位置平移一格。
    /// </summary>
    private void ApplyDodge(ListBox listBox, ObservableCollection<PlayerControlItem> items, DragEventArgs e)
    {
        var gap = ResolveGap(listBox, items, GetDraggedCenterX(listBox, e));

        if (ReferenceEquals(listBox, _lastDodgedListBox) && gap == _lastDodgeGap)
        {
            return;
        }

        if (_lastDodgedListBox is not null && !ReferenceEquals(_lastDodgedListBox, listBox))
        {
            ClearDodgeForList(_lastDodgedListBox);
        }

        ClearDodgeForList(listBox);

        var width = DraggedWidth;
        var sourceIndex = ReferenceEquals(_sourceListBox, listBox)
                          && _sourceListBoxItem?.DataContext is PlayerControlItem sourceItem
            ? items.IndexOf(sourceItem)
            : -1;

        for (var i = 0; i < items.Count; i++)
        {
            if (listBox.ContainerFromIndex(i) is not Control container)
            {
                continue;
            }

            // 源控件自身不参与避让（它已被隐藏）
            if (ReferenceEquals(container, _sourceListBoxItem))
            {
                continue;
            }

            double offset;
            if (sourceIndex >= 0)
            {
                // 洞向右移动：中间的控件向左补位；洞向左移动：中间的控件向右让位
                if (gap > sourceIndex && i > sourceIndex && i <= gap)
                {
                    offset = -width;
                }
                else if (gap < sourceIndex && i >= gap && i < sourceIndex)
                {
                    offset = width;
                }
                else
                {
                    offset = 0;
                }
            }
            else
            {
                // 从别的列表拖进来：落点及其右侧的控件统一向右让位
                offset = i >= gap ? width : 0;
            }

            DragDodgeAnimation.AttachTransition(container);
            DragDodgeAnimation.ApplyDodge(container, offset, 0);
        }

        _lastDodgedListBox = listBox;
        _lastDodgeGap = gap;
    }

    /// <summary>清除指定列表框内所有控件的避让位移。</summary>
    private static void ClearDodgeForList(ListBox? listBox)
    {
        if (listBox?.Items is null)
        {
            return;
        }

        foreach (var item in listBox.Items)
        {
            if (item is null)
            {
                continue;
            }

            if (listBox.ContainerFromItem(item) is Control container)
            {
                DragDodgeAnimation.ClearDodge(container);
            }
        }
    }
}
