using Avalonia.Controls;
using Player.App.Views.PlayerControls;

namespace Player.App.Controls;

/// <summary>
/// 控制栏控件工厂：按 <see cref="PlayerControlItem"/> 递归创建控件（容器会连同它的子控件一起建好）。
/// 叶子控件显式挂上主窗口 ViewModel 作数据上下文——容器的子控件会继承到容器自己的数据上下文，
/// 不显式设置的话容器里的控件就绑不到播放状态了。
/// </summary>
public static class PlayerControlFactory
{
    /// <summary>创建一个控件项对应的宿主控件（含容器内的子控件）。</summary>
    public static Control Build(PlayerControlItem item, object? dataContext)
    {
        var host = new PlayerControlHost(item) { Child = CreateCore(item, dataContext) };
        return host;
    }

    private static Control? CreateCore(PlayerControlItem item, object? dataContext) => item.Kind switch
    {
        PlayerControlKind.Slide => new SlideControl(item, BuildChildren(item, dataContext)),
        PlayerControlKind.Rolling => new RollingControl(item, BuildChildren(item, dataContext)),
        PlayerControlKind.Group => new GroupControl(BuildChildren(item, dataContext)),
        PlayerControlKind.Stack => new StackControl(BuildChildren(item, dataContext)),
        _ => CreateLeaf(item.Kind, dataContext),
    };

    private static List<Control> BuildChildren(PlayerControlItem item, object? dataContext) =>
        item.Children is { } children
            ? children.Select(child => Build(child, dataContext)).ToList()
            : [];

    /// <summary>普通控件（非容器）：与 PlayerControlBar 原有的工厂一致。</summary>
    private static Control? CreateLeaf(PlayerControlKind kind, object? dataContext)
    {
        Control? control = kind switch
        {
            PlayerControlKind.OpenFile => new OpenFileControl(),
            PlayerControlKind.PlayPause => new PlayPauseControl(),
            PlayerControlKind.Stop => new StopControl(),
            PlayerControlKind.Position => new PositionControl { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            PlayerControlKind.TimeDisplay => new TimeDisplayControl(),
            PlayerControlKind.Volume => new VolumeControl(),
            PlayerControlKind.Renderer => new RendererControl(),
            PlayerControlKind.Settings => new SettingsControl(),
            PlayerControlKind.Close => new CloseControl(),
            PlayerControlKind.Minimize => new MinimizeControl(),
            PlayerControlKind.Fullscreen => new FullscreenControl(),
            PlayerControlKind.Previous => new PreviousControl(),
            PlayerControlKind.Rewind => new RewindControl(),
            PlayerControlKind.FastForward => new FastForwardControl(),
            PlayerControlKind.Next => new NextControl(),
            PlayerControlKind.LoopMode => new LoopModeControl(),
            PlayerControlKind.DebugInfo => new DebugInfoControl(),
            _ => null,
        };

        if (control is not null)
        {
            control.DataContext = dataContext;
        }

        return control;
    }
}