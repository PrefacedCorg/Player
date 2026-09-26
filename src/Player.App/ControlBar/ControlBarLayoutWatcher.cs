using System.Collections.Specialized;

namespace Player.App;

/// <summary>
/// 控制栏布局监视器：订阅行列表、每行的控件列表与各级容器的子控件列表，
/// 任何一层增删（加行、删行、行内加删控件、容器里加删子控件）时触发重建。
/// 重建前先整体退订，避免重建过程中集合变化引发的重入。
/// </summary>
public sealed class ControlBarLayoutWatcher(Action onChanged)
{
    private readonly List<INotifyCollectionChanged> _tracked = [];

    /// <summary>订阅整棵布局树：行集合 → 每行的控件列表 → 各级容器的子控件列表。</summary>
    public void Track(System.Collections.ObjectModel.ObservableCollection<PlayerControlLine> lines)
    {
        Untrack();
        lines.CollectionChanged += OnCollectionChanged;
        _tracked.Add(lines);
        foreach (var line in lines)
        {
            line.Children.CollectionChanged += OnCollectionChanged;
            _tracked.Add(line.Children);
            foreach (var item in line.Children)
            {
                TrackItem(item);
            }
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
