using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Player.Playback;

namespace Player.App.Views;

/// <summary>拖动的方向：识别到拖动时锁定，之后整个拖动只按这个轴处理（斜向拖动不会在两种调节之间来回跳）。</summary>
public enum VideoDragAxis
{
    /// <summary>横向拖动（左右滑动）。</summary>
    Horizontal,

    /// <summary>纵向拖动（上下滑动）。</summary>
    Vertical,
}

/// <summary>单指拖动开始的参数。</summary>
public sealed class VideoDragStartedEventArgs(Point start, VideoDragAxis axis) : EventArgs
{
    /// <summary>拖动起点（按下位置，DIP）。纵向拖动按它落在左 / 右半区取对应手势的设置。</summary>
    public Point Start { get; } = start;

    /// <summary>锁定的拖动方向。</summary>
    public VideoDragAxis Axis { get; } = axis;
}

/// <summary>
/// 覆盖在视频区上的透明触摸层。
/// <para>
/// Native 渲染器下视频是 mpv 的原生子窗口，会吞掉落在视频区内的触摸事件；
/// 这一层是独立顶层窗口、位于视频之上，专门负责把视频区的触摸转成手势事件，
/// 从而在不放弃零拷贝的前提下保住完整触摸交互。
/// </para>
/// <para>
/// 手势判定：单指的按下到抬起总位移小于 <see cref="DragThreshold"/> 视为点按——点按在
/// <see cref="DoubleTapMaxDelay"/> 内出现第二下就上报双击，否则等间隔过去上报单击；
/// 超过阈值即视为拖动，上报一次 <see cref="VideoDragStarted"/> 锁定方向（横向 / 纵向），
/// 之后持续上报位移，由上层按位移比例做连续调节（滑多少调多少）；
/// 两指按下即进入捏合，上报倍率增量与中点位移（捏合时同时可以拖）。
/// 点按在抬起时才上报——按下即上报的话，就无法把它和"按住后开始拖"区分开。
/// </para>
/// </summary>
public sealed class VideoTouchOverlay : Window
{
    /// <summary>点按与拖动的判定阈值（DIP）。手指抖动比鼠标大，取 8。</summary>
    private const double DragThreshold = 8d;

    /// <summary>连续两次点按的最大间隔：超过就按两次单击处理。</summary>
    private static readonly TimeSpan DoubleTapMaxDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>两次点按的落点最大偏差（DIP）：偏差过大不算双击。</summary>
    private const double DoubleTapMaxDistance = 48d;

    /// <summary>锁定拖动方向时的主方向倍数：两个方向的位移相差不到这个倍数就先不定，继续等下一次移动。</summary>
    private const double AxisDominance = 1.5d;

    /// <summary>滚轮每格的缩放倍数：桌面上没有触摸时的备用入口（也便于自动化验证）。</summary>
    private const double WheelStep = 1.15d;

    /// <summary>视频区被点按（坐标为相对视频区左上角的点）。</summary>
    public event EventHandler<Point>? VideoTapped;

    /// <summary>视频区被双击（两次点按都在 <see cref="DoubleTapMaxDelay"/> 内、落点接近）。</summary>
    public event EventHandler<Point>? VideoDoubleTapped;

    /// <summary>单指拖动开始：超过阈值、方向已锁定，整个拖动只上报一次。</summary>
    public event EventHandler<VideoDragStartedEventArgs>? VideoDragStarted;

    /// <summary>视频区被拖动或捏合中移动（相对上一次事件的像素位移）。</summary>
    public event EventHandler<Point>? VideoDragged;

    /// <summary>一次手势结束，携带累计位移（像素）。用于日志取证与手感调校。</summary>
    public event EventHandler<Point>? VideoDragCompleted;

    /// <summary>缩放请求（倍率增量，1.0 表示不变）：双指捏合或滚轮产生。</summary>
    public event EventHandler<double>? VideoZoomRequested;

    /// <summary>右下角「恢复亮度」被点击：请求把亮度复位为 0。</summary>
    public event EventHandler? BrightnessRestoreRequested;

    /// <summary>提示气泡的底色：半透明黑，压在画面上也看得清。</summary>
    private static readonly IBrush HintBackground = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0));

    private readonly Panel _surface;

    /// <summary>视频区中央的提示文字（滑动调进度时显示「目标位置 / 总时长」）。</summary>
    private readonly TextBlock _centerHintText;

    /// <summary>中央提示的容器：整体显隐，且不吃触摸（按下仍落到 _surface 走手势判定）。</summary>
    private readonly Border _centerHint;

    /// <summary>右下角「恢复亮度」提示：亮度 ≠ 0 时显示，点击复位。</summary>
    private readonly Border _brightnessHint;

    /// <summary>单击延迟上报的定时器：等过了双击间隔没有第二下，才确认这是一次单击。</summary>
    private readonly DispatcherTimer _singleTapTimer = new() { Interval = DoubleTapMaxDelay };

    /// <summary>当前按下的所有触点。捏合要同时看两个，因此按指针 Id 存而不是单个坐标。</summary>
    private readonly Dictionary<int, Point> _pointers = [];

    private bool _pressed;
    private bool _dragging;

    /// <summary>本次拖动是否已经锁定方向（锁定后只上报一次拖动开始）。</summary>
    private bool _axisLocked;
    private bool _pinching;
    private Point _pressedAt;
    private Point _lastAt;
    private double _dragTotalX;
    private double _dragTotalY;
    private double _pinchLastDistance;
    private Point _pinchLastMidpoint;

    /// <summary>有一次点按在等双击间隔（还没上报）。</summary>
    private bool _hasPendingTap;
    private DateTime _lastTapAt;
    private Point _lastTapPosition;

    public VideoTouchOverlay()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;

        _singleTapTimer.Tick += OnSingleTapTimerTick;

        _surface = new Panel { Background = Brushes.Transparent };
        _surface.PointerPressed += OnSurfacePointerPressed;
        _surface.PointerMoved += OnSurfacePointerMoved;
        _surface.PointerReleased += OnSurfacePointerReleased;
        _surface.PointerCaptureLost += OnSurfacePointerCaptureLost;
        _surface.PointerWheelChanged += OnSurfacePointerWheelChanged;

        // 中心提示只显示不吃触摸（IsHitTestVisible=false）：提示可见时按下仍会落到 _surface，
        // 手势判定不受影响。右下角「恢复亮度」要点，因此保持命中——它的点击不会进入 _surface 的手势逻辑。
        _centerHintText = new TextBlock { FontSize = 22, Foreground = Brushes.White };
        _centerHint = new Border
        {
            Background = HintBackground,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 12),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = false,
            IsHitTestVisible = false,
            Child = _centerHintText,
        };

        _brightnessHint = new Border
        {
            Background = HintBackground,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8),
            Margin = new Thickness(0, 0, 24, 24),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            IsVisible = false,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = "恢复亮度", FontSize = 15, Foreground = Brushes.White },
        };
        _brightnessHint.PointerPressed += OnBrightnessHintPressed;

        Content = new Grid { Children = { _surface, _centerHint, _brightnessHint } };
    }

    /// <summary>在视频区中央显示一行提示（滑动调进度时的「目标位置 / 总时长」）。</summary>
    public void ShowCenterHint(string text)
    {
        _centerHintText.Text = text;
        _centerHint.IsVisible = true;
    }

    /// <summary>隐藏中央提示（拖动结束时）。</summary>
    public void HideCenterHint() => _centerHint.IsVisible = false;

    /// <summary>右下角「恢复亮度」提示的显隐：亮度被调过（≠ 0）就显示，复位后隐藏。</summary>
    public void SetBrightnessHintVisible(bool visible) => _brightnessHint.IsVisible = visible;

    private void OnBrightnessHintPressed(object? sender, PointerPressedEventArgs e)
    {
        // 吃掉这次按下：不让它落到 _surface 上启动一次点按 / 拖动判定
        e.Handled = true;
        BrightnessRestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetPosition(_surface);
        _pointers[e.Pointer.Id] = position;

        // 捕获指针：手指滑出视频区边缘后仍能收到移动与抬起事件
        e.Pointer.Capture(_surface);

        if (_pointers.Count == 1)
        {
            _pressed = true;
            _dragging = false;
            _axisLocked = false;
            _dragTotalX = 0d;
            _dragTotalY = 0d;
            _pressedAt = _lastAt = position;
            return;
        }

        if (_pointers.Count == 2)
        {
            // 第二根手指按下 → 转捏合，之前的点按/拖动判定作废
            _pressed = false;
            _dragging = false;
            _pinching = true;
            (_pinchLastDistance, _pinchLastMidpoint) = ReadPinch();
        }
    }

    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointers.ContainsKey(e.Pointer.Id))
        {
            return;
        }

        var position = e.GetPosition(_surface);
        _pointers[e.Pointer.Id] = position;

        if (_pinching && _pointers.Count >= 2)
        {
            OnPinchMoved();
            return;
        }

        if (!_pressed)
        {
            return;
        }

        if (!_dragging && Distance(position, _pressedAt) >= DragThreshold)
        {
            _dragging = true;
        }

        if (_dragging)
        {
            if (!_axisLocked)
            {
                TryLockDragAxis(position);
            }

            RaiseDrag(position - _lastAt);
        }

        _lastAt = position;
    }

    /// <summary>
    /// 锁定拖动方向：主方向要明显（相差 1.5 倍以上）才定，斜着且不分明就等下一次移动再判——
    /// 免得刚动起来的一点横向抖动把纵向滑动判成横向。
    /// 定下来时上报一次拖动开始（起点用于区分左 / 右半区），此后整个拖动只按这个轴处理。
    /// </summary>
    private void TryLockDragAxis(Point position)
    {
        var dx = Math.Abs(position.X - _pressedAt.X);
        var dy = Math.Abs(position.Y - _pressedAt.Y);
        if (dx < dy * AxisDominance && dy < dx * AxisDominance)
        {
            return;
        }

        _axisLocked = true;
        var axis = dx >= dy ? VideoDragAxis.Horizontal : VideoDragAxis.Vertical;
        VideoDragStarted?.Invoke(this, new VideoDragStartedEventArgs(_pressedAt, axis));
    }

    /// <summary>捏合中：指距变化换算成倍率，中点位移换算成平移（一边缩放一边拖）。</summary>
    private void OnPinchMoved()
    {
        var (distance, midpoint) = ReadPinch();

        var factor = VideoZoomMath.ScaleFromPinch(distance, _pinchLastDistance);
        if (Math.Abs(factor - 1d) > 0.0005d)
        {
            VideoZoomRequested?.Invoke(this, factor);
        }

        var delta = midpoint - _pinchLastMidpoint;
        if (delta.X != 0d || delta.Y != 0d)
        {
            RaiseDrag(delta);
        }

        _pinchLastDistance = distance;
        _pinchLastMidpoint = midpoint;
    }

    private void OnSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pointers.Remove(e.Pointer.Id);

        if (_pinching)
        {
            if (_pointers.Count >= 2)
            {
                // 还有两指按着：以新的指距与中点继续捏合
                (_pinchLastDistance, _pinchLastMidpoint) = ReadPinch();
                return;
            }

            _pinching = false;

            if (_pointers.Count == 1)
            {
                // 抬起一根手指后剩下的手指继续当作拖动，不用重新按一次
                _pressed = true;
                _dragging = true;
                _lastAt = _pointers.Values.First();
                return;
            }

            _pressed = false;
            _dragging = false;
            RaiseDragCompleted();
            return;
        }

        if (!_pressed)
        {
            return;
        }

        var wasDragging = _dragging;
        _pressed = false;
        _dragging = false;

        if (wasDragging)
        {
            RaiseDragCompleted();
        }
        else
        {
            RaiseTapOrDoubleTap(_pressedAt);
        }
    }

    /// <summary>
    /// 指针捕获被外力打断（窗口失焦等）。多指场景下每个指针会各自触发一次，
    /// 因此只在最后一根手指也离开时才收尾，否则会把正在进行的捏合误判为结束。
    /// </summary>
    private void OnSurfacePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pointers.Remove(e.Pointer.Id);

        if (_pointers.Count > 0)
        {
            return;
        }

        if (_dragging || _pinching)
        {
            RaiseDragCompleted();
        }

        _pressed = false;
        _dragging = false;
        _pinching = false;
    }

    private void OnSurfacePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0d)
        {
            return;
        }

        // 向上滚 = 放大。带小数的格数（触控板/高精度滚轮）也能连续缩放
        VideoZoomRequested?.Invoke(this, Math.Pow(WheelStep, e.Delta.Y));
        e.Handled = true;
    }

    private void RaiseDrag(Point delta)
    {
        _dragTotalX += delta.X;
        _dragTotalY += delta.Y;
        VideoDragged?.Invoke(this, delta);
    }

    private void RaiseDragCompleted() =>
        VideoDragCompleted?.Invoke(this, new Point(_dragTotalX, _dragTotalY));

    /// <summary>
    /// 点按上报：与上一次点按在时间与落点上足够接近时判为双击（撤销上一次待定的单击），
    /// 否则先挂起，等双击间隔过去没有第二下再作为单击上报。
    /// 单击必须延迟上报——立即上报就无法把"单击"与"双击的第一下"区分开。
    /// </summary>
    private void RaiseTapOrDoubleTap(Point position)
    {
        var now = DateTime.UtcNow;
        if (_hasPendingTap
            && now - _lastTapAt <= DoubleTapMaxDelay
            && Distance(position, _lastTapPosition) <= DoubleTapMaxDistance)
        {
            _hasPendingTap = false;
            _singleTapTimer.Stop();
            VideoDoubleTapped?.Invoke(this, position);
            return;
        }

        _hasPendingTap = true;
        _lastTapAt = now;
        _lastTapPosition = position;
        _singleTapTimer.Stop();
        _singleTapTimer.Start();
    }

    /// <summary>双击间隔内没有第二下 → 确认是一次单击。</summary>
    private void OnSingleTapTimerTick(object? sender, EventArgs e)
    {
        _singleTapTimer.Stop();
        if (!_hasPendingTap)
        {
            return;
        }

        _hasPendingTap = false;
        VideoTapped?.Invoke(this, _lastTapPosition);
    }

    /// <summary>取前两个触点算指距与中点（按 Id 排序，保证同一手势内取到的是同一对）。</summary>
    private (double Distance, Point Midpoint) ReadPinch()
    {
        var ids = _pointers.Keys.OrderBy(static id => id).Take(2).ToArray();
        var first = _pointers[ids[0]];
        var second = _pointers[ids[1]];

        return (Distance(first, second), new Point((first.X + second.X) / 2d, (first.Y + second.Y) / 2d));
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}