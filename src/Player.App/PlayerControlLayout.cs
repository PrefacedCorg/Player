using System.Collections.ObjectModel;
using Avalonia.Layout;
using FluentAvalonia.UI.Controls;

namespace Player.App;

/// <summary>
/// 控制栏里可以摆放的控件种类。每种对应 Views/PlayerControls 下的一个独立 UserControl，
/// 增删控件 = 加/删一个控件文件 + 在 <see cref="PlayerControlBar"/> 的工厂里加一行。
/// 轮播/滚动/分组/堆叠是容器型控件：它们通过 <see cref="PlayerControlItem.Children"/> 承载其它控件。
/// </summary>
public enum PlayerControlKind
{
    /// <summary>打开文件（含多选入队）。</summary>
    OpenFile,

    /// <summary>播放 / 暂停。</summary>
    PlayPause,

    /// <summary>停止。</summary>
    Stop,

    /// <summary>进度条（自绘，占控制栏剩余宽度）。</summary>
    Position,

    /// <summary>时间显示（当前 / 总时长）。</summary>
    TimeDisplay,

    /// <summary>音量（滑块 + 百分比）。</summary>
    Volume,

    /// <summary>渲染器选择。</summary>
    Renderer,

    /// <summary>打开设置。</summary>
    Settings,

    /// <summary>关闭应用。</summary>
    Close,

    /// <summary>最小化窗口。</summary>
    Minimize,

    /// <summary>进入/退出全屏。</summary>
    Fullscreen,

    /// <summary>播放上一个（队列导航）。</summary>
    Previous,

    /// <summary>回退（快退 10 秒）。</summary>
    Rewind,

    /// <summary>快进（快进 10 秒）。</summary>
    FastForward,

    /// <summary>播放下一个（队列导航）。</summary>
    Next,

    /// <summary>循环模式：单个循环 / 列表循环 / 单个播放 / 随机播放，点击循环切换。</summary>
    LoopMode,

    /// <summary>调试信息：播放状态、起播时序与解码/CPU 诊断（一个组件占一行）。</summary>
    DebugInfo,

    /// <summary>轮播容器：定时切换显示容器里的控件。</summary>
    Slide,

    /// <summary>滚动容器：容器里的控件横向滚动显示。</summary>
    Rolling,

    /// <summary>分组容器：容器里的控件横向排成一组。</summary>
    Group,

    /// <summary>堆叠容器：容器里的控件叠放在一起。</summary>
    Stack,
}

/// <summary>
/// 组件库条目：只表示"可以放进控制栏的控件种类"。放置之后由 <see cref="PlayerControlItem"/> 承载。
/// </summary>
public sealed record PlayerControlLibraryEntry(PlayerControlKind Kind, string Title, string Description)
{
    /// <summary>图标（组件库条目用，32x32；字号写在图标源上的原因同 <see cref="PlayerControlItem.IconSource"/>）。</summary>
    public FASymbolIconSource IconSource { get; } = new() { Symbol = PlayerControlCatalog.SymbolFor(Kind), FontSize = 32 };

    /// <summary>图标（符号形式，右键菜单"包裹到新容器"这类小尺寸场合用）。</summary>
    public FASymbol Symbol => PlayerControlCatalog.SymbolFor(Kind);

    /// <summary>是否是容器型控件（放置后可以往里拖其它控件）。</summary>
    public bool IsContainer => PlayerControlCatalog.IsContainer(Kind);
}

/// <summary>
/// 拖动载荷：条目 + 它当时所在的列表（照抄 ClassIsland 的 EditableComponentsListBoxDragData——
/// MultiBinding 把两者打包，落点处理器靠 SourceList 区分"从控制栏里拖的"还是"从组件库拖的"，
/// 以及"从哪个容器的子控件列表里拖的"）。
/// </summary>
public sealed record PlayerControlDragData(PlayerControlItem Item, System.Collections.IList SourceList)
{
    /// <summary>MultiBinding 转换器：[0]=行 DataContext（条目），[1]=所在 ListBox 的 ItemsSource。</summary>
    public static readonly PlayerControlDragDataConverter Create = new();
}

public sealed class PlayerControlDragDataConverter : Avalonia.Data.Converters.IMultiValueConverter
{
    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        // MultiBinding 求值过程中会出现 UnsetValue（比如第二个绑定还没解析出 ItemsSource），
        // 不能靠 "is { }" 判空——必须显式排除，否则强转 IList 直接崩（InvalidCastException）。
        if (values is not [PlayerControlItem item, System.Collections.IList source])
        {
            return Avalonia.AvaloniaProperty.UnsetValue;
        }

        return new PlayerControlDragData(item, source);
    }
}

/// <summary>控制栏控件清单与默认布局。</summary>
public static class PlayerControlCatalog
{
    /// <summary>全部可放置的控件，顺序即组件库里的排列顺序（容器型控件排在最后）。</summary>
    public static IReadOnlyList<PlayerControlLibraryEntry> All { get; } =
    [
        new(PlayerControlKind.OpenFile, "打开文件", "选择媒体文件播放，多选时按顺序入队"),
        new(PlayerControlKind.PlayPause, "播放 / 暂停", "切换当前媒体的播放与暂停"),
        new(PlayerControlKind.Stop, "停止", "停止播放并清空画面"),
        new(PlayerControlKind.Position, "进度条", "显示并拖动播放进度"),
        new(PlayerControlKind.TimeDisplay, "时间显示", "当前时间 / 总时长"),
        new(PlayerControlKind.Volume, "音量", "音量滑块与百分比，上限 200%"),
        new(PlayerControlKind.Renderer, "渲染器", "切换 Native / OpenGl / Software"),
        new(PlayerControlKind.Settings, "设置", "打开设置窗口"),
        new(PlayerControlKind.Close, "关闭", "关闭应用"),
        new(PlayerControlKind.Minimize, "最小化", "最小化窗口"),
        new(PlayerControlKind.Fullscreen, "全屏", "进入 / 退出全屏"),
        new(PlayerControlKind.Previous, "上一个", "播放队列里的上一个文件"),
        new(PlayerControlKind.Rewind, "回退", "快退 10 秒"),
        new(PlayerControlKind.FastForward, "快进", "快进 10 秒"),
        new(PlayerControlKind.Next, "下一个", "播放队列里的下一个文件"),
        new(PlayerControlKind.LoopMode, "循环模式", "循环切换：单个循环 / 列表循环 / 单个播放 / 随机播放"),
        new(PlayerControlKind.DebugInfo, "调试信息", "播放状态、起播时序与解码/CPU 诊断"),
        new(PlayerControlKind.Slide, "轮播容器", "定时切换显示容器里的控件"),
        new(PlayerControlKind.Rolling, "滚动容器", "容器里的控件横向滚动显示"),
        new(PlayerControlKind.Group, "分组容器", "把容器里的控件横向排成一组"),
        new(PlayerControlKind.Stack, "堆叠容器", "把容器里的控件叠放在一起"),
    ];

    /// <summary>
    /// 默认布局（三行，照 ClassIsland-2.0 主界面多行的结构）：
    /// 第一行进度条独占；第二行分左右中三栏（三个分组容器，对齐方式走分组容器
    /// 高级设置里的「对齐方式」）；第三行调试信息算一个组件占一行。
    /// </summary>
    public static ObservableCollection<PlayerControlLine> CreateDefaultLayout()
    {
        // 第一行：进度条独占
        var positionLine = new PlayerControlLine();
        positionLine.Children.Add(Item(PlayerControlKind.Position));

        // 第二行：居左 关闭/最小化/全屏 · 居中 上一个/回退/播放暂停/快进/下一个（左右对称，
        // 行面板的整行居中让播放/暂停正好在整行正中间）· 居右 音量/渲染器/循环模式/设置
        var buttonsLine = new PlayerControlLine();
        buttonsLine.Children.Add(Group(HorizontalAlignment.Left,
            Item(PlayerControlKind.Close),
            Item(PlayerControlKind.Minimize),
            Item(PlayerControlKind.Fullscreen)));
        buttonsLine.Children.Add(Group(HorizontalAlignment.Center,
            Item(PlayerControlKind.Previous),
            Item(PlayerControlKind.Rewind),
            Item(PlayerControlKind.PlayPause),
            Item(PlayerControlKind.FastForward),
            Item(PlayerControlKind.Next)));
        buttonsLine.Children.Add(Group(HorizontalAlignment.Right,
            Item(PlayerControlKind.Volume),
            Item(PlayerControlKind.Renderer),
            Item(PlayerControlKind.LoopMode),
            Item(PlayerControlKind.Settings)));

        // 第三行：调试信息（状态 + 起播时序 + 解码诊断，算一个组件也就是一行）
        var debugLine = new PlayerControlLine();
        debugLine.Children.Add(Item(PlayerControlKind.DebugInfo));

        return
        [
            positionLine,
            buttonsLine,
            debugLine,
        ];
    }

    /// <summary>按种类创建一个控件项（默认布局用，标题取组件库条目）。</summary>
    private static PlayerControlItem Item(PlayerControlKind kind) =>
        new(kind, All.First(entry => entry.Kind == kind).Title);

    /// <summary>创建一个分组容器并把控件装进去（默认布局用，对齐方式直接写进高级设置里的对齐属性）。</summary>
    private static PlayerControlItem Group(HorizontalAlignment alignment, params PlayerControlItem[] children)
    {
        var group = new PlayerControlItem(PlayerControlKind.Group, "分组容器");
        group.Settings.HorizontalAlignment = alignment;
        foreach (var child in children)
        {
            group.Children!.Add(child);
        }

        return group;
    }

    /// <summary>控件种类 → 图标（FluentAvalonia 内置符号字体，无需外部图标资源）。</summary>
    public static FASymbol SymbolFor(PlayerControlKind kind) => kind switch
    {
        PlayerControlKind.OpenFile => FASymbol.OpenFile,
        PlayerControlKind.PlayPause => FASymbol.Play,
        PlayerControlKind.Stop => FASymbol.Stop,
        // 进度条没有现成的轨道图标，用"前进"箭头表达播放推进
        PlayerControlKind.Position => FASymbol.Forward,
        PlayerControlKind.TimeDisplay => FASymbol.Clock,
        PlayerControlKind.Volume => FASymbol.Volume,
        PlayerControlKind.Renderer => FASymbol.Video,
        PlayerControlKind.Settings => FASymbol.Settings,
        PlayerControlKind.Close => FASymbol.Cancel,
        PlayerControlKind.Minimize => FASymbol.ChevronDown,
        PlayerControlKind.Fullscreen => FASymbol.FullScreen,
        PlayerControlKind.Previous => FASymbol.Previous,
        PlayerControlKind.Rewind => FASymbol.Back,
        PlayerControlKind.FastForward => FASymbol.Forward,
        PlayerControlKind.Next => FASymbol.Next,
        PlayerControlKind.LoopMode => FASymbol.RepeatAll,
        PlayerControlKind.DebugInfo => FASymbol.Code,
        PlayerControlKind.Slide => FASymbol.SlideShow,
        PlayerControlKind.Rolling => FASymbol.Sync,
        PlayerControlKind.Group => FASymbol.AllApps,
        PlayerControlKind.Stack => FASymbol.Pictures,
        _ => FASymbol.ViewAll,
    };

    /// <summary>某种控件是否是容器型控件。</summary>
    public static bool IsContainer(PlayerControlKind kind) => kind is
        PlayerControlKind.Slide or PlayerControlKind.Rolling or PlayerControlKind.Group or PlayerControlKind.Stack;

    /// <summary>某种控件是否有自己的设置界面（轮播、滚动有参数可调，其余没有）。</summary>
    public static bool HasSettingsView(PlayerControlKind kind) => kind is
        PlayerControlKind.Slide or PlayerControlKind.Rolling;

    /// <summary>某种控件的设置对象工厂。</summary>
    public static PlayerControlSettings CreateSettings(PlayerControlKind kind) => kind switch
    {
        PlayerControlKind.Slide => new SlideControlSettings(),
        PlayerControlKind.Rolling => new RollingControlSettings(),
        PlayerControlKind.Group => new GroupControlSettings(),
        PlayerControlKind.Stack => new StackControlSettings(),
        _ => new PlayerControlSettings(),
    };
}