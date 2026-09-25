namespace Player.Playback;

/// <summary>
/// 画面缩放方式：视频宽高比与视频区形状不一致时的填充策略。
/// <para>
/// 每个取值对应 mpv 上的一组属性组合（keepaspect / video-unscaled / panscan），
/// 具体下发见 <see cref="MpvMediaEngine"/>，四者互斥且都是运行时可改的。
/// </para>
/// </summary>
public enum VideoScalingMode
{
    /// <summary>保持纵横比填充：完整显示整个画面，差出来的部分补黑边。mpv 默认行为。</summary>
    Fit,

    /// <summary>拉伸填充：画面铺满整个视频区，不保持纵横比（画面会变形）。</summary>
    Stretch,

    /// <summary>
    /// 原始大小（点对点）：1 个画面像素对应 1 个屏幕像素，不做任何缩放；
    /// 画面比视频区大时超出部分被裁掉，可触屏拖动查看（见 <see cref="MpvMediaEngine.PanVideo"/>）。
    /// </summary>
    Original,

    /// <summary>保持纵横比裁切填充：等比放大到铺满视频区，超出部分自动裁掉（裁高或裁宽）。</summary>
    Crop,

    /// <summary>
    /// 自由缩放：保持纵横比，倍率由用户捏合/滚轮控制（见 <see cref="MpvMediaEngine.ZoomVideo"/>），
    /// 拖动可平移画面。1× 等于铺满视频区（与"保持比例填充"同观感），可缩到 0.25×、放到 8×。
    /// </summary>
    Free,
}