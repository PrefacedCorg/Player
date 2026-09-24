using Player.Library;
using Xunit;

namespace Player.Tests;

public class NaturalComparerTests
{
    [Fact]
    public void Sort_OrdersNumericPartsNaturally()
    {
        var names = new[] { "第10集.mp4", "第2集.mp4", "第1集.mp4", "第02集.mp4" };

        Array.Sort(names, NaturalComparer.Compare);

        Assert.Equal(["第1集.mp4", "第2集.mp4", "第02集.mp4", "第10集.mp4"], names);
    }

    [Fact]
    public void Compare_HandlesNullsAndCaseInsensitiveText()
    {
        Assert.True(NaturalComparer.Compare(null, "a") < 0);
        Assert.Equal(0, NaturalComparer.Compare("TrackA", "tracka"));
    }
}