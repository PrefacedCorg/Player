using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Avalonia;
using Player.Platform;
using Player.App.Rules;
using Player.Playback;

namespace Player.App.ViewModels;

/// <summary>
/// M0 主窗口 ViewModel：挂载引擎、暴露播放控制、显示启动时序与解码诊断。
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly PlayerSettings _settings;
    private readonly IReadOnlyList<string> _startupFiles;
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _surfaceReadyTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    /// <summary>规则集里的时间/星期条件与播放状态无关，靠这个定时器每分钟重新求值一次。</summary>
    private readonly DispatcherTimer _rulesTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSampleAt = DateTime.UtcNow;
    private MpvMediaEngine? _engine;
    private int _diagnosticsTicks;
    private int _surfaceWaitTicks;
    private bool _decoderReadyLogged;
    private bool _videoSnapshotLogged;
    private bool _playbackStartedLogged;
    private bool _startupPlayed;
    private bool _stalledLogged;

    // 时序测量：进程启动 → mpv 可交互 → 媒体加载 → 解码就绪 → 首帧
    private Stopwatch? _loadWatch;
    private long? _mpvReadyMs;
    private long? _loadCallMs;
    private long? _decodeReadyMs;
    private long? _firstFrameMs;
    private TimeSpan _seekTarget;
    private DateTime _seekGraceUntil = DateTime.MinValue;

    [ObservableProperty]
    private MpvContext? _mpv;

    /// <summary>
    /// 默认渲染器。选 Native（零拷贝、丢帧最少、内存最低）：
    /// 代价是视频是原生子窗口，同一窗口内的 Avalonia 控件无法叠加在视频之上，
    /// 因此界面采用"视频区 + 独立控制条"布局（见 MainWindow.axaml）。
    /// </summary>
    [ObservableProperty]
    private VideoRenderer _renderer = VideoRenderer.Native;

    [ObservableProperty]
    private string _statusText = "等待渲染视图交出 mpv 上下文…";

    [ObservableProperty]
    private string _timingText = "时序：等待 mpv 初始化…";

    [ObservableProperty]
    private string _debugText = string.Empty;

    [ObservableProperty]
    private string _timeText = "00:00 / 00:00";

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds = 1d;

    [ObservableProperty]
    private double _volume = 100d;

    [ObservableProperty]
    private string _playPauseText = "播放";

    [ObservableProperty]
    private string? _mediaTitle;

    /// <summary>控制条是否可见。视频区点击可切换（大屏手势：点画面呼出/收起控制层）。</summary>
    [ObservableProperty]
    private bool _isControlBarVisible = true;

    public MainWindowViewModel(PlayerSettings settings, IReadOnlyList<string>? startupFiles = null)
    {
        _settings = settings;
        _startupFiles = startupFiles ?? [];

        // 设置窗口与主窗口共用同一个 PlayerSettings 实例，改动在这里即时下发到引擎
        _settings.PropertyChanged += OnSettingsChanged;

        // 诊断走独立定时器，不依赖播放状态变化：暂停或位置不推进时也要能取到硬解信息
        _diagnosticsTimer.Tick += (_, _) => RunDiagnostics();
        _diagnosticsTimer.Start();

        _rulesTimer.Tick += (_, _) => PlayerRuleService.NotifyStatusChanged();
        _rulesTimer.Start();

        // 渲染表面就绪即开始加载，与界面初始化并行，避免串行等待拖慢首帧
        _surfaceReadyTimer.Tick += (_, _) => CheckSurfaceReady();
    }

    /// <summary>设置变化回调。类型全名限定：LibMpv 也有同名类型，直接 using 会歧义。</summary>
    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerSettings.ScalingMode))
        {
            ApplyScalingMode();
        }
    }

    /// <summary>把当前缩放方式下发给引擎，并把 mpv 的回读值写进日志，便于确认属性真的生效。</summary>
    private void ApplyScalingMode()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.ScalingMode = _settings.ScalingMode;
        StartupTrace.Mark($"画面缩放方式 → {_settings.ScalingMode} · {_engine.ScalingSnapshot()}");
    }

    /// <summary>
    /// 视频区被拖动（来自透明触摸层）：点对点模式下平移画面，其余模式忽略。
    /// 位移单位为像素，换算与限位在引擎侧完成（见 VideoPanMath）。
    /// </summary>
    public void PanVideo(double deltaX, double deltaY) => _engine?.PanVideo(deltaX, deltaY);

    /// <summary>
    /// 缩放画面（捏合或滚轮）：factor 为倍率增量，仅"自由缩放"模式生效。
    /// 捏合过程中会连续调用，因此不在这里打日志——倍率会出现在拖动结束与心跳的几何快照里。
    /// </summary>
    public void ZoomVideo(double factor) => _engine?.ZoomVideo(factor);

    /// <summary>
    /// 一次拖动结束：记录本次累计位移与 mpv 回读的平移量。
    /// 两个数字放在一起才能判断"手指位移"与"画面位移"是否一致（手感调校的依据）。
    /// </summary>
    public void EndVideoDrag(double totalX, double totalY)
    {
        if (_engine is null)
        {
            return;
        }

        StartupTrace.Mark($"画面拖动结束：手指位移 {totalX:F0},{totalY:F0} px · {_engine.ScalingSnapshot()}");
    }

    public VideoRenderer[] RendererOptions { get; } = Enum.GetValues<VideoRenderer>();

    /// <summary>应用级实时设置。控制栏宿主按其中的控件列表渲染，因此这里要暴露给视图。</summary>
    public PlayerSettings Settings => _settings;

    /// <summary>进度条是否正在被拖动。拖动期间忽略引擎回传的位置，松手才真正 seek。</summary>
    public bool IsScrubbing { get; private set; }

    partial void OnMpvChanged(MpvContext? value) => AttachEngine(value);

    partial void OnRendererChanged(VideoRenderer value) =>
        StartupTrace.Mark($"渲染器切换为 {value}");

    [RelayCommand]
    private void TogglePause() => _engine?.TogglePause();

    [RelayCommand]
    private void Stop()
    {
        _engine?.Stop();
        MediaTitle = null;
        SyncRuleContext(false, true, null);
    }

    /// <summary>
    /// 播放启动参数带进来的文件。调用时机由渲染表面就绪决定（见 <see cref="CheckSurfaceReady"/>）：
    /// OpenGl 渲染路径下，mpv 渲染上下文建立前 LoadFile 会让播放中断（退回 idle）。
    /// </summary>
    public void PlayStartupFiles()
    {
        if (_startupPlayed || _startupFiles.Count == 0 || _engine is null)
        {
            return;
        }

        _startupPlayed = true;
        StartupTrace.Mark($"渲染表面就绪，开始加载启动文件：{_startupFiles.Count} 个");
        OpenFiles(_startupFiles);
    }

    /// <summary>打开一组文件：第一个立即播放，其余追加到播放队列（拖入多个文件时连续播放）。</summary>
    public void OpenFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        OpenFile(paths[0]);

        if (paths.Count == 1 || _engine is null)
        {
            return;
        }

        var queued = 0;
        for (var i = 1; i < paths.Count; i++)
        {
            // 与 OpenFile 一致：扩展名未知也交给 mpv 尝试
            _engine.Enqueue(MediaItem.FromPath(paths[i]));
            queued++;
        }

        if (queued > 0)
        {
            StatusText = $"{StatusText} · 已入队 {queued} 个";
        }
    }

    /// <summary>由视图层在拿到文件路径后调用（文件选择器属于平台层，不进 ViewModel）。</summary>
    public void OpenFile(string path)
    {
        if (_engine is null)
        {
            StatusText = "mpv 尚未就绪，无法播放";
            return;
        }

        var item = MediaItem.FromPath(path);

        // 不因扩展名未知就拒绝播放：容器/编码判断交给 mpv，失败时由"播放未启动"告警兜底。
        // （白名单只用于媒体库扫描，不能挡用户直接拖进来的文件。）
        BeginLoadTiming();

        _engine.Load(item);
        _loadCallMs = _loadWatch!.ElapsedMilliseconds;

        StatusText = $"{item.Kind} · {item.Title}";
        MediaTitle = item.Title;
        SyncRuleContext(PlayerRuleContext.IsPlaying, PlayerRuleContext.IsPaused, item.Title);
        StartupTrace.Mark($"LoadFile 返回：{_loadCallMs} ms · {item.Title}");
        UpdateTimingText();
    }

    /// <summary>音量变化即时下发到引擎（拖动过程就有听觉反馈）。</summary>
    partial void OnVolumeChanged(double value)
    {
        if (_engine is not null)
        {
            _engine.Volume = value;
        }
    }

    /// <summary>拖动期间时间显示跟随手指，而不是跟着播放位置。</summary>
    partial void OnPositionSecondsChanged(double value)
    {
        if (IsScrubbing)
        {
            TimeText = $"{Format(TimeSpan.FromSeconds(value))} / {Format(TimeSpan.FromSeconds(DurationSeconds))}";
        }
    }

    /// <summary>视频区被触摸（来自透明触摸层）：切换控制层显隐。M2 会在这里接入完整手势路由。</summary>
    public void OnVideoTapped(double x, double y)
    {
        var action = IsControlBarVisible ? "收起" : "呼出";
        StartupTrace.Mark($"画面触摸 ({x:F0},{y:F0}) → {action}控制层");
        IsControlBarVisible = !IsControlBarVisible;
    }

    public void BeginScrub()
    {
        if (IsScrubbing)
        {
            return;
        }

        IsScrubbing = true;
        StartupTrace.Mark("进度条：开始拖动");
    }

    public void EndScrub()
    {
        if (!IsScrubbing)
        {
            return;
        }

        IsScrubbing = false;
        var target = TimeSpan.FromSeconds(PositionSeconds);
        _seekTarget = target;
        _seekGraceUntil = DateTime.UtcNow.AddMilliseconds(800);
        _engine?.Seek(target);
        StartupTrace.Mark($"进度拖动完成 → seek {target.TotalSeconds:F1}s");
    }

    /// <summary>
    /// 是否接受引擎回传的位置。
    /// <para>
    /// 拖动期间不接受（否则回弹到实时播放位置）；seek 后的宽限期内只接受已接近目标的位置，
    /// 因为 mpv 生效有延迟，紧接着的轮询仍会报旧位置——那正是"拖过去又弹回来"的根源。
    /// </para>
    /// </summary>
    private bool ShouldAcceptEnginePosition(TimeSpan reported)
    {
        if (IsScrubbing)
        {
            return false;
        }

        if (DateTime.UtcNow >= _seekGraceUntil)
        {
            return true;
        }

        return Math.Abs((reported - _seekTarget).TotalSeconds) < 2d;
    }

    private void AttachEngine(MpvContext? context)
    {
        _engine?.Dispose();
        _engine = null;

        if (context is null)
        {
            StatusText = "渲染视图已释放 mpv 上下文";
            return;
        }

        try
        {
            var engine = new MpvMediaEngine(context);
            engine.StateChanged += OnEngineStateChanged;
            engine.DecoderReady += OnDecoderReady;
            engine.Volume = Volume;
            engine.ScalingMode = _settings.ScalingMode;
            _engine = engine;

            _mpvReadyMs = (long)StartupTrace.ElapsedMs;
            StatusText = $"mpv {engine.MpvVersion} 已就绪 · 渲染器 {Renderer}";
            StartupTrace.Mark($"mpv 冷启动完成（进程启动→引擎可交互）：{_mpvReadyMs} ms · 音量上限 {engine.VolumeMax:F0}%");
            UpdateTimingText();

            // 开始等待渲染表面就绪（30ms 粒度；最多等 3 秒后兜底强行加载）
            _surfaceWaitTicks = 0;
            _surfaceReadyTimer.Start();
        }
        catch (Exception ex)
        {
            StatusText = "mpv 引擎挂载失败：" + ex.Message;
            StartupTrace.Mark("mpv 引擎挂载失败：" + ex);
        }
    }

    /// <summary>重置一次加载过程的所有计时与一次性标志。</summary>
    private void BeginLoadTiming()
    {
        _decoderReadyLogged = false;
        _videoSnapshotLogged = false;
        _playbackStartedLogged = false;
        _stalledLogged = false;
        _loadCallMs = null;
        _decodeReadyMs = null;
        _firstFrameMs = null;
        _loadWatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// 等待渲染表面就绪后立即起播。表面就绪 = mpv 渲染上下文可用（OpenGl 的 GL 上下文已建立，
    /// 或 Native 的原生子窗口已创建），此时 LoadFile 才不会中断播放。
    /// </summary>
    private void CheckSurfaceReady()
    {
        if (_startupPlayed)
        {
            _surfaceReadyTimer.Stop();
            return;
        }

        _surfaceWaitTicks++;
        // OpenGl 路径必须等 mpv 渲染上下文建立；Native 走原生子窗口，无需等待（探测不到反而会白等超时）
        var ready = Renderer != VideoRenderer.OpenGl || Mpv?.IsCustomRendering() == true;

        // 30ms × 100 = 3 秒兜底：即使探测不到也强行加载，避免永久卡在等待
        if (!ready && _surfaceWaitTicks < 100)
        {
            return;
        }

        _surfaceReadyTimer.Stop();
        StartupTrace.Mark(ready
            ? $"渲染表面就绪（等待 {_surfaceWaitTicks * 30} ms），开始加载媒体"
            : "警告：等待渲染表面超时（3s），仍尝试加载媒体");
        PlayStartupFiles();
    }

    private void OnEngineStateChanged(object? sender, MediaEngineState state)
    {
        // 轮询线程回调 → 切回 UI 线程
        Dispatcher.UIThread.Post(() => ApplyState(state), DispatcherPriority.Background);
    }

    /// <summary>mpv 事件线程回调：解码器 reconfigure 说明 demux + 解码链路真的通了。</summary>
    private void OnDecoderReady(object? sender, MediaKind kind)
    {
        if (_decoderReadyLogged)
        {
            return;
        }

        _decoderReadyLogged = true;
        Dispatcher.UIThread.Post(() =>
        {
            _decodeReadyMs ??= _loadWatch?.ElapsedMilliseconds;
            StartupTrace.Mark($"解码链路就绪：{kind} · 媒体加载耗时 {_decodeReadyMs} ms（LoadFile→解码器建立）");
            UpdateTimingText();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// 把播放状态同步到规则上下文；有变化才通知规则刷新（引擎心跳很频繁，避免每次都刷新）。
    /// </summary>
    private static void SyncRuleContext(bool isPlaying, bool isPaused, string? title)
    {
        var changed = PlayerRuleContext.IsPlaying != isPlaying
                      || PlayerRuleContext.IsPaused != isPaused
                      || !string.Equals(PlayerRuleContext.MediaTitle, title, StringComparison.Ordinal);
        if (!changed)
        {
            return;
        }

        PlayerRuleContext.IsPlaying = isPlaying;
        PlayerRuleContext.IsPaused = isPaused;
        PlayerRuleContext.MediaTitle = title;
        PlayerRuleService.NotifyStatusChanged();
    }

    private void ApplyState(MediaEngineState state)
    {
        PlayPauseText = state.IsPaused ? "播放" : "暂停";

        var acceptPosition = ShouldAcceptEnginePosition(state.Position);
        if (acceptPosition)
        {
            TimeText = $"{Format(state.Position)} / {Format(state.Duration)}";
        }

        // 位置开始推进 = 播放真正启动（M0 核心验收项）。判据用 Position>0 而非更大的阈值，
        // 否则会把"等位置推进到阈值"的时间也算进起播耗时。
        if (!_playbackStartedLogged && state.Position > TimeSpan.Zero)
        {
            _playbackStartedLogged = true;
            _firstFrameMs ??= _loadWatch?.ElapsedMilliseconds;
            StartupTrace.Mark($"起播完成：{_firstFrameMs} ms（LoadFile→播放开始推进）· "
                + $"{state.Position.TotalSeconds:F2}s / {state.Duration.TotalSeconds:F2}s");

            // 起播后几何已确定（dwidth/dheight、视频区尺寸可读），落一条缩放自检作为取证
            StartupTrace.Mark($"画面几何自检（{_engine?.ScalingMode}）：{_engine?.ScalingSnapshot()}");
            UpdateTimingText();
        }

        // 视频解码器就绪后记录一次硬解快照
        if (acceptPosition && state.Duration > TimeSpan.Zero)
        {
            DurationSeconds = Math.Max(1d, state.Duration.TotalSeconds);
            PositionSeconds = state.Position.TotalSeconds;
        }

        if (state.Title is not null)
        {
            MediaTitle = state.Title;
        }

        SyncRuleContext(!state.IsPaused, state.IsPaused, MediaTitle);
    }

    /// <summary>500ms 诊断心跳：刷新硬解信息，并在拿到 video-codec 后落一次快照。</summary>
    private void RunDiagnostics()
    {
        if (_engine is null)
        {
            return;
        }

        // 每个 tick 只采一次 CPU，否则第二次采样的窗口会小到接近 0
        var cpuPercent = SampleCpuPercent();
        DebugText = $"{_engine.DebugSnapshot()} · cpu={cpuPercent:F1}%";
        _diagnosticsTicks++;

        // 加载后前 30 秒每 5 秒落一条心跳，便于排查"位置不推进"这类问题
        if (_loadWatch is { } heartbeatWatch
            && heartbeatWatch.ElapsedMilliseconds < 30_000
            && _diagnosticsTicks % 10 == 0)
        {
            var state = _engine.State;
            StartupTrace.Mark(
                $"心跳：pos={state.Position.TotalSeconds:F2}s / {state.Duration.TotalSeconds:F2}s"
                + $" · cpu={cpuPercent:F1}% · {_engine.DebugSnapshot()}{_engine.PanSummary()}");
        }

        var watch = _loadWatch;

        // 加载 3 秒后播放仍没起来（duration 仍为 0）= 明显异常，必须显式报警而不是静默
        if (watch is not null
            && !_playbackStartedLogged
            && !_stalledLogged
            && watch.ElapsedMilliseconds > 3000
            && _engine.State.Duration <= TimeSpan.Zero)
        {
            _stalledLogged = true;
            StatusText = $"播放未启动：{MediaTitle} 未能播放（详见日志）";
            StartupTrace.Mark(
                $"警告：{MediaTitle} 加载后 {watch.ElapsedMilliseconds} ms 播放仍未开始（duration=0）"
                + "，可能是容器/编码不被支持，或渲染视图未就绪");
        }

        if (_videoSnapshotLogged || watch is null)
        {
            return;
        }

        if (_engine.IsVideoDecoderReady)
        {
            _videoSnapshotLogged = true;
            _decodeReadyMs ??= watch.ElapsedMilliseconds;
            StartupTrace.Mark($"视频解码就绪：{_decodeReadyMs} ms · {_engine.VideoDecoderSummary}");
            UpdateTimingText();
        }
        else if (watch.ElapsedMilliseconds > 3000)
        {
            // 兜底：OpenGl 渲染路径下 video-codec 可能长时间不可读，诊断信息不能缺
            _videoSnapshotLogged = true;
            StartupTrace.Mark(
                $"解码快照（加载后 {watch.ElapsedMilliseconds} ms，位置 {_engine.State.Position.TotalSeconds:F1}s）"
                + $" · {_engine.DebugSnapshot()}");
        }
    }

    /// <summary>
    /// 采样本进程 CPU 占用（%）。基于两次采样之间的处理器时间差，除以墙钟时间与核心数，
    /// 只统计 CPU 时间，不含 GPU 侧开销。
    /// </summary>
    private double SampleCpuPercent()
    {
        var now = DateTime.UtcNow;
        var wallSeconds = (now - _lastCpuSampleAt).TotalSeconds;
        if (wallSeconds <= 0)
        {
            return 0d;
        }

        _currentProcess.Refresh();
        var cpu = _currentProcess.TotalProcessorTime;
        var usedSeconds = (cpu - _lastCpuTime).TotalSeconds;
        _lastCpuTime = cpu;
        _lastCpuSampleAt = now;

        var percent = usedSeconds / (wallSeconds * Environment.ProcessorCount) * 100d;
        return Math.Max(0d, percent);
    }

    private void UpdateTimingText()
    {
        if (_mpvReadyMs is null)
        {
            TimingText = "时序：等待 mpv 初始化…";
            return;
        }

        var sb = new StringBuilder();
        sb.Append("mpv 就绪 ").Append(_mpvReadyMs).Append(" ms");
        if (_loadCallMs is not null)
        {
            sb.Append(" · 加载 ").Append(_loadCallMs).Append(" ms");
        }

        if (_decodeReadyMs is not null)
        {
            sb.Append(" · 解码 ").Append(_decodeReadyMs).Append(" ms");
        }

        if (_firstFrameMs is not null)
        {
            sb.Append(" · 首帧 ").Append(_firstFrameMs).Append(" ms");
        }

        TimingText = sb.ToString();
    }

    private static string Format(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}"
            : $"{value.Minutes:D2}:{value.Seconds:D2}";
}