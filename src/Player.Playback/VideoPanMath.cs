namespace Player.Playback;

/// <summary>
/// 画面平移的换算与限位（纯函数，便于单测）。
/// <para>
/// 单位依据：mpv 的 video-pan-x/y 取值为"当前显示画面尺寸的比例"，1.0 相当于把画面位移一个画面宽度。
/// 依据是 mpv 源码 video/out/aspect.c 的公式（在其自带 positioning.lua 注释中被逐字引用）：
/// <c>ml = (osdW - width) * (align + 1) / 2 + panX * width</c>，
/// 其中 width 为缩放后的画面宽度、osdW 为视频区宽度、ml 为画面左边缘相对视频区左边缘的位置。
/// </para>
/// <para>
/// 由此推出：像素位移 dx 对应 <c>panX += dx / 显示宽度</c>；
/// 画面比视频区大时，可平移上限为 <c>±(显示宽度 - 视频区宽度) / (2 * 显示宽度)</c>——
/// 恰好是把画面边缘拖到视频区边缘，再拖就没有内容了。画面不比视频区大时上限为 0（不可平移）。
/// </para>
/// </summary>
public static class VideoPanMath
{
    /// <summary>把像素位移换算为 mpv 的 pan 增量（按显示尺寸归一化）。</summary>
    public static double ToPanDelta(double pixelDelta, double displayedSize) =>
        displayedSize > 0d ? pixelDelta / displayedSize : 0d;

    /// <summary>可平移上限（绝对值）。画面不比视频区大时为 0。</summary>
    public static double MaxPan(double displayedSize, double windowSize) =>
        displayedSize > windowSize && displayedSize > 0d
            ? (displayedSize - windowSize) / (2d * displayedSize)
            : 0d;

    /// <summary>在当前值上累加增量，并夹取到可平移范围内。</summary>
    public static double ClampPan(double current, double delta, double displayedSize, double windowSize)
    {
        var max = MaxPan(displayedSize, windowSize);
        return Math.Clamp(current + delta, -max, max);
    }
}