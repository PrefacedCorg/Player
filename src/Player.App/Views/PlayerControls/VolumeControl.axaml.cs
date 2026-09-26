using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Player.App.Platform;
using Player.App.ViewModels;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「音量」控件。交互方式全部由「组件设置」驱动：
/// <para>
/// · 显示音量百分比总开关（含百分号、数字直接编辑 FANumberBox 个位精度、隐藏滑块）；
/// · 横向滑块（默认）或点击弹出竖直滑块（像系统音量 Flyout）；
/// · 鼠标滚轮按步进调节（0 = 禁用）；
/// · 直接调整系统音量：0–100% 只调系统（软件侧固定 100%），超过 100% 后系统固定 100%、
///   增益交给软件（100–200%）；系统被外部调到 100% 以下时控件直接跟随系统值。
/// </para>
/// </summary>
public partial class VolumeControl : UserControl
{
    private readonly PlayerControlItem _item;

    /// <summary>当前显示值（0–200）。系统模式下是「总音量」：0–100 映射到系统，>100 是软件增益。</summary>
    private double _value;

    /// <summary>回写 UI 时防 ValueChanged 事件回环。</summary>
    private bool _syncing;

    private VolumeControlSettings Settings => _item.Settings as VolumeControlSettings ?? new VolumeControlSettings();

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    public VolumeControl(PlayerControlItem item)
    {
        _item = item;
        InitializeComponent();

        // FANumberBox 的 ValueChanged 是 TypedEventHandler，XAML 解析不了，这里代码订阅
        //（横排 + 竖直弹窗里的编辑框共用同一个 handler，值变化统一收敛）
        NumberBox.ValueChanged += OnNumberBoxChanged;
        FlyoutNumberBox.ValueChanged += OnNumberBoxChanged;

        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 初始值：跟随当前播放音量；系统模式下系统已被调到 100 以下时直接跟随系统
        _value = Vm?.Volume ?? 100d;
        if (Settings.UseSystemVolume && SystemVolumeService.IsAvailable)
        {
            var system = SystemVolumeService.Get();
            if (system is >= 0 and < 100)
            {
                _value = system;
                if (Vm is { } vm)
                {
                    vm.Volume = 100d;
                }
            }

            SystemVolumeService.StartPolling();
        }

        Settings.PropertyChanged += OnSettingsChanged;
        RefreshUi();
        SyncDisplay();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        SystemVolumeService.Changed -= OnSystemVolumeChanged;
        SystemVolumeService.StopPolling();
    }

    /// <summary>组件设置改动（设置页里调）即时反映到控件。</summary>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RefreshUi();
            SyncDisplay();
            if (e.PropertyName == nameof(VolumeControlSettings.UseSystemVolume))
            {
                ApplyToTarget();
            }
        });
    }

    /// <summary>
    /// 按设置切换显示形态：
    /// · 竖直弹窗：只显示「音量」按钮，外面的数字全部隐藏（数字在弹窗里）；
    /// · 横向：滑块默认显示，「数字直接编辑 + 隐藏滑块」都开启时只剩数字框；
    /// · 数字只在「显示音量百分比」总开关开启时出现，编辑框的百分号跟随同一个设置。
    /// </summary>
    private void RefreshUi()
    {
        var settings = Settings;
        var vertical = settings.VerticalFlyout;
        var showNumber = settings.ShowVolumeNumber && !vertical;

        FlyoutButton.IsVisible = vertical;
        Flyout.IsOpen = false;
        // 弹窗里的数字：编辑开关决定只读文本还是编辑框（跟随总开关，横竖两种形态行为一致）
        FlyoutNumber.IsVisible = settings.ShowVolumeNumber && !settings.EditableNumber;
        FlyoutNumberBox.IsVisible = settings.ShowVolumeNumber && settings.EditableNumber;
        // 「隐藏滑块」对两种形态都生效：横排藏横向滑块，弹窗里藏竖直滑块（数字编辑开启才有意义）
        HorizontalSlider.IsVisible = !vertical && !(settings.EditableNumber && settings.HideSlider);
        VerticalSlider.IsVisible = !(settings.EditableNumber && settings.HideSlider);
        NumberDisplay.IsVisible = showNumber && !settings.EditableNumber;
        NumberBox.IsVisible = showNumber && settings.EditableNumber;
        // NumberFormatter 闭包实时读设置（切百分号不用换委托）；Text 直接回写保证百分号切换立即生效
        NumberBox.NumberFormatter = FormatNumber;
        FlyoutNumberBox.NumberFormatter = FormatNumber;
        NumberBox.Text = FormatNumber(Math.Round(_value, MidpointRounding.AwayFromZero));
        FlyoutNumberBox.Text = NumberBox.Text;
    }

    /// <summary>FANumberBox 的数字渲染：个位数 + 可选百分号。解析器按数字前缀匹配，输入 100% 也认。</summary>
    private string FormatNumber(double value) => value.ToString("0") + (Settings.ShowPercent ? "%" : string.Empty);

    /// <summary>把当前值应用到播放/系统：非系统模式只写 mpv；系统模式 0–100 只动系统、软件固定 100。</summary>
    private void ApplyToTarget()
    {
        if (_syncing)
        {
            return;
        }

        var settings = Settings;
        if (settings.UseSystemVolume && SystemVolumeService.IsAvailable)
        {
            SystemVolumeService.Set(Math.Min(_value, 100d));
            if (Vm is { } vm)
            {
                vm.Volume = _value <= 100d ? 100d : _value;
            }
        }
        else if (Vm is { } vm)
        {
            vm.Volume = _value;
        }

        SystemVolumeService.Changed -= OnSystemVolumeChanged;
        if (settings.UseSystemVolume && SystemVolumeService.IsAvailable)
        {
            SystemVolumeService.Changed += OnSystemVolumeChanged;
        }

        SyncDisplay();
    }

    /// <summary>系统被外部（键盘/其它应用）调低到 100 以下：放弃软件增益直接跟随。</summary>
    private void OnSystemVolumeChanged(object? sender, double system)
    {
        if (!Settings.UseSystemVolume || system >= 100d)
        {
            return;
        }

        _value = system;
        if (Vm is { } vm)
        {
            vm.Volume = 100d;
        }

        SyncDisplay();
    }

    /// <summary>滑块 / 数字框的值变化：统一收敛到当前值再应用。</summary>
    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || Math.Abs(e.NewValue - _value) < 0.01d)
        {
            return;
        }

        _value = e.NewValue;
        ApplyToTarget();
    }

    /// <summary>可编辑数字框（FANumberBox，个位精度；NewValue 是 double 非空）。</summary>
    private void OnNumberBoxChanged(object? sender, FANumberBoxValueChangedEventArgs e)
    {
        if (_syncing || Math.Abs(e.NewValue - _value) < 0.01d)
        {
            return;
        }

        _value = e.NewValue;
        ApplyToTarget();
    }

    /// <summary>滚轮调节：按组件设置的步进（0 = 禁用）。</summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var step = Settings.MouseWheelStep;
        if (step <= 0)
        {
            return;
        }

        e.Handled = true;
        _value = Math.Clamp(_value + (e.Delta.Y > 0 ? step : -step), 0d, 200d);
        ApplyToTarget();
    }

    private void OnFlyoutClick(object? sender, RoutedEventArgs e)
    {
        Flyout.IsOpen = !Flyout.IsOpen;
        if (Flyout.IsOpen)
        {
            SyncDisplay();
        }
    }

    /// <summary>把当前值回写到各显示元素（滑块、数字、弹窗数字），按设置决定百分号。</summary>
    private void SyncDisplay()
    {
        _syncing = true;
        var settings = Settings;
        var suffix = settings.ShowPercent ? "%" : string.Empty;

        HorizontalSlider.Value = _value;
        VerticalSlider.Value = _value;
        NumberDisplay.Text = $"{_value:F0}{suffix}";
        FlyoutNumber.Text = $"{_value:F0}{suffix}";
        if (Math.Abs(NumberBox.Value - _value) >= 0.01d)
        {
            NumberBox.Value = Math.Round(_value, MidpointRounding.AwayFromZero);
        }

        if (Math.Abs(FlyoutNumberBox.Value - _value) >= 0.01d)
        {
            FlyoutNumberBox.Value = Math.Round(_value, MidpointRounding.AwayFromZero);
        }

        _syncing = false;
    }
}
