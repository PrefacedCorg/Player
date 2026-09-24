using System.Text;
using HanumanInstitute.LibMpv;

namespace Player.Playback;

/// <summary>
/// 基于 libmpv 的播放引擎实现。
/// <para>
/// 生命周期约定：<see cref="MpvContext"/> 由渲染视图（OpenGlView / NativeView）创建并拥有，
/// 本类只持有引用做控制与状态轮询，绝不主动 Dispose 该上下文。
/// </para>
/// <para>
/// 状态获取采用 250ms 轮询而非属性订阅：实现简单、跨线程安全，
/// 对进度条精度足够（M1 如需更高精度可切换到 mpv 属性观察机制）。
/// </para>
/// </summary>
public sealed class MpvMediaEngine : IMediaEngine
{
    /// <summary>状态轮询间隔。100ms 兼顾进度条精度与"首帧"测量的时间分辨率。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>音量上限（%）。100 为原始增益，之上是软件放大，可能削波失真。</summary>
    private const double MaxVolume = 200d;

    private readonly MpvContext _mpv;
    private readonly Timer _pollTimer;
    private readonly object _gate = new();
    private MediaEngineState _state = MediaEngineState.Empty;
    private bool _videoDecoderReady;
    private string? _videoDecoderSummary;
    private bool _disposed;

    public MpvMediaEngine(MpvContext mpv)
    {
        _mpv = mpv ?? throw new ArgumentNullException(nameof(mpv));
        _mpv.AudioReconfig += OnAudioReconfig;
        _mpv.VideoReconfig += OnVideoReconfig;
        // mpv 默认 volume-max=130，界面要支持 200% 必须先放宽它的上限，否则会被 mpv 夹回 130
        Safe(() => _mpv.VolumeMax.Set(MaxVolume));
        _pollTimer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    public event EventHandler<MediaEngineState>? StateChanged;

    public event EventHandler<MediaKind>? DecoderReady;

    public MediaEngineState State => _state;

    public MediaItem? CurrentItem { get; private set; }

    public bool IsAvailable => !_disposed;

    /// <summary>libmpv 版本字符串，用于启动自检显示。</summary>
    public string MpvVersion => Safe(() => _mpv.GetPropertyString("mpv-version"), string.Empty) ?? "unknown";

    /// <summary>
    /// 视频解码器是否已就绪。在轮询线程上探测：mpv 的 VideoReconfig 事件在嵌入模式下不保证触发，
    /// 而属性读取在轮询线程上最稳定。
    /// </summary>
    public bool IsVideoDecoderReady => _videoDecoderReady;

    /// <summary>视频解码器就绪那一刻的调试快照（硬解方式、编码、分辨率、丢帧）。</summary>
    public string? VideoDecoderSummary => _videoDecoderSummary;

    public double Volume
    {
        get => Safe(() => _mpv.Volume.Get(), (double?)null) ?? 100d;
        set => Safe(() => _mpv.Volume.Set(Math.Clamp(value, 0d, MaxVolume)));
    }

    /// <summary>mpv 实际生效的音量上限，用于启动自检（确认 volume-max 放宽成功）。</summary>
    public double VolumeMax => Safe(() => _mpv.VolumeMax.Get(), (double?)null) ?? 0d;

    public void Load(MediaItem item, bool autoPlay = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CurrentItem = item;
        _videoDecoderReady = false;
        _videoDecoderSummary = null;
        Safe(() => _mpv.LoadFile(item.Path).Invoke());
        if (autoPlay)
        {
            SetPaused(false);
        }

        Poll();
    }

    public void Play() => SetPaused(false);

    public void Enqueue(MediaItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // append=true, appendPlay=false：追加到 mpv 内部队列且不打断当前播放
        Safe(() => _mpv.LoadFile(item.Path, true, false, null).Invoke());
    }

    public void Pause() => SetPaused(true);

    public void TogglePause()
    {
        var paused = Safe(() => _mpv.Pause.Get(), (bool?)null) ?? true;
        SetPaused(!paused);
    }

    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var seconds = Math.Max(0d, position.TotalSeconds);
        Safe(() => _mpv.Seek(seconds, SeekOption.Absolute).Invoke());
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Safe(() => _mpv.Stop().Invoke());
        SetPaused(false);
        CurrentItem = null;
        Poll();
    }

    /// <summary>
    /// 一行式调试快照，用于 M0 阶段验证硬解是否生效、素材信息与丢帧情况。
    /// </summary>
    public string DebugSnapshot()
    {
        var hwdec = Safe(() => _mpv.GetPropertyString("hwdec-current"), string.Empty);
        var codec = Safe(() => _mpv.GetPropertyString("video-codec"), string.Empty);
        var format = Safe(() => _mpv.GetPropertyString("video-format"), string.Empty);
        var width = Safe(() => _mpv.GetProperty<int?>("width"), (int?)null) ?? 0;
        var height = Safe(() => _mpv.GetProperty<int?>("height"), (int?)null) ?? 0;
        var fps = Safe(() => _mpv.GetProperty<double?>("estimated-vf-fps"), (double?)null) ?? 0d;
        var drops = Safe(() => _mpv.GetProperty<int?>("frame-drop-count"), (int?)null) ?? 0;
        var decoderDrops = Safe(() => _mpv.GetProperty<int?>("decoder-frame-drop-count"), (int?)null) ?? 0;

        var sb = new StringBuilder();
        sb.Append("hwdec=").Append(string.IsNullOrEmpty(hwdec) ? "-" : hwdec);
        sb.Append(" · codec=").Append(string.IsNullOrEmpty(codec) ? "-" : codec);
        sb.Append(" · ").Append(width).Append('x').Append(height);
        if (!string.IsNullOrEmpty(format))
        {
            sb.Append(' ').Append(format);
        }

        sb.Append(" · fps=").Append(fps.ToString("F1"));
        sb.Append(" · drop=").Append(drops).Append('/').Append(decoderDrops);
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _pollTimer.Dispose();
        _mpv.AudioReconfig -= OnAudioReconfig;
        _mpv.VideoReconfig -= OnVideoReconfig;
        StateChanged = null;
        DecoderReady = null;
    }

    private void OnAudioReconfig(object? sender, EventArgs e) => DecoderReady?.Invoke(this, MediaKind.Audio);

    private void OnVideoReconfig(object? sender, EventArgs e) => DecoderReady?.Invoke(this, MediaKind.Video);

    private void SetPaused(bool paused) => Safe(() => _mpv.Pause.Set(paused));

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // 在轮询线程上探测视频解码器是否就绪，并抓取一次硬解快照
            if (!_videoDecoderReady
                && !string.IsNullOrEmpty(Safe(() => _mpv.GetPropertyString("video-codec"), string.Empty)))
            {
                _videoDecoderReady = true;
                _videoDecoderSummary = DebugSnapshot();
            }

            var paused = Safe(() => _mpv.Pause.Get(), (bool?)null) ?? true;
            var position = Safe(() => _mpv.PlaybackTime.Get(), (double?)null) ?? 0d;
            var duration = Safe(() => _mpv.Duration.Get(), (double?)null) ?? 0d;
            var title = Safe(() => _mpv.GetPropertyString("media-title"), string.Empty);

            var next = new MediaEngineState(
                paused,
                TimeSpan.FromSeconds(position),
                TimeSpan.FromSeconds(duration),
                string.IsNullOrEmpty(title) ? null : title);

            MediaEngineState previous;
            lock (_gate)
            {
                if (next.Equals(_state))
                {
                    return;
                }

                previous = _state;
                _state = next;
            }

            StateChanged?.Invoke(this, next);
        }
        catch
        {
            // 轮询失败（例如播放器正在销毁）不应影响播放，下一轮重试即可。
        }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // 控件命令失败在 M0 阶段静默忽略，后续接入日志系统。
        }
    }
}