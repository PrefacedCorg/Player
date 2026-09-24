namespace Player.Playback;

/// <summary>
/// 播放引擎抽象。视频/音频走 libmpv；图片后续由独立呈现通道实现同一层语义。
/// 实现方不依赖任何 UI 框架。
/// </summary>
public interface IMediaEngine : IDisposable
{
    event EventHandler<MediaEngineState>? StateChanged;

    /// <summary>
    /// 解码链路就绪（首次 reconfigure）。M0 阶段用来确认 demux + 解码器真的跑起来了，
    /// 而不是只完成了 LoadFile 调用。
    /// </summary>
    event EventHandler<MediaKind>? DecoderReady;

    MediaEngineState State { get; }

    /// <summary>引擎底层是否可用（libmpv 是否成功装载）。</summary>
    bool IsAvailable { get; }

    void Load(MediaItem item, bool autoPlay = true);

    /// <summary>把文件追加到播放队列（不打断当前播放）。拖入/一次打开多个文件时用。</summary>
    void Enqueue(MediaItem item);

    void Play();

    void Pause();

    void TogglePause();

    void Seek(TimeSpan position);

    void Stop();

    double Volume { get; set; }
}