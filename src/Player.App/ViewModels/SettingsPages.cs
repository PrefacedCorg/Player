using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using HanumanInstitute.LibMpv.Avalonia;
using Player.App.Views.SettingsPages;
using Player.Playback;

namespace Player.App.ViewModels;

/// <summary>
/// 设置页基类。模板阶段这些属性只是界面状态（控件可交互、可切换），
/// 后续接入配置持久化时把字段读写换成 AppSettings 即可，界面结构无需改动。
/// </summary>
public abstract class SettingsPageViewModel(string title, string description, FASymbol symbol) : ObservableObject
{
    /// <summary>导航栏条目文字。</summary>
    public string Title { get; } = title;

    /// <summary>内容页标题下方的一行说明。</summary>
    public string Description { get; } = description;

    /// <summary>导航栏图标（FluentAvalonia 内置符号字体，无需额外图标资源）。</summary>
    public FASymbol Symbol { get; } = symbol;
}

/// <summary>播放行为。</summary>
public partial class PlaybackSettingsViewModel() : SettingsPageViewModel("播放", "起播方式与播放队列行为", FASymbol.Play)
{
    [ObservableProperty]
    private bool _resumeLastPosition = true;

    [ObservableProperty]
    private bool _autoAdvanceQueue = true;

    [ObservableProperty]
    private string _startupAction = "自动播放";

    public string[] StartupActions { get; } = ["自动播放", "仅加载并暂停"];
}

/// <summary>显示与渲染。渲染器选项与主窗口控制条同源（VideoRenderer 枚举）。</summary>
public partial class DisplaySettingsViewModel : SettingsPageViewModel
{
    private readonly PlayerSettings _settings;

    public DisplaySettingsViewModel(PlayerSettings settings)
        : base("显示与渲染", "渲染器、全屏与画面缩放", FASymbol.Video)
    {
        _settings = settings;

        // 与主窗口共用同一份设置：打开设置页时下拉框直接反映当前生效值
        _selectedScaling = ScalingOptions.First(option => option.Mode == settings.ScalingMode);
    }

    [ObservableProperty]
    private VideoRenderer _renderer = VideoRenderer.Native;

    [ObservableProperty]
    private bool _startFullScreen = true;

    /// <summary>当前画面缩放方式。已接通引擎：改动即时下发到正在播放的画面。</summary>
    [ObservableProperty]
    private VideoScalingOption _selectedScaling;

    public VideoRenderer[] RendererOptions { get; } = Enum.GetValues<VideoRenderer>();

    /// <summary>
    /// 画面缩放方式候选。标题压短到 4–6 字：下拉框宽度是跟着选中项文字走的，
    /// 长标题会在切换时带动整行布局抖动（宽度限制一律不写死在控件上，仍交给主题度量）。
    /// 具体效果放在说明行里，跟随选中项显示。
    /// </summary>
    public VideoScalingOption[] ScalingOptions { get; } =
    [
        new(VideoScalingMode.Fit, "保持比例填充", "完整显示整个画面，空出来的部分补黑边"),
        new(VideoScalingMode.Stretch, "拉伸填充", "画面铺满整个窗口，不保持比例（画面会变形）"),
        new(VideoScalingMode.Original, "原始大小", "点对点：1 个画面像素对应 1 个屏幕像素，画面大于窗口时可触屏拖动查看"),
        new(VideoScalingMode.Crop, "裁切填充", "保持比例放大到铺满窗口，超出的部分自动裁掉（裁高或裁宽）"),
        new(VideoScalingMode.Free, "自由缩放", "双指捏合缩放、拖动移动画面（0.25×–8×，滚轮可缩放）"),
    ];

    /// <summary>下拉框选中项变化 → 直接写到共享设置，主窗口随即下发到引擎。null 来自绑定拆卸，忽略。</summary>
    partial void OnSelectedScalingChanged(VideoScalingOption value)
    {
        if (value is not null)
        {
            _settings.ScalingMode = value.Mode;
        }
    }
}

/// <summary>画面缩放方式的候选项：引擎取值 + 可读的标题与说明。</summary>
public sealed record VideoScalingOption(VideoScalingMode Mode, string Title, string Description);

/// <summary>
/// 控制栏。控件本身是组件化的（一个控件一个文件，见 Views/PlayerControls），
/// 这里管"哪些控件放在栏里、按什么顺序、容器里放什么"：可以像 ClassIsland 那样把控件包裹进容器、
/// 把控件移进/移出容器、逐层查看容器里的子控件，改动由主窗口立即重建生效。
/// </summary>
public partial class ControlBarSettingsViewModel : SettingsPageViewModel
{
    private readonly Stack<PlayerControlItem> _navigationStack = new();

    public ControlBarSettingsViewModel(PlayerSettings settings)
        : base("控制栏", "拖动调整控制栏里的控件与顺序", FASymbol.Repair)
    {
        Placed = settings.ControlBar;
        DropHandler = new ControlBarDropHandler(this);
    }

    /// <summary>拖放处理器：拖动源的落点都交给它（与 ClassIsland 一样挂在 ViewModel 上）。</summary>
    public ControlBarDropHandler DropHandler { get; }

    /// <summary>控制栏根列表：顺序就是控制栏里的左右顺序。</summary>
    public ObservableCollection<PlayerControlItem> Placed { get; }

    /// <summary>当前打开的容器里的子控件列表（未打开子组件视图时为 null）。</summary>
    [ObservableProperty]
    private ObservableCollection<PlayerControlItem>? _currentContainerChildren;

    /// <summary>当前打开的容器（null = 未打开子组件视图）。</summary>
    [ObservableProperty]
    private PlayerControlItem? _currentContainer;

    /// <summary>当前选中的控件：组件设置、高级设置两个标签页都作用于它。</summary>
    [ObservableProperty]
    private PlayerControlItem? _selectedItem;

    /// <summary>是否处于"查看子组件"层级。</summary>
    public bool IsComponentChildrenViewOpen => CurrentContainer is not null;

    /// <summary>是否可以返回上一层级。</summary>
    public bool CanChildrenNavigateBack => _navigationStack.Count > 0;

    /// <summary>选中的控件是否有自己的设置页（轮播、滚动有参数可调，其余没有）。</summary>
    public bool IsComponentSettingsVisible =>
        SelectedItem is not null && PlayerControlCatalog.HasSettingsView(SelectedItem.Kind);

    /// <summary>选中的控件是否显示高级设置页（每个控件都有外观与隐藏规则可调）。</summary>
    public bool IsComponentAdvancedSettingsVisible => SelectedItem is not null;

    partial void OnSelectedItemChanged(PlayerControlItem? value)
    {
        OnPropertyChanged(nameof(IsComponentSettingsVisible));
        OnPropertyChanged(nameof(IsComponentAdvancedSettingsVisible));
    }

    /// <summary>组件库：固定列出全部可用控件（与 ClassIsland 的组件池一致——不是"还没放的"，
    /// 而是"可以放的"，所以不会越用越空）。拖到上方即往控制栏里再放一个。</summary>
    public IReadOnlyList<PlayerControlLibraryEntry> Library => PlayerControlCatalog.All;

    /// <summary>可用容器（右键菜单"包裹到新容器"的来源）。</summary>
    public IReadOnlyList<PlayerControlLibraryEntry> Containers { get; } =
        [.. PlayerControlCatalog.All.Where(static entry => entry.IsContainer)];

    #region 层级导航

    /// <summary>进入一个容器的子组件视图。</summary>
    public void EnterContainer(PlayerControlItem container)
    {
        if (!container.IsContainer)
        {
            return;
        }

        // 已经在这个容器里就不再压栈（避免重复进入同层）
        if (CurrentContainer is { } current && !ReferenceEquals(current, container))
        {
            _navigationStack.Push(current);
        }

        SetCurrentContainer(container);
    }

    /// <summary>返回上一层级。</summary>
    public void NavigateBack()
    {
        if (!_navigationStack.TryPop(out var parent))
        {
            SetCurrentContainer(null);
            return;
        }

        SetCurrentContainer(parent);
    }

    /// <summary>关闭子组件视图，回到控制栏根层。</summary>
    public void CloseChildrenView()
    {
        _navigationStack.Clear();
        SetCurrentContainer(null);
    }

    private void SetCurrentContainer(PlayerControlItem? container)
    {
        CurrentContainer = container;
        CurrentContainerChildren = container?.Children;
        SelectedItem = null;
        OnPropertyChanged(nameof(IsComponentChildrenViewOpen));
        OnPropertyChanged(nameof(CanChildrenNavigateBack));
    }

    #endregion

    #region 控件操作

    /// <summary>把控件移出控制栏。</summary>
    public void Remove(PlayerControlItem item)
    {
        var list = FindOwnerList(item);
        if (list is null)
        {
            return;
        }

        list.Remove(item);
        if (ReferenceEquals(SelectedItem, item))
        {
            SelectedItem = null;
        }

        // 删除的是当前打开的容器（或它的上层容器）时，退回根层，避免停留在已失效的层级
        if (CurrentContainer is not null && (ReferenceEquals(CurrentContainer, item) || !IsInTree(CurrentContainer)))
        {
            CloseChildrenView();
        }
    }

    /// <summary>右键菜单"向左移动一位"：与拖动排序等价的按钮入口（ClassIsland 的行菜单同款）。</summary>
    public void MovePrevious(PlayerControlItem item) => MoveBy(item, -1);

    /// <summary>右键菜单"向右移动一位"。</summary>
    public void MoveNext(PlayerControlItem item) => MoveBy(item, 1);

    /// <summary>右键菜单"创建副本"：连同外观设置与子控件一起复制。</summary>
    public void Duplicate(PlayerControlItem item)
    {
        var list = FindOwnerList(item);
        var index = list?.IndexOf(item) ?? -1;
        if (list is null || index < 0)
        {
            return;
        }

        list.Insert(index + 1, item.Clone());
    }

    /// <summary>右键菜单"包裹到新容器"：在控件原位置放一个容器，把控件装进去。</summary>
    public void WrapIntoContainer(PlayerControlItem item, PlayerControlKind containerKind)
    {
        var list = FindOwnerList(item);
        var index = list?.IndexOf(item) ?? -1;
        if (list is null || index < 0 || !PlayerControlCatalog.IsContainer(containerKind))
        {
            return;
        }

        var entry = PlayerControlCatalog.All.First(e => e.Kind == containerKind);
        var container = new PlayerControlItem(containerKind, entry.Title);

        list.Insert(index, container);
        list.Remove(item);
        container.Children?.Add(item);
        SelectedItem = container;
    }

    /// <summary>右键菜单"移动到选中的容器"：把控件放进当前打开的容器里。</summary>
    public void MoveToCurrentContainer(PlayerControlItem item)
    {
        if (CurrentContainer is not { } container || ReferenceEquals(item, container) || IsAncestorOf(item, container))
        {
            return;
        }

        var list = FindOwnerList(item);
        if (list is null || ReferenceEquals(list, container.Children))
        {
            return;
        }

        list.Remove(item);
        container.Children?.Add(item);
        SelectedItem = item;
    }

    /// <summary>右键菜单"从容器组件移出"：把当前容器里的控件放回控制栏。</summary>
    public void MoveOutOfCurrentContainer(PlayerControlItem item)
    {
        if (CurrentContainer?.Children is not { } children || !children.Remove(item))
        {
            return;
        }

        Placed.Add(item);
        SelectedItem = item;
    }

    private void MoveBy(PlayerControlItem item, int offset)
    {
        var list = FindOwnerList(item);
        var from = list?.IndexOf(item) ?? -1;
        var to = from + offset;

        if (list is not null && from >= 0 && to >= 0 && to < list.Count)
        {
            list.Move(from, to);
        }
    }

    #endregion

    #region 树查找

    /// <summary>找到控件所在的列表（控制栏根列表或某个容器的子控件列表）。</summary>
    public ObservableCollection<PlayerControlItem>? FindOwnerList(PlayerControlItem item)
    {
        if (Placed.Contains(item))
        {
            return Placed;
        }

        return FindOwnerListIn(Placed, item);
    }

    private static ObservableCollection<PlayerControlItem>? FindOwnerListIn(
        IEnumerable<PlayerControlItem> items, PlayerControlItem target)
    {
        foreach (var item in items)
        {
            if (item.Children is not { } children)
            {
                continue;
            }

            if (children.Contains(target))
            {
                return children;
            }

            if (FindOwnerListIn(children, target) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>找到某个控件列表所属的容器（控制栏根列表返回 null）。</summary>
    public PlayerControlItem? FindOwnerContainer(ObservableCollection<PlayerControlItem> list)
    {
        return ReferenceEquals(list, Placed) ? null : FindOwnerContainerIn(Placed, list);
    }

    /// <summary>落点是否合法：不能把控件拖进它自己或它自己的子级里。</summary>
    public bool CanDropInto(PlayerControlItem item, ObservableCollection<PlayerControlItem> targetList)
    {
        if (FindOwnerContainer(targetList) is not { } owner)
        {
            return true;
        }

        return !ReferenceEquals(owner, item) && !IsAncestorOf(item, owner);
    }

    private static PlayerControlItem? FindOwnerContainerIn(
        IEnumerable<PlayerControlItem> items, ObservableCollection<PlayerControlItem> target)
    {
        foreach (var item in items)
        {
            if (item.Children is not { } children)
            {
                continue;
            }

            if (ReferenceEquals(children, target))
            {
                return item;
            }

            if (FindOwnerContainerIn(children, target) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>控件（含它的子级）是否还在控制栏布局树里。</summary>
    private bool IsInTree(PlayerControlItem item) =>
        IsInTreeIn(Placed, item);

    private static bool IsInTreeIn(IEnumerable<PlayerControlItem> items, PlayerControlItem target)
    {
        foreach (var item in items)
        {
            if (ReferenceEquals(item, target))
            {
                return true;
            }

            if (item.Children is { } children && IsInTreeIn(children, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>candidate 是否是 node 的祖先（含自身判断交给调用方）。</summary>
    private static bool IsAncestorOf(PlayerControlItem candidate, PlayerControlItem node) =>
        candidate.Children is { } children && IsInTreeIn(children, node);

    #endregion
}

/// <summary>音频。音量上限与引擎侧一致（mpv volume-max 已放宽到 200）。</summary>
public partial class AudioSettingsViewModel() : SettingsPageViewModel("音频", "音量上限与音频输出", FASymbol.Audio)
{
    [ObservableProperty]
    private double _maxVolume = 200;

    [ObservableProperty]
    private double _startupVolume = 100;

    [ObservableProperty]
    private string _outputDevice = "自动（跟随系统默认设备）";

    public string[] OutputDevices { get; } = ["自动（跟随系统默认设备）", "独占模式（WASAPI）"];
}

/// <summary>媒体库。扩展名统计取自真实数据（MediaKindResolver），不含占位数字。</summary>
public partial class LibrarySettingsViewModel() : SettingsPageViewModel("媒体库", "扫描目录与识别范围", FASymbol.Library)
{
    [ObservableProperty]
    private string _libraryRoot = string.Empty;

    [ObservableProperty]
    private bool _scanOnStartup = true;

    [ObservableProperty]
    private bool _watchFolderChanges = true;

    public string ExtensionCountText { get; } =
        $"共识别 {MediaKindResolver.SupportedExtensions.Count()} 种音视频/图片扩展名";

    public string ExtensionSummary { get; } =
        string.Join("  ", MediaKindResolver.SupportedExtensions.Take(18)) + " …";
}

/// <summary>关于。版本与运行环境都是实时读取的真实值。</summary>
public partial class AboutSettingsViewModel() : SettingsPageViewModel("关于", "版本与运行环境", FASymbol.Help)
{
    public string AppVersion { get; } = ResolveAppVersion();

    public string FrameworkText { get; } = RuntimeInformation.FrameworkDescription;

    public string OsText { get; } = $"{RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}";

    public string RenderPipeline { get; } = "ANGLE（EGL over D3D11）· 默认渲染器 Native（零拷贝 d3d11va）";

    public string PlaybackCore { get; } = "官方 libmpv（libmpv-2.dll）· 引擎实现 Player.Playback/MpvMediaEngine";

    private static string ResolveAppVersion()
    {
        var version = typeof(AboutSettingsViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(version))
        {
            return "0.0.0";
        }

        // InformationalVersion 形如 "1.2.3+gitHash"，界面只显示版本号部分
        var plusIndex = version.IndexOf('+');
        return plusIndex > 0 ? version[..plusIndex] : version;
    }
}