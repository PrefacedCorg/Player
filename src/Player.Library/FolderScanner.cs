using Player.Playback;

namespace Player.Library;

/// <summary>
/// 文件夹扫描器。M0 阶段同步实现，M3 会改为后台增量索引并落 SQLite 缓存。
/// </summary>
public sealed class FolderScanner
{
    /// <summary>
    /// 扫描目录下的受支持媒体文件，按文件名自然顺序返回。
    /// </summary>
    /// <param name="folder">目标目录。</param>
    /// <param name="recursive">是否递归子目录。</param>
    public IReadOnlyList<MediaItem> Scan(string folder, bool recursive = false)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var items = new List<MediaItem>();

        foreach (var path in Directory.EnumerateFiles(folder, "*", option))
        {
            if (MediaKindResolver.IsSupported(path))
            {
                items.Add(MediaItem.FromPath(path));
            }
        }

        items.Sort(static (a, b) => NaturalComparer.Compare(a.Title, b.Title));
        return items;
    }
}