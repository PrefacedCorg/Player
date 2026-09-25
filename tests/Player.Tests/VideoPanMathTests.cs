using Player.Playback;
using Xunit;

namespace Player.Tests;

/// <summary>
/// 点对点模式下的平移换算与限位。
/// 基准来自 mpv 源码 aspect.c 的公式 ml = (osdW - width) * (align + 1) / 2 + panX * width：
/// panX 以显示画面尺寸归一化，可平移上限 = (显示尺寸 - 视频区尺寸) / (2 * 显示尺寸)。
/// </summary>
public class VideoPanMathTests
{
    [Fact]
    public void ToPanDelta_NormalizesByDisplayedSize()
    {
        // 4K 画面拖动 384 像素 = 0.1 个画面宽度
        Assert.Equal(0.1d, VideoPanMath.ToPanDelta(384d, 3840d), 6);
        Assert.Equal(-0.05d, VideoPanMath.ToPanDelta(-192d, 3840d), 6);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-100d)]
    public void ToPanDelta_WithoutDisplayedSize_ReturnsZero(double displayedSize)
    {
        Assert.Equal(0d, VideoPanMath.ToPanDelta(500d, displayedSize));
    }

    [Theory]
    // 4K 画面在 1280 宽视频区里：左右各可移动 (3840-1280)/2 = 1280 像素
    [InlineData(3840d, 1280d, 0.333333d)]
    // 画面比视频区小：没有可移动余量
    [InlineData(480d, 1280d, 0d)]
    // 尺寸相同：没有余量
    [InlineData(1280d, 1280d, 0d)]
    public void MaxPan_IsHalfOfOverflowInDisplayedUnits(double displayedSize, double windowSize, double expected)
    {
        Assert.Equal(expected, VideoPanMath.MaxPan(displayedSize, windowSize), 6);
    }

    [Fact]
    public void ClampPan_SingleSwipeCannotPassTheEdge()
    {
        // 一次拖出远超余量的距离，结果仍停在边界（画面边缘刚好贴住视频区边缘）
        var pan = VideoPanMath.ClampPan(0d, VideoPanMath.ToPanDelta(5000d, 3840d), 3840d, 1280d);
        Assert.Equal(0.333333d, pan, 6);

        pan = VideoPanMath.ClampPan(0d, VideoPanMath.ToPanDelta(-5000d, 3840d), 3840d, 1280d);
        Assert.Equal(-0.333333d, pan, 6);
    }

    [Fact]
    public void ClampPan_SwipeOfHalfOverflowReachesEdgeExactly()
    {
        // 拖动手感校准：位移等于 (显示宽 - 视频区宽)/2 时，正好抵达边界
        var overflowHalf = (3840d - 1280d) / 2d;
        var pan = VideoPanMath.ClampPan(0d, VideoPanMath.ToPanDelta(overflowHalf, 3840d), 3840d, 1280d);
        Assert.Equal(VideoPanMath.MaxPan(3840d, 1280d), pan, 9);
    }

    [Fact]
    public void ClampPan_AccumulatesAndStaysInsideBounds()
    {
        var pan = 0d;
        for (var i = 0; i < 20; i++)
        {
            pan = VideoPanMath.ClampPan(pan, VideoPanMath.ToPanDelta(200d, 3840d), 3840d, 1280d);
        }

        Assert.Equal(VideoPanMath.MaxPan(3840d, 1280d), pan, 9);
    }

    [Fact]
    public void ClampPan_NotPannable_StaysAtZero()
    {
        var pan = VideoPanMath.ClampPan(0d, VideoPanMath.ToPanDelta(800d, 1280d), 1280d, 1280d);
        Assert.Equal(0d, pan);
    }
}