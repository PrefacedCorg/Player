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

    /// <summary>当前缩放方式（"原始大小"模式才允许平移）。</summary>
    private VideoScalingMode _scalingMode = VideoScalingMode.Fit;

    /// <summary>当前画面平移量（mpv video-pan-x/y 的原始值，单位为显示画面尺寸的比例）。</summary>
    private double _panX;
    private double _panY;

    /// <summary>自由缩放模式下的倍率（1.0 = 铺满视频区）。</summary>
    private double _scale = 1d;

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

    /// <summary>
    /// 画面缩放方式。改为立即下发：keepaspect / video-unscaled / panscan 都是 mpv 的运行期属性，
    /// 播放中切换无需重新加载文件。
    /// </summary>
    public VideoScalingMode ScalingMode
    {
        get => _scalingMode;
        set
        {
            if (_scalingMode == value)
            {
                return;
            }

            _scalingMode = value;
            ApplyScalingMode(value);
        }
    }

    /// <summary>
    /// 触屏拖动平移画面。像素位移按显示尺寸归一化后累加，并按可平移上限夹取（见 <see cref="VideoPanMath"/>）。
    /// 仅"原始大小"与"自由缩放"两种模式生效——其余模式下画面随窗口缩放，没有可平移的余量。
    /// </summary>
    public void PanVideo(double deltaX, double deltaY)
    {
        if (_disposed || _scalingMode is not (VideoScalingMode.Original or VideoScalingMode.Free))
        {
            return;
        }

        var geometry = ReadDisplayGeometry();
        if (geometry is null)
        {
            return;
        }

        var (displayedWidth, displayedHeight, windowWidth, windowHeight) = geometry.Value;
        _panX = VideoPanMath.ClampPan(
            _panX, VideoPanMath.ToPanDelta(deltaX, displayedWidth), displayedWidth, windowWidth);
        _panY = VideoPanMath.ClampPan(
            _panY, VideoPanMath.ToPanDelta(deltaY, displayedHeight), displayedHeight, windowHeight);
        ApplyPan();
    }

    /// <summary>
    /// 捏合/滚轮缩放。倍率夹取到允许范围（1× 为铺满视频区），缩放后把既有平移量拉回新范围——
    /// 放大后两边余量变大可以继续拖，缩小时原来拖出去的部分必须回到可见区内。
    /// </summary>
    public void ZoomVideo(double factor)
    {
        if (_disposed || _scalingMode != VideoScalingMode.Free || !double.IsFinite(factor) || factor <= 0d)
        {
            return;
        }

        var next = VideoZoomMath.ClampScale(_scale * factor);
        if (Math.Abs(next - _scale) < 0.0001d)
        {
            return;
        }

        _scale = next;
        ApplyZoom();
        ClampPanToView();
    }

    /// <summary>自由缩放模式下的当前倍率（1.0 = 铺满视频区）。</summary>
    public double Scale => _scale;

    public void ResetVideoPan()
    {
        _panX = 0d;
        _panY = 0d;
        ApplyPan();
    }

    /// <summary>
    /// 缩放相关属性的回读快照，用于启动自检与日志取证：确认属性真的生效，而不是只发出了调用。
    /// 其中 dwidth/dheight 是显示中的画面尺寸，osd-dimensions 是视频区尺寸——两者决定可平移范围。
    /// </summary>
    public string ScalingSnapshot()
    {
        var geometry = ReadDisplayGeometry();

        var sb = new StringBuilder();
        // keepaspect / video-unscaled 是"是/否"型属性：按字符串读回才是 mpv 侧的真实生效值
        sb.Append("keepaspect=").Append(Describe(Safe(() => _mpv.GetPropertyString("keepaspect"), string.Empty)));
        sb.Append(" · unscaled=").Append(Describe(Safe(() => _mpv.GetPropertyString("video-unscaled"), string.Empty)));
        sb.Append(" · panscan=").Append(ReadDouble("panscan").ToString("F2"));
        // video-zoom 用 log2 记倍率：这里打印 mpv 回读值，并换算成倍率方便直接核对
        var zoom = ReadDouble("video-zoom");
        sb.Append(" · zoom=").Append(zoom.ToString("F2"))
            .Append("（").Append(VideoZoomMath.FromVideoZoom(zoom).ToString("F2")).Append("×）");
        sb.Append(" · 画面=").Append(geometry is null
            ? "-"
            : $"{geometry.Value.DisplayedWidth:F0}x{geometry.Value.DisplayedHeight:F0}");
        sb.Append(" · 视频区=").Append(geometry is null
            ? "-"
            : $"{geometry.Value.WindowWidth:F0}x{geometry.Value.WindowHeight:F0}");
        // pan 从 mpv 回读（而不是打印本地字段）：证明下发值被 mpv 接受
        sb.Append(" · pan=").Append(ReadDouble("video-pan-x").ToString("F3"))
            .Append(',').Append(ReadDouble("video-pan-y").ToString("F3"));
        return sb.ToString();
    }

    /// <summary>平移量日志片段：无位移时为空串。用于确认触屏拖动真的把位移下发生效了。</summary>
    public string PanSummary()
    {
        var panX = ReadDouble("video-pan-x");
        var panY = ReadDouble("video-pan-y");

        return Math.Abs(panX) < 0.0005d && Math.Abs(panY) < 0.0005d
            ? string.Empty
            : $" · pan={panX:F3},{panY:F3}（{_scalingMode}）";
    }

    private static string Describe(string? value) => string.IsNullOrEmpty(value) ? "-" : value;

    public void Load(MediaItem item, bool autoPlay = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CurrentItem = item;
        _videoDecoderReady = false;
        _videoDecoderSummary = null;
        // 换片后显示尺寸变了，旧的平移量对新画面没有意义
        ResetVideoPan();
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

    /// <summary>把缩放方式翻译成 mpv 属性组合。四种方式互斥，且都只动这三个运行期属性。</summary>
    private void ApplyScalingMode(VideoScalingMode mode)
    {
        // 换方式后旧平移量没有意义（例如从点对点切回等比填充）
        ResetVideoPan();

        switch (mode)
        {
            case VideoScalingMode.Fit:
                SetGeometry(keepAspect: true, unscaled: false, panscan: 0d, scale: 1d);
                break;
            case VideoScalingMode.Stretch:
                SetGeometry(keepAspect: false, unscaled: false, panscan: 0d, scale: 1d);
                break;
            case VideoScalingMode.Original:
                SetGeometry(keepAspect: true, unscaled: true, panscan: 0d, scale: 1d);
                break;
            case VideoScalingMode.Crop:
                SetGeometry(keepAspect: true, unscaled: false, panscan: 1d, scale: 1d);
                break;
            case VideoScalingMode.Free:
                // 进入自由缩放一律从 1×（铺满视频区）起算，避免带着上一次的倍率
                _scale = 1d;
                SetGeometry(keepAspect: true, unscaled: false, panscan: 0d, scale: _scale);
                break;
        }
    }

    private void SetGeometry(bool keepAspect, bool unscaled, double panscan, double scale)
    {
        Safe(() => _mpv.SetPropertyFlag("keepaspect", keepAspect));
        Safe(() => _mpv.SetPropertyString("video-unscaled", unscaled ? "yes" : "no"));
        Safe(() => _mpv.SetPropertyDouble("panscan", panscan));
        // video-zoom 是全局属性：非自由缩放模式必须显式归零，否则倍率会被带到别的模式里
        Safe(() => _mpv.SetPropertyDouble("video-zoom", VideoZoomMath.ToVideoZoom(scale)));
    }

    private void ApplyZoom() =>
        Safe(() => _mpv.SetPropertyDouble("video-zoom", VideoZoomMath.ToVideoZoom(_scale)));

    /// <summary>把平移量夹到当前显示尺寸允许的范围内（缩放后调用）。</summary>
    private void ClampPanToView()
    {
        var geometry = ReadDisplayGeometry();
        if (geometry is null)
        {
            return;
        }

        var (displayedWidth, displayedHeight, windowWidth, windowHeight) = geometry.Value;
        _panX = Math.Clamp(_panX, -VideoPanMath.MaxPan(displayedWidth, windowWidth), VideoPanMath.MaxPan(displayedWidth, windowWidth));
        _panY = Math.Clamp(_panY, -VideoPanMath.MaxPan(displayedHeight, windowHeight), VideoPanMath.MaxPan(displayedHeight, windowHeight));
        ApplyPan();
    }

    private void ApplyPan()
    {
        Safe(() => _mpv.SetPropertyDouble("video-pan-x", _panX));
        Safe(() => _mpv.SetPropertyDouble("video-pan-y", _panY));
    }

    /// <summary>
    /// 读显示几何：返回"当前实际显示的画面尺寸"与视频区尺寸。
    /// <para>
    /// 注意 dwidth/dheight 是"未经窗口适配与缩放"的画面尺寸（实测：4K 素材在 1280x717 视频区、
    /// panscan=1 时仍报 3840x2160），因此：
    /// 点对点模式显示尺寸就是它本身（1:1）；自由缩放模式要按视频区求适配比例再乘倍率。
    /// </para>
    /// 任一读不到（未起播、纯音频）返回 null。
    /// </summary>
    private (double DisplayedWidth, double DisplayedHeight, double WindowWidth, double WindowHeight)? ReadDisplayGeometry()
    {
        var sourceWidth = ReadInt("dwidth");
        var sourceHeight = ReadInt("dheight");
        var windowWidth = ReadInt("osd-dimensions/w");
        var windowHeight = ReadInt("osd-dimensions/h");

        if (sourceWidth <= 0 || sourceHeight <= 0 || windowWidth <= 0 || windowHeight <= 0)
        {
            return null;
        }

        if (_scalingMode != VideoScalingMode.Free)
        {
            return (sourceWidth, sourceHeight, windowWidth, windowHeight);
        }

        var (width, height) = VideoZoomMath.ScaledSize(sourceWidth, sourceHeight, windowWidth, windowHeight, _scale);
        return (width, height, windowWidth, windowHeight);
    }

    private int ReadInt(string name) => Safe(() => _mpv.GetProperty<int?>(name), (int?)null) ?? 0;

    private double ReadDouble(string name) => Safe(() => _mpv.GetProperty<double?>(name), (double?)null) ?? 0d;

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