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
}