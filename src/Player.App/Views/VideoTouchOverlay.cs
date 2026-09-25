using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Player.Playback;

namespace Player.App.Views;

/// <summary>
/// 覆盖在视频区上的透明触摸层。
/// <para>
/// Native 渲染器下视频是 mpv 的原生子窗口，会吞掉落在视频区内的触摸事件；
/// 这一层是独立顶层窗口、位于视频之上，专门负责把视频区的触摸转成手势事件，
/// 从而在不放弃零拷贝的前提下保住完整触摸交互。
/// </para>
/// <para>
/// 手势判定：单指的按下到抬起总位移小于 <see cref="DragThreshold"/> 视为点按（切换控制层），
/// 超过阈值即视为拖动并持续上报位移；两指按下即进入捏合，上报倍率增量与中点位移
/// （捏合时同时可以拖）。点按在抬起时才上报——按下即上报的话，就无法把它和"按住后开始拖"区分开。
/// </para>
/// </summary>
public sealed class VideoTouchOverlay : Window
{
    /// <summary>点按与拖动的判定阈值（DIP）。手指抖动比鼠标大，取 8。</summary>
    private const double DragThreshold = 8d;

    /// <summary>滚轮每格的缩放倍数：桌面上没有触摸时的备用入口（也便于自动化验证）。</summary>
    private const double WheelStep = 1.15d;

    /// <summary>视频区被点按（坐标为相对视频区左上角的点）。</summary>
    public event EventHandler<Point>? VideoTapped;

    /// <summary>视频区被拖动或捏合中移动（相对上一次事件的像素位移）。</summary>
    public event EventHandler<Point>? VideoDragged;

    /// <summary>一次手势结束，携带累计位移（像素）。用于日志取证与手感调校。</summary>
    public event EventHandler<Point>? VideoDragCompleted;

    /// <summary>缩放请求（倍率增量，1.0 表示不变）：双指捏合或滚轮产生。</summary>
    public event EventHandler<double>? VideoZoomRequested;

    private readonly Panel _surface;

    /// <summary>当前按下的所有触点。捏合要同时看两个，因此按指针 Id 存而不是单个坐标。</summary>
    private readonly Dictionary<int, Point> _pointers = [];

    private bool _pressed;
    private bool _dragging;
    private bool _pinching;
    private Point _pressedAt;
    private Point _lastAt;
    private double _dragTotalX;
    private double _dragTotalY;
    private double _pinchLastDistance;
    private Point _pinchLastMidpoint;

    public VideoTouchOverlay()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;

        _surface = new Panel { Background = Brushes.Transparent };
        _surface.PointerPressed += OnSurfacePointerPressed;
        _surface.PointerMoved += OnSurfacePointerMoved;
        _surface.PointerReleased += OnSurfacePointerReleased;
        _surface.PointerCaptureLost += OnSurfacePointerCaptureLost;
        _surface.PointerWheelChanged += OnSurfacePointerWheelChanged;
        Content = _surface;
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
            RaiseDrag(position - _lastAt);
        }

        _lastAt = position;
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
            VideoTapped?.Invoke(this, _pressedAt);
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