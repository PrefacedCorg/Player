using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace Player.App;

/// <summary>
/// 控制栏里可以摆放的控件种类。每种对应 Views/PlayerControls 下的一个独立 UserControl，
/// 增删控件 = 加/删一个控件文件 + 在 <see cref="PlayerControlBar"/> 的工厂里加一行。
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
}

/// <summary>
/// 控制栏中的一个控件项：种类 + 名称。
/// 它出现在控制栏布局列表里就表示"已放置"。
/// </summary>
public partial class PlayerControlItem(PlayerControlKind kind, string title) : ObservableObject
{
    public PlayerControlKind Kind { get; } = kind;

    /// <summary>显示名称（设置页与组件库用）。</summary>
    public string Title { get; } = title;

    /// <summary>
    /// 图标（设置页已放置行用，14x14）。
    /// 字号必须写在图标源上：FAIconSourceElement 生成子图标时是把 IconSource 的 FontSize
    /// 绑过去的（见 FluentAvalonia 的 FAIconHelpers.CreateSymbolIconFromSymbolIconSource），
    /// 元素上的 TextElement.FontSize 不起作用。
    /// </summary>
    public FASymbolIconSource IconSource { get; } = new() { Symbol = PlayerControlCatalog.SymbolFor(kind), FontSize = 14 };
}

/// <summary>
/// 组件库条目：只表示"可以放进控制栏的控件种类"。放置之后由 <see cref="PlayerControlItem"/> 承载。
/// </summary>
public sealed record PlayerControlLibraryEntry(PlayerControlKind Kind, string Title, string Description)
{
    /// <summary>图标（组件库条目用，32x32；字号写在图标源上的原因同 <see cref="PlayerControlItem.IconSource"/>）。</summary>
    public FASymbolIconSource IconSource { get; } = new() { Symbol = PlayerControlCatalog.SymbolFor(Kind), FontSize = 32 };
}

/// <summary>
/// 拖动载荷：条目 + 它当时所在的列表（照抄 ClassIsland 的 EditableComponentsListBoxDragData——
/// MultiBinding 把两者打包，落点处理器靠 SourceList 区分"从控制栏里拖的"还是"从组件库拖的"）。
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
    /// <summary>全部可放置的控件，顺序即组件库里的排列顺序。</summary>
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
    ];

    /// <summary>默认布局：全部控件按默认顺序放进控制栏。</summary>
    public static ObservableCollection<PlayerControlItem> CreateDefaultLayout() =>
        new(All.Select(static entry => new PlayerControlItem(entry.Kind, entry.Title)));

    /// <summary>
    /// 控件种类 → 图标（FluentAvalonia 内置符号字体，无需外部图标资源）。
    /// 组件库条目与已放置行都取这里，保证两边图标一致。
    /// </summary>
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
        _ => FASymbol.ViewAll,
    };
}