namespace Player.Playback;

/// <summary>
/// 自由缩放模式的换算（纯函数，便于单测）。
/// <para>
/// 与 mpv 的对应关系：<c>video-zoom</c> 以 2 为底的对数表示倍率（0 = 不缩放，1 = 2 倍），
/// 且"不缩放"指的是画面正好铺满视频区（适配尺寸），不是原始像素尺寸。
/// </para>
/// <para>
/// 显示尺寸的算法有一处必须显式处理：mpv 的 <c>dwidth/dheight</c> 是"未经窗口适配与缩放"的画面尺寸
/// （实测：4K 素材在 1280x717 视频区、panscan=1 时仍报 3840x2160），
/// 所以缩放后的显示尺寸要自己算：先按视频区求适配比例，再乘缩放倍率。
/// </para>
/// </summary>
public static class VideoZoomMath
{
    /// <summary>最小缩放倍率（0.25×，画面缩到铺满窗口的四分之一，用于观看整幅小图）。</summary>
    public const double MinScale = 0.25d;

    /// <summary>最大缩放倍率（8×，再大只是看马赛克）。</summary>
    public const double MaxScale = 8d;

    /// <summary>把缩放倍率夹取到允许范围。</summary>
    public static double ClampScale(double scale) => Math.Clamp(scale, MinScale, MaxScale);

    /// <summary>捏合时的倍率增量：当前指距 / 上一次指距（1.0 表示没有缩放）。</summary>
    public static double ScaleFromPinch(double distance, double previousDistance) =>
        previousDistance > 0d && distance > 0d ? distance / previousDistance : 1d;

    /// <summary>缩放倍率 → mpv 的 video-zoom（log2 单位）。</summary>
    public static double ToVideoZoom(double scale) => Math.Log2(scale);

    /// <summary>mpv 的 video-zoom → 缩放倍率。</summary>
    public static double FromVideoZoom(double videoZoom) => Math.Pow(2d, videoZoom);

    /// <summary>
    /// 缩放后的显示尺寸：按视频区求出"铺满窗口"的适配比例，再乘缩放倍率。
    /// 参数为 0（未起播、纯音频）时返回 (0, 0)，调用方应视为"几何未知"。
    /// </summary>
    public static (double Width, double Height) ScaledSize(
        double sourceWidth, double sourceHeight, double viewWidth, double viewHeight, double scale)
    {
        if (sourceWidth <= 0d || sourceHeight <= 0d || viewWidth <= 0d || viewHeight <= 0d || scale <= 0d)
        {
            return (0d, 0d);
        }

        var fit = Math.Min(viewWidth / sourceWidth, viewHeight / sourceHeight);
        var factor = fit * scale;
        return (sourceWidth * factor, sourceHeight * factor);
    }
}