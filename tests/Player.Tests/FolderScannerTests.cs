using Player.Library;
using Player.Playback;
using Xunit;

namespace Player.Tests;

public class FolderScannerTests
{
    [Fact]
    public void Scan_ReturnsSupportedMediaOnly_InNaturalOrder()
    {
        var dir = Directory.CreateTempSubdirectory("player-scan-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "clip10.mp4"), string.Empty);
            File.WriteAllText(Path.Combine(dir.FullName, "clip2.mp4"), string.Empty);
            File.WriteAllText(Path.Combine(dir.FullName, "cover.jpg"), string.Empty);
            File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), string.Empty);

            var items = new FolderScanner().Scan(dir.FullName);

            Assert.Equal(3, items.Count);
            Assert.Equal(["clip2.mp4", "clip10.mp4", "cover.jpg"], items.Select(static i => i.Title));
            Assert.Equal(MediaKind.Image, items[2].Kind);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Scan_ReturnsEmptyForMissingFolder()
    {
        var items = new FolderScanner().Scan(@"D:\definitely-not-exists-player");
        Assert.Empty(items);
    }
}