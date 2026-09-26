using System.Collections.Specialized;
using Avalonia.Controls;
using Player.App.Controls;
using Player.App.ViewModels;
using Player.Platform;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏宿主：控件不写死在 XAML 里，而是按 <see cref="PlayerSettings.ControlBar"/> 的行列表逐行生成
/// （容器型控件会连同容器里的子控件一起生成），因此"控制栏里有几行、每行放哪些控件、
/// 按什么顺序、容器里放什么"都能在设置页里拖出来，改完立即重建，不需要重启。
/// <para>
/// 每行用 <see cref="PlayerControlLinePanel"/> 排布：居左靠行首、居右靠行尾、
/// <b>居中是整行居中</b>（连续打包后整体中心对准整行的水平中心，左右内容宽度变化不影响）、
/// 拉伸占满整行——第二行放三个分组容器（左对齐 / 居中 / 右对齐）即可实现
/// 左 / 中（正中间）/ 右 三栏。
/// </para>
/// </summary>
public partial class PlayerControlBar : UserControl
{
    public PlayerControlBar()
    {
        InitializeComponent();
        _watcher = new ControlBarLayoutWatcher(Rebuild);
        DataContextChanged += (_, _) => HookSettings();
        DetachedFromVisualTree += (_, _) => UnhookSettings();
    }

    private readonly ControlBarLayoutWatcher _watcher;
    private PlayerSettings? _settings;

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

    /// <summary>按当前布局重建控制栏（每行一个行面板，容器里的子控件由工厂递归生成）。</summary>
    private void Rebuild()
    {
        _watcher.Untrack();
        Root.Children.Clear();

        if (_settings is null)
        {
            return;
        }

        foreach (var line in _settings.ControlBar)
        {
            Root.Children.Add(BuildLine(line));
        }

        // 行集合、每行的控件列表、各级容器里的子控件增删都要重建（监视整棵布局树）
        _watcher.Track(_settings.ControlBar);

        // 落一条布局日志：拖拽排序/增删的结果一眼可查，也便于事后追溯
        StartupTrace.Mark("控制栏布局：" + string.Join(
            " | ",
            _settings.ControlBar.Select(line => string.Join(" / ", line.Children.Select(Describe)))));
    }

    /// <summary>一行：行面板按对齐整行排布（居左靠行首、居中打包对准整行中心、居右靠行尾、拉伸占满）。</summary>
    private PlayerControlLinePanel BuildLine(PlayerControlLine line)
    {
        var panel = new PlayerControlLinePanel();
        foreach (var item in line.Children)
        {
            panel.Children.Add(PlayerControlFactory.Build(item, DataContext));
        }

        return panel;
    }

    /// <summary>布局日志用：容器把它的子控件写在方括号里。</summary>
    private static string Describe(PlayerControlItem item) =>
        item.Children is { Count: > 0 } children
            ? $"{item.Title}[{string.Join(" ", children.Select(Describe))}]"
            : item.Title;
}
