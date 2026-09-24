using Player.Playback;
using Xunit;

namespace Player.Tests;

public class MediaKindResolverTests
{
    [Theory]
    [InlineData("movie.mp4", MediaKind.Video)]
    [InlineData("movie.MKV", MediaKind.Video)]
    [InlineData("clip.m2ts", MediaKind.Video)]
    [InlineData("sample.F4V", MediaKind.Video)]
    [InlineData("clip.mts", MediaKind.Video)]
    [InlineData("song.mka", MediaKind.Audio)]
    [InlineData("song.flac", MediaKind.Audio)]
    [InlineData("song.MP3", MediaKind.Audio)]
    [InlineData("photo.JPG", MediaKind.Image)]
    [InlineData("anim.gif", MediaKind.Image)]
    [InlineData("readme.txt", MediaKind.Unknown)]
    [InlineData("noextension", MediaKind.Unknown)]
    public void Resolve_ReturnsExpectedKind(string fileName, MediaKind expected)
    {
        Assert.Equal(expected, MediaKindResolver.Resolve(fileName));
    }

    [Fact]
    public void IsSupported_RejectsNonMedia()
    {
        Assert.True(MediaKindResolver.IsSupported(@"D:\media\a.webm"));
        Assert.False(MediaKindResolver.IsSupported(@"D:\media\a.docx"));
    }
}