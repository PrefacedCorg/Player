using System.Collections.Frozen;

namespace Player.Playback;

/// <summary>
/// 按扩展名判断媒体类型。不做内容探测，保证扫描阶段零 IO 开销。
/// </summary>
public static class MediaKindResolver
{
    private static readonly FrozenSet<string> VideoExtensions = new[]
    {
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".rmvb", ".rm", ".3gp", ".vob",
        // Flash 系容器
        ".f4v", ".asf", ".divx",
        // 广电/专业容器
        ".mts", ".m2t", ".tp", ".trp", ".mxf", ".dv", ".amv", ".ogv", ".m2v",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> AudioExtensions = new[]
    {
        ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".oga", ".opus",
        ".wma", ".ape", ".alac", ".aif", ".aiff", ".mka", ".ac3", ".dts", ".amr",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ImageExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".avif",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static MediaKind Resolve(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            return MediaKind.Unknown;
        }

        // .gif 同时可能是动图或静态图，统一交给图片通道处理。
        if (ImageExtensions.Contains(ext))
        {
            return MediaKind.Image;
        }

        if (VideoExtensions.Contains(ext))
        {
            return MediaKind.Video;
        }

        return AudioExtensions.Contains(ext) ? MediaKind.Audio : MediaKind.Unknown;
    }

    public static bool IsSupported(string path) => Resolve(path) != MediaKind.Unknown;

    public static IEnumerable<string> SupportedExtensions =>
        VideoExtensions.Concat(AudioExtensions).Concat(ImageExtensions);
}