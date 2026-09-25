using Player.Playback;
using Xunit;

namespace Player.Tests;

/// <summary>
/// 自由缩放的换算：倍率与 mpv video-zoom（log2）的互换、捏合增量、缩放后的显示尺寸。
/// 基准：mpv 的 dwidth/dheight 不含窗口适配与缩放，显示尺寸必须自己按适配比例乘倍率算出来。
/// </summary>
public class VideoZoomMathTests
{
    [Theory]
    [InlineData(2d, 1d)]     // 2 倍 → video-zoom = 1
    [InlineData(1d, 0d)]     // 不缩放 → 0
    [InlineData(0.25d, -2d)]
    [InlineData(8d, 3d)]
    public void ToVideoZoom_UsesLog2(double scale, double expected)
    {
        Assert.Equal(expected, VideoZoomMath.ToVideoZoom(scale), 9);
    }

    [Fact]
    public void ZoomConversions_RoundTrip()
    {
        foreach (var scale in new[] { 0.25d, 0.5d, 1d, 1.5d, 8d })
        {
            Assert.Equal(scale, VideoZoomMath.FromVideoZoom(VideoZoomMath.ToVideoZoom(scale)), 9);
        }
    }

    [Theory]
    [InlineData(0.1d, 0.25d)]
    [InlineData(100d, 8d)]
    [InlineData(2d, 2d)]
    public void ClampScale_KeepsWithinRange(double scale, double expected)
    {
        Assert.Equal(expected, VideoZoomMath.ClampScale(scale));
    }

    [Theory]
    [InlineData(200d, 100d, 2d)]     // 指距翻倍 = 放大一倍
    [InlineData(100d, 100d, 1d)]     // 没动
    [InlineData(50d, 100d, 0.5d)]
    [InlineData(100d, 0d, 1d)]       // 起点为 0 视为无变化，避免除零放大
    [InlineData(0d, 100d, 1d)]
    public void ScaleFromPinch_IsDistanceRatio(double distance, double previous, double expected)
    {
        Assert.Equal(expected, VideoZoomMath.ScaleFromPinch(distance, previous), 9);
    }

    [Fact]
    public void ScaledSize_AtOneX_FitsTheView()
    {
        // 4K 画面放进 1280x717 视频区：按高度适配（0.3319），宽度留黑边
        var (width, height) = VideoZoomMath.ScaledSize(3840d, 2160d, 1280d, 717d, 1d);

        Assert.Equal(717d, height, 6);
        Assert.Equal(1274.67d, width, 1);
        // 适配的判据：两个方向都不超出视频区，且至少一个方向刚好贴边
        Assert.True(width <= 1280d && height <= 717d);
        Assert.True(Math.Abs(width - 1280d) < 0.01d || Math.Abs(height - 717d) < 0.01d);
    }

    [Fact]
    public void ScaledSize_ScalesLinearlyWithFactor()
    {
        var one = VideoZoomMath.ScaledSize(3840d, 2160d, 1280d, 717d, 1d);
        var two = VideoZoomMath.ScaledSize(3840d, 2160d, 1280d, 717d, 2d);

        Assert.Equal(one.Width * 2d, two.Width, 6);
        Assert.Equal(one.Height * 2d, two.Height, 6);
    }

    [Theory]
    [InlineData(0d, 2160d)]
    [InlineData(3840d, 0d)]
    public void ScaledSize_WithoutGeometry_ReturnsZero(double sourceWidth, double sourceHeight)
    {
        Assert.Equal((0d, 0d), VideoZoomMath.ScaledSize(sourceWidth, sourceHeight, 1280d, 717d, 2d));
    }

    /// <summary>
    /// 缩放与平移的联动：1× 时画面刚好铺满、无处可移；放大到 2× 后左右各出现半个溢出的余量可拖。
    /// 这条同时守住两件事——显示尺寸随倍率变、平移上限按显示尺寸算。
    /// </summary>
    [Fact]
    public void PanRange_GrowsWithZoom()
    {
        var view = (Width: 1280d, Height: 717d);
        var atOne = VideoZoomMath.ScaledSize(3840d, 2160d, view.Width, view.Height, 1d);
        var atTwo = VideoZoomMath.ScaledSize(3840d, 2160d, view.Width, view.Height, 2d);

        // 1×：宽度没铺满（按高度适配），高度正好贴边 → 无可平移余量
        Assert.Equal(0d, VideoPanMath.MaxPan(atOne.Height, view.Height), 9);

        // 2×：高 1434、宽 2549 都超出视频区 → 余量 = (显示尺寸 - 视频区尺寸) / (2 × 显示尺寸)
        Assert.Equal((atTwo.Height - view.Height) / (2d * atTwo.Height), VideoPanMath.MaxPan(atTwo.Height, view.Height), 6);
        Assert.Equal((atTwo.Width - view.Width) / (2d * atTwo.Width), VideoPanMath.MaxPan(atTwo.Width, view.Width), 6);
        Assert.True(VideoPanMath.MaxPan(atTwo.Width, view.Width) > 0d);
    }
}