using System.Collections.Specialized;

namespace Player.App;

/// <summary>
/// 控制栏布局监视器：递归订阅控件列表与各级容器的子控件列表，任何一层增删子控件时触发重建。
/// 重建前先整体退订，避免重建过程中集合变化引发的重入。
/// </summary>
public sealed class ControlBarLayoutWatcher(Action onChanged)
{
    private readonly List<INotifyCollectionChanged> _tracked = [];

    /// <summary>订阅一棵布局树（含各级容器的子控件列表）。</summary>
    public void Track(IEnumerable<PlayerControlItem> items)
    {
        Untrack();
        foreach (var item in items)
        {
            TrackItem(item);
        }
    }

    /// <summary>退订全部已订阅的集合。</summary>
    public void Untrack()
    {
        foreach (var collection in _tracked)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }

        _tracked.Clear();
    }

    private void TrackItem(PlayerControlItem item)
    {
        if (item.Children is not { } children)
        {
            return;
        }

        children.CollectionChanged += OnCollectionChanged;
        _tracked.Add(children);
        foreach (var child in children)
        {
            TrackItem(child);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => onChanged();
}