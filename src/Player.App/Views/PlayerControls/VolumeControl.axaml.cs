using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Player.App.Controls;
using Player.App.Platform;
using Player.App.ViewModels;
using Player.App.Views;

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

    /// <summary>正在拖动滑块（防抖）：期间只刷新显示不下发引擎/系统，松手才提交。</summary>
    private bool _dragging;

    /// <summary>已订阅的 ViewModel（Loaded 时记下，卸载时按同一引用退订）。</summary>
    private MainWindowViewModel? _subscribedVm;

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

    /// <summary>预览器/设计时用（工厂始终走带 item 的构造）：默认设置走 Settings 兜底。</summary>
    public VolumeControl() : this(new PlayerControlItem(PlayerControlKind.Volume, "音量", new VolumeControlSettings())) { }

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
        // 画面手势 / 键盘快捷键也会改播放音量：跟随刷新显示（订阅放在初值整理之后，避免误触发）
        _subscribedVm = Vm;
        if (_subscribedVm is { } subscribed)
        {
            subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }

        RefreshUi();
        SyncDisplay();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        if (_subscribedVm is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

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

    /// <summary>系统被外部（键盘/其它应用）调低到 100 以下：放弃软件增益直接跟随。拖动期间不干扰。</summary>
    private void OnSystemVolumeChanged(object? sender, double system)
    {
        if (_dragging || !Settings.UseSystemVolume || system >= 100d)
        {
            return;
        }

        // 先同步软件侧再落到系统值：顺序反了，vm.Volume=100 的通知会把显示停回 100
        if (Vm is { } vm)
        {
            vm.Volume = 100d;
        }

        _value = system;
        SyncDisplay();
    }

    /// <summary>
    /// 外部改了播放音量（画面手势 / 键盘快捷键）：跟随刷新显示——滑块与数字都要动，
    /// 否则看起来就像"调音量没生效"。用户正在拖滑块或系统音量正在跟随（_dragging）时不打扰。
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Volume) || _dragging || _syncing)
        {
            return;
        }

        var volume = Math.Clamp(Vm?.Volume ?? _value, 0d, 200d);
        if (Math.Abs(volume - _value) < 0.01d)
        {
            return;
        }

        _value = volume;
        SyncDisplay();
    }

    /// <summary>滑块的值变化（PositionBar 自绘滑块）：拖动中只更新显示，松手由 ScrubCompleted 提交。</summary>
    private void OnSliderChanged(object? sender, EventArgs e)
    {
        if (sender is not PositionBar bar || _syncing || Math.Abs(bar.Value - _value) < 0.01d)
        {
            return;
        }

        _value = bar.Value;
        if (_dragging)
        {
            SyncDisplay();
        }
        else
        {
            ApplyToTarget();
        }
    }

    /// <summary>开始拖动滑块：进入防抖状态，外部值（系统音量轮询等）不再回写。</summary>
    private void OnScrubStarted(object? sender, EventArgs e) => _dragging = true;

    /// <summary>松手：提交当前值到引擎/系统（防抖终点）。</summary>
    private void OnScrubCompleted(object? sender, EventArgs e)
    {
        _dragging = false;
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

        // 音量三态图标（竖直弹窗按钮）
        if (FlyoutButton != null)
        {
            FlyoutButton.Icon = _value <= 0 ? PlayerControlIcons.Volume3
                : _value < 67 ? PlayerControlIcons.Volume2
                : PlayerControlIcons.Volume;
        }

        _syncing = false;
    }
}
