namespace Player.Playback;

/// <summary>
/// 媒体类型。用于决定交给哪个呈现通道处理。
/// </summary>
public enum MediaKind
{
    Unknown,
    Video,
    Audio,
    Image,
}