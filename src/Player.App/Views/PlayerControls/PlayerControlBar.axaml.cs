using System.Collections.Specialized;
using Avalonia.Controls;
using Player.App.Controls;
using Player.App.ViewModels;
using Player.Platform;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏宿主：控件不写死在 XAML 里，而是按 <see cref="PlayerSettings.ControlBar"/> 的顺序递归生成
/// （容器型控件会连同容器里的子控件一起生成），因此"控制栏里放哪些控件、按什么顺序、容器里放什么"
/// 都能在设置页里拖出来，改完立即重建，不需要重启。
/// <para>
/// 进度条那一项用星形列占剩余宽度，其余按内容宽度排——这样别的控件宽度变化不会把进度条挤没。
/// </para>
/// </summary>
public partial class PlayerControlBar : UserControl
{
    private readonly ControlBarLayoutWatcher _watcher;
    private PlayerSettings? _settings;

    public PlayerControlBar()
    {
        InitializeComponent();
        _watcher = new ControlBarLayoutWatcher(Rebuild);
        DataContextChanged += (_, _) => HookSettings();
        DetachedFromVisualTree += (_, _) => UnhookSettings();
    }

    private void HookSettings()
    {
        var settings = (DataContext as MainWindowViewModel)?.Settings;
        if (ReferenceEquals(settings, _settings))
        {
            return;
        }

        UnhookSettings();
        _settings = settings;

        if (_settings is null)
        {
            return;
        }

        _settings.ControlBar.CollectionChanged += OnLayoutChanged;
        Rebuild();
    }

    private void UnhookSettings()
    {
        if (_settings is not null)
        {
            _settings.ControlBar.CollectionChanged -= OnLayoutChanged;
        }

        _watcher.Untrack();
        _settings = null;
    }

    private void OnLayoutChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>按当前布局重建控制行（容器里的子控件由工厂递归生成）。</summary>
    private void Rebuild()
    {
        _watcher.Untrack();
        Root.Children.Clear();
        Root.ColumnDefinitions.Clear();

        if (_settings is null)
        {
            return;
        }

        var column = 0;
        foreach (var item in _settings.ControlBar)
        {
            var control = PlayerControlFactory.Build(item, DataContext);

            Root.ColumnDefinitions.Add(new ColumnDefinition(
                item.Kind == PlayerControlKind.Position ? GridLength.Star : GridLength.Auto));
            Grid.SetColumn(control, column);
            Root.Children.Add(control);
            column++;
        }

        // 容器里的子控件增删也要重建（监视整棵布局树）
        _watcher.Track(_settings.ControlBar);

        // 落一条布局日志：拖拽排序/增删的结果一眼可查，也便于事后追溯
        StartupTrace.Mark("控制栏布局：" + string.Join(" / ", _settings.ControlBar.Select(Describe)));
    }

    /// <summary>布局日志用：容器把它的子控件写在方括号里。</summary>
    private static string Describe(PlayerControlItem item) =>
        item.Children is { Count: > 0 } children
            ? $"{item.Title}[{string.Join(" ", children.Select(Describe))}]"
            : item.Title;
}