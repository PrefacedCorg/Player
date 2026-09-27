using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Player.App.Views;

/// <summary>
/// 自绘滑块（进度条/音量共用）：轨道、填充段、拇指的位置一律取整到整像素再摆放。
/// <para>
/// 为什么不用 Slider：Slider 内部的 Track 把拇指/填充边界放在亚像素位置（如 123.4 DIP），
/// 值推进时灰色剩余段的左边界反复落在像素之间被抗锯齿，肉眼看到的就是边缘左右抖动；
/// 暂停时值不动所以不抖。这里把边界吸附到整像素：要么不动，要么整像素前进，边缘永远清晰。
/// </para>
/// <para>
/// 拖动判定在控件内部完成（隧道阶段 + handledEventsToo，拇指会吞冒泡事件），
/// 通过 <see cref="ScrubStarted"/> / <see cref="ScrubCompleted"/> 通知外层；
/// 拖动过程中的值变化通过 <see cref="ValueChanged"/> 抛出。
/// 支持 <see cref="Orientation"/>（水平进度条 / 竖直音量条）。
/// </para>
/// </summary>
public partial class PositionBar : UserControl
{
    /// <summary>拇指直径，与 FluentAvalonia 的 SliderHorizontalThumbWidth 一致。</summary>
    private const double ThumbSize = 18d;

    /// <summary>
    /// 轨道两端各缩进 ThumbSize/2（9px）：
    /// <para>
    /// Thumb 在最左时中心对齐轨道左缘（thumbLeft=0、center=9=TrackPadding），
    /// 在最右时中心对齐轨道右缘（thumbLeft=width-18、center=width-9=width-TrackPadding），
    /// 两端各留出 9px 绘制缓冲，圆圈不被控件边缘裁掉。
    /// </para>
    /// </summary>
    private const double TrackPadding = ThumbSize / 2d;

    /// <summary>轨道厚度，与 FluentAvalonia 的 SliderTrackThemeHeight 一致。</summary>
    private const double TrackThickness = 4d;

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<PositionBar, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<PositionBar, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PositionBar, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<PositionBar, Orientation>(nameof(Orientation), Avalonia.Layout.Orientation.Horizontal);

    /// <summary>手指按下开始拖动（外层此时应开始忽略引擎回写）。</summary>
    public event EventHandler? ScrubStarted;

    /// <summary>手指抬起结束拖动（外层此时用当前 Value 做真正的 seek/提交）。</summary>
    public event EventHandler? ScrubCompleted;

    /// <summary>值变化（含拖动与程序赋值），参数就是控件自身，读 <see cref="Value"/> 即可。</summary>
    public event EventHandler? ValueChanged;

    private bool _scrubbing;

    public PositionBar()
    {
        InitializeComponent();

        // 隧道阶段 + handledEventsToo：Thumb 会接管冒泡的 PointerPressed/Released 并标记已处理
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);

        Root.SizeChanged += (_, _) => UpdateVisual();
        ConfigureTemplate();
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>滑块方向：水平（进度条）或竖直（音量弹窗，值从下往上增长）。</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MinimumProperty
            || change.Property == MaximumProperty
            || change.Property == ValueProperty)
        {
            UpdateVisual();
            if (change.Property == ValueProperty)
            {
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (change.Property == OrientationProperty)
        {
            ConfigureTemplate();
        }
    }

    /// <summary>模板加载/方向切换时按方向摆轨道与填充的形状（对齐、边距、厚度）。</summary>
    private void ConfigureTemplate()
    {
        if (Orientation == Avalonia.Layout.Orientation.Vertical)
        {
            TrackPart.Width = TrackThickness;
            TrackPart.Height = double.NaN;
            TrackPart.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            TrackPart.VerticalAlignment = VerticalAlignment.Stretch;
            TrackPart.Margin = new Thickness(0, TrackPadding);

            FillPart.Width = TrackThickness;
            FillPart.Height = 0;
            FillPart.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            FillPart.VerticalAlignment = VerticalAlignment.Bottom;
            FillPart.Margin = new Thickness(0, 0, 0, TrackPadding);
        }
        else
        {
            TrackPart.Height = TrackThickness;
            TrackPart.Width = double.NaN;
            TrackPart.VerticalAlignment = VerticalAlignment.Center;
            TrackPart.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            TrackPart.Margin = new Thickness(TrackPadding, 0);

            FillPart.Height = TrackThickness;
            FillPart.Width = 0;
            FillPart.VerticalAlignment = VerticalAlignment.Center;
            FillPart.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            FillPart.Margin = new Thickness(TrackPadding, 0, 0, 0);
        }
    }

    /// <summary>按当前 Value 重排填充段与拇指，所有坐标取整到整像素。</summary>
    private void UpdateVisual()
    {
        // 模板还没挂上（极早期布局）时忽略
        if (TrackPart is null || FillPart is null || ThumbPart is null)
        {
            return;
        }

        var width = Root.Bounds.Width;
        var height = Root.Bounds.Height;
        var range = Maximum - Minimum;
        var ratio = range > 0d ? Math.Clamp((Value - Minimum) / range, 0d, 1d) : 0d;

        if (Orientation == Avalonia.Layout.Orientation.Vertical)
        {
            if (height < ThumbSize + TrackPadding * 2 || width < 1d)
            {
                return;
            }

            // 竖直：值从下往上。thumbBottom = Thumb 底缘相对 Root 底缘的偏移，中心 = 底缘 + TrackPadding
            var trackLength = height - TrackPadding * 2d;
            var thumbBottom = Math.Round(ratio * trackLength);
            Canvas.SetTop(ThumbPart, Math.Round(height - thumbBottom - ThumbSize));
            Canvas.SetLeft(ThumbPart, Math.Round((width - ThumbSize) / 2d));

            // FillPart 有底 Margin=TrackPadding，顶缘对齐 Thumb 中心：Height = thumbBottom + TrackPadding - TrackPadding
            FillPart.Height = thumbBottom;
        }
        else
        {
            if (width < ThumbSize + TrackPadding * 2 || height < 1d)
            {
                return;
            }

            // Track 有效区间 = [TrackPadding, width-TrackPadding]，长度 trackLength = width - 2*TrackPadding
            // Thumb 左缘范围 [0, trackLength]，中心 = thumbLeft + TrackPadding = ratio * trackLength + TrackPadding
            var trackLength = width - TrackPadding * 2d;
            var thumbLeft = Math.Round(ratio * trackLength);
            Canvas.SetLeft(ThumbPart, thumbLeft);
            Canvas.SetTop(ThumbPart, Math.Round((height - ThumbSize) / 2d));

            // FillPart 有左 Margin=TrackPadding，右缘要对齐 Thumb 中心：
            // FillPart.Width = (thumbLeft + TrackPadding) - TrackPadding = thumbLeft
            FillPart.Width = thumbLeft;
        }
    }

    private void SetValueFromPoint(Point position)
    {
        var range = Maximum - Minimum;
        if (range <= 0d)
        {
            return;
        }

        double ratio;
        if (Orientation == Avalonia.Layout.Orientation.Vertical)
        {
            var height = Root.Bounds.Height;
            var trackLength = height - TrackPadding * 2d;
            if (trackLength <= 0d)
            {
                return;
            }

            // 指针 Y → 距底偏移换算成比例（值从下往上）
            var thumbBottom = Math.Clamp(height - TrackPadding - position.Y, 0d, trackLength);
            ratio = thumbBottom / trackLength;
        }
        else
        {
            var width = Root.Bounds.Width;
            var trackLength = width - TrackPadding * 2d;
            if (trackLength <= 0d)
            {
                return;
            }

            // 指针按下时让 Thumb 中心对齐指针位置：thumbLeft = x - TrackPadding
            var thumbLeft = Math.Clamp(position.X - TrackPadding, 0d, trackLength);
            ratio = thumbLeft / trackLength;
        }

        Value = Minimum + ratio * range;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _scrubbing = true;
        e.Pointer.Capture(this);
        ScrubStarted?.Invoke(this, EventArgs.Empty);
        SetValueFromPoint(point.Position);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_scrubbing)
        {
            SetValueFromPoint(e.GetPosition(this));
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        e.Pointer.Capture(null);
        ScrubCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 指针捕获被外力打断（窗口失焦等）：按"松手"收尾，保证外层不会卡在拖动状态
        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        ScrubCompleted?.Invoke(this, EventArgs.Empty);
    }
}
