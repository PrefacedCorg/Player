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

    /// <summary>
    /// 画面缩放方式。播放中切换需立即生效（实现方直接改 mpv 运行期属性，不重新加载文件）。
    /// </summary>
    VideoScalingMode ScalingMode { get; set; }

    /// <summary>
    /// 按像素位移平移画面（触屏拖动调用）。仅"原始大小"模式且画面大于视频区时有效果，
    /// 内部按可平移上限夹取，不会把画面拖出边界。
    /// </summary>
    void PanVideo(double deltaX, double deltaY);

    /// <summary>复位画面平移（切换缩放方式、换片时调用）。</summary>
    void ResetVideoPan();

    /// <summary>
    /// 按倍率增量缩放画面（捏合/滚轮调用），仅"自由缩放"模式有效。
    /// 倍率夹取到 <see cref="VideoZoomMath.MinScale"/>–<see cref="VideoZoomMath.MaxScale"/>，
    /// 缩放后画面平移量会重新夹取到新的可视范围内。
    /// </summary>
    void ZoomVideo(double factor);
}