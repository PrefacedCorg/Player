using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Player.App.Views;

/// <summary>
/// 覆盖在视频区上的透明触摸层。
/// <para>
/// Native 渲染器下视频是 mpv 的原生子窗口，会吞掉落在视频区内的触摸事件；
/// 这一层是独立顶层窗口、位于视频之上，专门负责把视频区的触摸转成手势事件，
/// 从而在不放弃零拷贝的前提下保住完整触摸交互。
/// </para>
/// </summary>
public sealed class VideoTouchOverlay : Window
{
    /// <summary>视频区被触摸（坐标为相对视频区左上角的点）。</summary>
    public event EventHandler<Point>? VideoTapped;

    public VideoTouchOverlay()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;

        var surface = new Panel { Background = Brushes.Transparent };
        surface.PointerPressed += OnSurfacePointerPressed;
        Content = surface;
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Panel surface)
        {
            return;
        }

        VideoTapped?.Invoke(this, e.GetPosition(surface));
    }
}