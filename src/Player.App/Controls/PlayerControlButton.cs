using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Player.App.Controls;

/// <summary>
/// 控制栏按钮：按「全局设置 + 组件高级设置」的合成结果显示图标或文字（默认文字，与原 Button 一致）。
/// 图标用 <see cref="PlayerControlIcons"/> 里的 SVG 路径（矢量几何，不依赖字体），颜色跟随按钮 Foreground。
/// 图标模式下把文字挂在 ToolTip 上，悬停仍能知道控件是什么；文字模式清除 ToolTip。
/// 设置在设置窗口里改完即时切换（<see cref="PlayerControlDisplayModeTracker"/>），无需重建控制栏。
/// </summary>
public class PlayerControlButton : Button
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<PlayerControlButton, string?>(nameof(Text));

    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<PlayerControlButton, string?>(nameof(Icon));

    private readonly Border _iconContainer = new()
    {
        Width = 40,
        Height = 36,
        Background = Brushes.Transparent,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly Avalonia.Controls.Shapes.Path _iconPath = new()
    {
        Width = 20,
        Height = 20,
        Stretch = Stretch.Uniform,
        StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Fill = Brushes.Transparent,
    };

    private readonly PlayerControlDisplayModeTracker _tracker;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>图标模式显示的 SVG 路径（path data），取 <see cref="PlayerControlIcons"/> 里的常量。</summary>
    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public PlayerControlButton()
    {
        _tracker = new PlayerControlDisplayModeTracker(this, UpdateContent);
        _iconPath.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
        // 把 Path 放入 Border 容器，Border 完全覆盖可点击区域，彻底消除指针闪烁
        _iconContainer.Child = _iconPath;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
        {
            _iconPath.Data = change.NewValue is string path ? StreamGeometry.Parse(path) : null;
        }

        if (change.Property == TextProperty || change.Property == IconProperty)
        {
            UpdateContent();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _tracker.Attach();
        UpdateContent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tracker.Detach();
    }

    /// <summary>按当前合成模式切换内容：图标（附 ToolTip）或文字。</summary>
    private void UpdateContent()
    {
        if (_tracker.Current == ControlDisplayMode.Icon)
        {
            Content = _iconContainer;
            ClearValue(ToolTip.TipProperty);
        }
        else
        {
            Content = Text;
            ClearValue(ToolTip.TipProperty);
        }
    }
}
