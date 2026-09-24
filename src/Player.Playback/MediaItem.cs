namespace Player.Playback;

/// <summary>
/// 一条可播放的媒体条目。
/// </summary>
/// <param name="Path">文件绝对路径。</param>
/// <param name="Kind">媒体类型。</param>
/// <param name="Title">显示名称，默认取文件名。</param>
public sealed record MediaItem(string Path, MediaKind Kind, string Title)
{
    public static MediaItem FromPath(string path) =>
        new(path, MediaKindResolver.Resolve(path), System.IO.Path.GetFileName(path));
}