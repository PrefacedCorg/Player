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
/// 这里管"哪些控件放在栏里、按什么顺序"：已放置的横向排列（与真实控制栏同序），
/// 未放置的留在组件库，两边靠拖拽互通，改动由主窗口立即重建生效。
/// </summary>
public partial class ControlBarSettingsViewModel : SettingsPageViewModel
{
    public ControlBarSettingsViewModel(PlayerSettings settings)
        : base("控制栏", "拖动调整控制栏里的控件与顺序", FASymbol.Repair)
    {
        Placed = settings.ControlBar;
        DropHandler = new ControlBarDropHandler(this);
    }

    /// <summary>拖放处理器：拖动源的落点都交给它（与 ClassIsland 一样挂在 ViewModel 上）。</summary>
    public ControlBarDropHandler DropHandler { get; }

    /// <summary>已放置的控件：列表顺序就是控制栏里的左右顺序。</summary>
    public ObservableCollection<PlayerControlItem> Placed { get; }

    /// <summary>
    /// 组件库：固定列出全部可用控件（与 ClassIsland 的组件池一致——不是"还没放的"，
    /// 而是"可以放的"，所以不会越用越空）。拖到上方即往控制栏里再放一个。
    /// </summary>
    public IReadOnlyList<PlayerControlLibraryEntry> Library => PlayerControlCatalog.All;

    /// <summary>把控件移出控制栏（拖回组件库）。</summary>
    public void Remove(PlayerControlItem item) => Placed.Remove(item);

    /// <summary>右键菜单"向左移动"：与拖动排序等价的按钮入口（ClassIsland 的行菜单同款）。</summary>
    public void MovePrevious(PlayerControlItem item) => MoveBy(item, -1);

    /// <summary>右键菜单"向右移动"。</summary>
    public void MoveNext(PlayerControlItem item) => MoveBy(item, 1);

    /// <summary>右键菜单"创建副本"。</summary>
    public void Duplicate(PlayerControlItem item)
    {
        var index = Placed.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        Placed.Insert(index + 1, new PlayerControlItem(item.Kind, item.Title));
    }

    private void MoveBy(PlayerControlItem item, int offset)
    {
        var from = Placed.IndexOf(item);
        var to = from + offset;

        if (from >= 0 && to >= 0 && to < Placed.Count)
        {
            Placed.Move(from, to);
        }
    }
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