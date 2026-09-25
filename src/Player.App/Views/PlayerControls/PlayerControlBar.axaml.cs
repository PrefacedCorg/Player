using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Layout;
using Player.App.ViewModels;
using Player.Platform;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏宿主：控件不写死在 XAML 里，而是按 <see cref="PlayerSettings.ControlBar"/> 的顺序动态生成，
/// 因此"控制栏里放哪些控件、按什么顺序"都能在设置页里拖出来，改完立即重建，不需要重启。
/// <para>
/// 进度条那一项用星形列占剩余宽度，其余按内容宽度排——这样别的控件宽度变化不会把进度条挤没。
/// </para>
/// </summary>
public partial class PlayerControlBar : UserControl
{
    private PlayerSettings? _settings;

    public PlayerControlBar()
    {
        InitializeComponent();

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

        _settings = null;
    }

    private void OnLayoutChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>按当前布局重建控制行。</summary>
    private void Rebuild()
    {
        Root.Children.Clear();
        Root.ColumnDefinitions.Clear();

        if (_settings is null)
        {
            return;
        }

        var column = 0;
        foreach (var item in _settings.ControlBar)
        {
            if (CreateControl(item.Kind) is not { } control)
            {
                continue;
            }

            Root.ColumnDefinitions.Add(new ColumnDefinition(
                item.Kind == PlayerControlKind.Position ? GridLength.Star : GridLength.Auto));
            Grid.SetColumn(control, column);
            Root.Children.Add(control);
            column++;
        }

        // 落一条布局日志：拖拽排序/增删的结果一眼可查，也便于事后追溯
        StartupTrace.Mark("控制栏布局：" + string.Join(" / ", _settings.ControlBar.Select(static item => item.Title)));
    }

    /// <summary>控件工厂：新增一种控制栏控件就在这里加一行，对应 Views/PlayerControls 下的一个 UserControl。</summary>
    private static Control? CreateControl(PlayerControlKind kind) => kind switch
    {
        PlayerControlKind.OpenFile => new OpenFileControl(),
        PlayerControlKind.PlayPause => new PlayPauseControl(),
        PlayerControlKind.Stop => new StopControl(),
        PlayerControlKind.Position => new PositionControl { VerticalAlignment = VerticalAlignment.Center },
        PlayerControlKind.TimeDisplay => new TimeDisplayControl(),
        PlayerControlKind.Volume => new VolumeControl(),
        PlayerControlKind.Renderer => new RendererControl(),
        PlayerControlKind.Settings => new SettingsControl(),
        _ => null,
    };
}