using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Player.App.ViewModels;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「进度条」控件。PositionBar 内部已处理拖动事件与像素对齐，
/// 这里把拖动开始/结束转给 ViewModel，并按「组件设置」刷新左右两侧的时间显示。
/// <para>
/// 左右各可选：不显示 / 只总 / 只已过 / 只剩余 / 两两「前 / 后」组合；
/// 时间精度由双头滑块选起止单位（h:m:s:ms，起始单位吸收更高级单位）。
/// </para>
/// </summary>
public partial class PositionControl : UserControl
{
    private readonly PlayerControlItem _item;

    private PositionControlSettings Settings => _item.Settings as PositionControlSettings ?? new PositionControlSettings();

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    public PositionControl(PlayerControlItem item)
    {
        _item = item;
        InitializeComponent();

        // DataContext 在事件发生时再取，避免控件构造顺序影响。
        // 「拖动时预览画面」是组件设置，开始拖动时按它决定要不要让画面跟着手指走
        Bar.ScrubStarted += (_, _) => Vm?.BeginScrub(Settings.ShowScrubPreview);
        Bar.ScrubCompleted += (_, _) => Vm?.EndScrub();

        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetached;
    }

    /// <summary>预览器/设计时用（工厂始终走带 item 的构造）：默认设置已过/总 + 只剩余。</summary>
    public PositionControl() : this(new PlayerControlItem(PlayerControlKind.Position, "进度条", new PositionControlSettings())) { }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
        }

        Settings.PropertyChanged += OnSettingsPropertyChanged;
        UpdateTimes();
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (Vm is { } vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
        }

        Settings.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.PositionSeconds) or nameof(MainWindowViewModel.DurationSeconds))
        {
            UpdateTimes();
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateTimes();

    /// <summary>按当前播放位置/总时长与设置，重算左右两侧的时间文本。</summary>
    private void UpdateTimes()
    {
        var settings = Settings;
        var vm = Vm;
        var elapsed = TimeSpan.FromSeconds(vm?.PositionSeconds ?? 0d);
        var total = TimeSpan.FromSeconds(vm?.DurationSeconds ?? 0d);
        var remaining = total - elapsed;

        LeftTime.Text = FormatSide(settings.LeftFirst, settings.LeftSecond, elapsed, total, remaining, settings.PrecisionStart, settings.PrecisionEnd);
        RightTime.Text = FormatSide(settings.RightFirst, settings.RightSecond, elapsed, total, remaining, settings.PrecisionStart, settings.PrecisionEnd);
    }

    /// <summary>一段来源 → 时间文本；None 返回 null（第一段为 None 整侧隐藏，第二段为 None 只显示第一段）。</summary>
    private static string? FormatSource(ProgressTimeSource source, TimeSpan elapsed, TimeSpan total, TimeSpan remaining, int precisionStart, int precisionEnd) => source switch
    {
        ProgressTimeSource.Total => PositionControlSettings.FormatTime(total, precisionStart, precisionEnd),
        ProgressTimeSource.Elapsed => PositionControlSettings.FormatTime(elapsed, precisionStart, precisionEnd),
        ProgressTimeSource.Remaining => PositionControlSettings.FormatTime(remaining, precisionStart, precisionEnd),
        _ => null,
    };

    private static string FormatSide(
        ProgressTimeSource first,
        ProgressTimeSource second,
        TimeSpan elapsed,
        TimeSpan total,
        TimeSpan remaining,
        int precisionStart,
        int precisionEnd)
    {
        var a = FormatSource(first, elapsed, total, remaining, precisionStart, precisionEnd);
        if (a is null)
        {
            return string.Empty;
        }

        var b = FormatSource(second, elapsed, total, remaining, precisionStart, precisionEnd);
        return b is null ? a : $"{a} / {b}";
    }
}
