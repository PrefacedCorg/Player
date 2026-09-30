using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Player.Playback;

namespace Player.App;

/// <summary>
/// 应用级实时设置：主窗口与设置窗口共用同一个实例，改动立即生效。
/// <para>
/// 目前只有"画面缩放方式"实现了引擎下发；设置页里的其余控件仍是界面状态。
/// 后续接入落盘时，这里就是持久化的读写点（属性值 ↔ 配置文件）。
/// </para>
/// </summary>
public partial class PlayerSettings : ObservableObject
{
    /// <summary>画面缩放方式。变化后由主窗口下发给播放引擎，播放中即切即生效。</summary>
    [ObservableProperty]
    private VideoScalingMode _scalingMode = VideoScalingMode.Fit;

    /// <summary>
    /// 底部播放控制栏的布局：多行（照抄 ClassIsland-2.0 的主界面行机制），
    /// 每行（<see cref="PlayerControlLine"/>）横向排一组控件，行从上到下排开，行可增删。
    /// 行内元素可以是容器型控件（轮播/滚动/分组/堆叠），容器里的子控件在 PlayerControlItem.Children。
    /// 主窗口的控制栏宿主直接读这个行列表递归渲染，设置页改这里即时生效。
    /// </summary>
    public ObservableCollection<PlayerControlLine> ControlBar { get; } = PlayerControlCatalog.CreateDefaultLayout();

    /// <summary>
    /// 视频区触摸层上的手势设置（单击 / 双击 / 左滑 / 右滑分别对应什么操作）。
    /// 主窗口在触摸层事件里执行手势时直接读这份设置，设置页改完即生效。
    /// </summary>
    public VideoGestureSettings Gestures { get; } = new();
}

/// <summary>视频区手势（单击 / 双击 / 左右滑动）可对应的操作。</summary>
public enum VideoGestureAction
{
    /// <summary>不做任何事。</summary>
    None,

    /// <summary>呼出 / 收起控制层。</summary>
    ToggleControls,

    /// <summary>播放 / 暂停。</summary>
    TogglePlayPause,

    /// <summary>停止播放并清空队列。</summary>
    Stop,

    /// <summary>播放队列里的上一个。</summary>
    Previous,

    /// <summary>播放队列里的下一个。</summary>
    Next,

    /// <summary>快退 10 秒。</summary>
    SeekBackward,

    /// <summary>快进 10 秒。</summary>
    SeekForward,

    /// <summary>音量 +5%。</summary>
    VolumeUp,

    /// <summary>音量 -5%。</summary>
    VolumeDown,

    /// <summary>进入 / 退出全屏。</summary>
    ToggleFullscreen,
}

/// <summary>给设置页下拉框用的「手势操作 → 中文名」选项表。</summary>
public static class VideoGestureActionOptions
{
    public sealed record Option(VideoGestureAction Value, string Label);

    public static readonly IReadOnlyList<Option> All =
    [
        new(VideoGestureAction.None, "不执行"),
        new(VideoGestureAction.ToggleControls, "呼出/收起控制层"),
        new(VideoGestureAction.TogglePlayPause, "播放/暂停"),
        new(VideoGestureAction.Stop, "停止播放"),
        new(VideoGestureAction.Previous, "上一个"),
        new(VideoGestureAction.Next, "下一个"),
        new(VideoGestureAction.SeekBackward, "快退 10 秒"),
        new(VideoGestureAction.SeekForward, "快进 10 秒"),
        new(VideoGestureAction.VolumeUp, "音量 +5%"),
        new(VideoGestureAction.VolumeDown, "音量 -5%"),
        new(VideoGestureAction.ToggleFullscreen, "切换全屏"),
    ];

    /// <summary>按操作取中文名（日志用；注册表里一定有这一项）。</summary>
    public static string LabelOf(VideoGestureAction action) =>
        All.First(option => option.Value == action).Label;
}

/// <summary>滑动手势可对应的连续调节项：滑多少调多少，方向决定增减（向右 / 向上为增加）。</summary>
public enum VideoSwipeAdjust
{
    /// <summary>不调节。</summary>
    None,

    /// <summary>调整播放进度：按固定比例折算（每像素 0.25 秒），与视频区大小 / 时长无关。</summary>
    Seek,

    /// <summary>调整音量：纵向滑满视频区高 = 100%。</summary>
    Volume,

    /// <summary>调整画面亮度：纵向滑满视频区高 = 100 个单位（mpv 取值范围 -100–100）。</summary>
    Brightness,
}

/// <summary>给设置页下拉框用的「滑动调节 → 中文名」选项表。</summary>
public static class VideoSwipeAdjustOptions
{
    public sealed record Option(VideoSwipeAdjust Value, string Label);

    public static readonly IReadOnlyList<Option> All =
    [
        new(VideoSwipeAdjust.None, "不调节"),
        new(VideoSwipeAdjust.Seek, "调整进度"),
        new(VideoSwipeAdjust.Volume, "调整音量"),
        new(VideoSwipeAdjust.Brightness, "调整亮度"),
    ];

    /// <summary>按调节项取中文名（日志用；选项表里一定有这一项）。</summary>
    public static string LabelOf(VideoSwipeAdjust adjust) =>
        All.First(option => option.Value == adjust).Label;
}

/// <summary>
/// 视频区手势设置：触摸层上四类手势各自对应的操作。
/// 单击 / 双击走离散操作（<see cref="VideoGestureAction"/>）；
/// 滑动是按位移比例的连续调节（<see cref="VideoSwipeAdjust"/>）——滑多少调多少，方向决定增减，
/// 因此左滑与右滑共用一项（同一种调节、只差正负号），纵向滑动才按手指落在左 / 右半区分成两项。
/// 按下 / 抬起、双击与拖动方向的判定都在 <see cref="Player.App.Views.VideoTouchOverlay"/> 里，
/// 这里只存"识别出来之后调什么"。
/// </summary>
public partial class VideoGestureSettings : ObservableObject
{
    /// <summary>单击对应的操作（默认呼出 / 收起控制层）。</summary>
    [ObservableProperty] private VideoGestureAction _tapAction = VideoGestureAction.ToggleControls;

    /// <summary>双击对应的操作（默认播放 / 暂停）。</summary>
    [ObservableProperty] private VideoGestureAction _doubleTapAction = VideoGestureAction.TogglePlayPause;

    /// <summary>左右滑动调节什么（默认调整进度）。</summary>
    [ObservableProperty] private VideoSwipeAdjust _horizontalSwipeAdjust = VideoSwipeAdjust.Seek;

    /// <summary>画面左半区上下滑动调节什么（默认调整亮度）。</summary>
    [ObservableProperty] private VideoSwipeAdjust _leftVerticalSwipeAdjust = VideoSwipeAdjust.Brightness;

    /// <summary>画面右半区上下滑动调节什么（默认调整音量）。</summary>
    [ObservableProperty] private VideoSwipeAdjust _rightVerticalSwipeAdjust = VideoSwipeAdjust.Volume;

    /// <summary>左右滑动调整进度时，拖动过程中画面实时跟随（暂停预览目标位置，抬手后恢复原播放状态）。</summary>
    [ObservableProperty] private bool _liveSeekWhileSwiping = true;

    // 给设置页 ComboBox 用的包装（Avalonia 无 SelectedValuePath，直接绑选项对象）

    [System.Text.Json.Serialization.JsonIgnore]
    public VideoGestureActionOptions.Option TapActionOption
    {
        get => VideoGestureActionOptions.All.First(o => o.Value == TapAction);
        set => TapAction = value.Value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public VideoGestureActionOptions.Option DoubleTapActionOption
    {
        get => VideoGestureActionOptions.All.First(o => o.Value == DoubleTapAction);
        set => DoubleTapAction = value.Value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public VideoSwipeAdjustOptions.Option HorizontalSwipeAdjustOption
    {
        get => VideoSwipeAdjustOptions.All.First(o => o.Value == HorizontalSwipeAdjust);
        set => HorizontalSwipeAdjust = value.Value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public VideoSwipeAdjustOptions.Option LeftVerticalSwipeAdjustOption
    {
        get => VideoSwipeAdjustOptions.All.First(o => o.Value == LeftVerticalSwipeAdjust);
        set => LeftVerticalSwipeAdjust = value.Value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public VideoSwipeAdjustOptions.Option RightVerticalSwipeAdjustOption
    {
        get => VideoSwipeAdjustOptions.All.First(o => o.Value == RightVerticalSwipeAdjust);
        set => RightVerticalSwipeAdjust = value.Value;
    }
}