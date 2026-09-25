using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 滚动容器（对应 ClassIsland 的 RollingComponent）：内容比可视区宽时按像素速度横向滚动，
/// 滚完一轮可以从右侧重新进入并在末尾暂停。内容放得下时不动（只保留初始偏移）。
/// </summary>
public partial class RollingControl : UserControl
{
    private readonly PlayerControlItem? _item;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly TranslateTransform _transform = new();
    private double _offset;
    private double _pauseRemainingMs;
    private long _lastTickMs;

    // 无参构造让 XAML 运行时加载器也能创建（AVLN3001）。
    // 没有绑定控件项就没有滚动设置，此时只作为普通容器显示，不启动滚动。
    public RollingControl() : this([])
    {
    }

    public RollingControl(IReadOnlyList<Control> children)
    {
        InitializeComponent();
        foreach (var child in children)
        {
            InnerPanel.Children.Add(child);
        }

        InnerPanel.RenderTransform = _transform;
    }

    public RollingControl(PlayerControlItem item, IReadOnlyList<Control> children) : this(children)
    {
        _item = item;

        _timer.Tick += (_, _) => OnTick();
        AttachedToVisualTree += (_, _) =>
        {
            _offset = -Math.Max(0, ((RollingControlSettings)_item.Settings).PauseOffsetX);
            ApplyOffset();
            _lastTickMs = Environment.TickCount64;
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private void OnTick()
    {
        if (_item?.Settings is not RollingControlSettings settings)
        {
            return;
        }

        var innerWidth = InnerPanel.Bounds.Width;
        var outerWidth = Viewport.Bounds.Width;

        var now = Environment.TickCount64;
        var deltaSeconds = Math.Clamp(now - _lastTickMs, 0, 100) / 1000d;
        _lastTickMs = now;

        if (innerWidth <= outerWidth)
        {
            // 内容放得下就不滚动，只保留初始偏移（与 ClassIsland 的 IsScrolling 判定一致）
            _offset = -Math.Min(Math.Max(0, settings.PauseOffsetX), innerWidth);
            ApplyOffset();
            return;
        }

        if (_pauseRemainingMs > 0)
        {
            _pauseRemainingMs -= deltaSeconds * 1000d;
            return;
        }

        _offset -= Math.Clamp(settings.SpeedPixelPerSecond, 1, int.MaxValue) * deltaSeconds;
        if (_offset <= -innerWidth)
        {
            // 内容已完全滚出左边：暂停一会儿（可选），再从右侧重新进入
            _pauseRemainingMs = settings.IsPauseEnabled ? Math.Max(0, settings.PauseSeconds) * 1000d : 0;
            _offset = outerWidth;
        }

        ApplyOffset();
    }

    private void ApplyOffset() => _transform.X = _offset;
}