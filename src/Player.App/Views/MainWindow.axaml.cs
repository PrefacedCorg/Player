using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HanumanInstitute.LibMpv.Avalonia;
using Player.App.Assists;
using Player.App.ViewModels;
using Player.App.Views.PlayerControls;
using Player.Platform;
using Player.Playback;

namespace Player.App.Views;

public partial class MainWindow : Window
{
    /// <summary>手势「快退 / 快进」一次跳转的秒数（单击 / 双击手势用）。</summary>
    private const int GestureSeekSeconds = 10;

    /// <summary>手势「音量 ±」一次调整的百分比（单击 / 双击手势用）。</summary>
    private const double GestureVolumeStep = 5d;

    /// <summary>音量上限：与引擎侧一致（mpv volume-max 已放宽到 200）。</summary>
    private const double GestureVolumeMax = 200d;

    /// <summary>音量 / 亮度滑满视频区对应方向的整个尺寸（水平宽 / 垂直高）对应的量程：100 个单位。</summary>
    private const double DragFullRange = 100d;

    /// <summary>滑动调进度的固定比例：沿滑动方向 4 像素 = 1 秒，与视频区大小、视频时长都无关。</summary>
    private const double DragSeekSecondsPerPixel = 0.25d;

    /// <summary>滑动过程中下发的步进：与上一次下发的值相差不到它就先攒着。</summary>
    private const double DragValueStep = 0.5d;

    /// <summary>触屏优先的保护期：触摸操作后这段时间内的鼠标事件一律忽略（系统会伴随触摸合成鼠标事件）。</summary>
    private static readonly TimeSpan TouchMouseGuard = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// 鼠标在视频区上的最小计步位移（DIP）：位移不到它不算「鼠标移动一下」。
    /// 手搭在鼠标上的轻微抖动、系统在窗口属性被反复触碰时补发的同位置移动都落在阈值内——
    /// 若拿这些事件去刷新倒计时，鼠标停在视频区上时控制栏就永远收不起来。
    /// </summary>
    private const double MouseMoveThreshold = 8d;

    private readonly MainWindowViewModel _viewModel;

    // ── 画面滑动调节的会话状态（拖动开始到抬手之间有效）──

    /// <summary>本次滑动调节什么；None 表示这次拖动不平移也不调节（例如平移模式下由引擎平移画面）。</summary>
    private VideoSwipeAdjust _dragAdjust = VideoSwipeAdjust.None;

    /// <summary>本次拖动的累计位移（DIP）。</summary>
    private Point _dragTotal;

    /// <summary>本次拖动锁定的方向：位移一律按这个轴折算（与配的是什么调节无关）。</summary>
    private VideoDragAxis _dragAxis;

    /// <summary>调节的基准值（拖动开始时的进度 / 音量 / 亮度），全程以它为基准加位移比例。</summary>
    private double _dragBaseValue;

    /// <summary>最近一次下发的值（节流比较用；NaN 表示还没下发过）。</summary>
    private double _dragAppliedValue = double.NaN;

    /// <summary>应用级实时设置：主窗口与设置窗口共用同一个实例（见 PlayerSettings）。</summary>
    private readonly PlayerSettings _settings;

    /// <summary>渲染器切换允许生效的时机：窗口显示之后。原生渲染视图（NativeView）未上树就创建会阻塞启动。</summary>
    private bool _rendererSwitchReady;

    /// <summary>
    /// 触摸层贴合视频区的兜底定时器。主路径是事件驱动（主窗口 PositionChanged / 视频区 Bounds 变化，
    /// 见构造函数），它只负责收尾个别拿不到事件的场景；值没变化时 SyncOverlayBounds 会直接跳过。
    /// </summary>
    private readonly DispatcherTimer _overlaySyncTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>捏合 / 滚轮缩放结束后隐藏中央「缩放 x%」提示：最后一次缩放后 900ms 内没有新事件才隐藏。</summary>
    private readonly DispatcherTimer _zoomHintTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };

    /// <summary>悬浮控制栏的「无操作自动隐藏」倒计时（秒数为 0 时不启动，见 RestartControlBarCountdown）。</summary>
    private readonly DispatcherTimer _controlBarHideTimer = new();

    /// <summary>最近一次触摸 / 笔操作的时间（触屏优先保护期用；最小时间 = 还没发生过）。</summary>
    private DateTime _lastTouchAt = DateTime.MinValue;

    /// <summary>鼠标是否停在悬浮控制栏上（鼠标支持开启时暂停倒计时，见 RestartControlBarCountdown）。</summary>
    private bool _mouseOverControlBar;

    /// <summary>上一次被认作「鼠标移动」的位置（视频区坐标；位移不足阈值的事件不刷新它）。</summary>
    private Point? _lastMouseMoveAt;

    /// <summary>悬浮控制栏是否已注入触摸层（触摸层重建后标志复位、重新注入）。</summary>
    private bool _overlayControlBarAttached;

    private VideoTouchOverlay? _touchOverlay;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();

        // 触摸模式：触摸屏点一下才显示拖动用的手柄（见 PointerStateAssist / TouchDragThumb）
        PointerStateAssist.Attach(this);

        // 应用级实时设置（App 上创建，主窗口与设置窗口共用同一份，改动即时生效）
        _settings = App.Settings;

        _viewModel = new MainWindowViewModel(_settings, Program.StartupFiles)
        {
            Renderer = Program.StartupRenderer ?? VideoRenderer.Native,
        };

        // 必须在设置 DataContext（即首次建立 mpv 上下文）之前定好渲染器：
        // 否则会先创建一个渲染上下文、加载一次文件，切换后上下文重建、文件丢失。
        VideoView.Renderer = _viewModel.Renderer;

        // 设置 DataContext 后，MpvView 通过 OneWayToSource 绑定把 mpv 上下文交给 ViewModel。
        DataContext = _viewModel;

        _overlaySyncTimer.Tick += (_, _) => SyncOverlayBounds();

        // 触摸层 / 悬浮栏是独立顶层窗口，不会随主窗口自动移动，必须自己贴上去：
        // 主窗口一移动就立即同步（WM_MOVE 事件驱动，与拖动在同一条消息里完成——这是「跟手」的关键，
        // Popup 也是这么跟的）；视频区尺寸变化（窗口缩放、控制栏显隐）同样即时同步。
        // 定时器只是兜底：个别拿不到位置事件的场景（DPI 切换等）靠它收尾。
        PositionChanged += (_, _) => SyncOverlayBounds();
        VideoView.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                SyncOverlayBounds();
            }
        };

        // 缩放提示的自动隐藏：正在滑动调节时不动它（那是滑动自己的提示，抬手时统一隐藏）
        _zoomHintTimer.Tick += (_, _) =>
        {
            _zoomHintTimer.Stop();
            if (_dragAdjust == VideoSwipeAdjust.None)
            {
                _touchOverlay?.HideCenterHint();
            }
        };

        // 悬浮控制栏：倒计时到点收起（能不能计时、要计多少秒统一在 RestartControlBarCountdown 里判断）
        _controlBarHideTimer.Tick += (_, _) =>
        {
            _controlBarHideTimer.Stop();
            if (_viewModel.IsOverlayControlBarActive && _viewModel.IsControlBarVisible)
            {
                _viewModel.IsControlBarVisible = false;
                // 落一条打点：控制栏没按时收起时，先看有没有这条，能分清是倒计时没跑还是被活动刷新了
                StartupTrace.Mark($"悬浮控制栏：无操作 {_settings.ControlBarBehavior.AutoHideSeconds:0.#} 秒，自动收起");
            }
        };

        // 悬浮控制栏行为设置变化（窗口 / 全屏显示方式、自动隐藏秒数、鼠标支持）：即时生效
        _settings.ControlBarBehavior.PropertyChanged += OnControlBarBehaviorChanged;

        // 控制栏已组件化：渲染器控件只改 ViewModel.Renderer，真正切换渲染视图（重建 mpv 上下文、
        // 同步触摸层）在这里统一处理。事件参数类型全名限定是因为 LibMpv 也有同名类型。
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Renderer))
            {
                ApplyRenderer(_viewModel.Renderer);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.Brightness))
            {
                // 亮度被调过（≠ 0）就在右下角显示「恢复亮度」，复位后隐藏
                _touchOverlay?.SetBrightnessHintVisible(_viewModel.Brightness != 0d);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsOverlayControlBarVisible))
            {
                // 悬浮控制栏在触摸层窗口里，显隐要单独同步过去（停靠栏由 XAML 绑定）
                _touchOverlay?.SetControlBarVisible(_viewModel.IsOverlayControlBarVisible);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsOverlayControlBarActive))
            {
                // 显示形态切换（窗口 / 全屏的开关改了，或进出全屏）：先呼出控制栏，
                // 避免落在“两种形态都看不到”的状态，再按新形态重挂悬浮栏并重开倒计时
                _viewModel.IsControlBarVisible = true;
                ApplyControlBarOverlay();
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsControlBarVisible))
            {
                // 控制栏显隐变化：悬浮形态下重新开始 / 停止自动隐藏倒计时
                RestartControlBarCountdown();
            }
        };

        // 全屏状态接线：WindowState ↔ ViewModel.IsFullscreen（控制栏「全屏」控件的按钮文本跟随它，
        // 用户按 F11 或系统快捷键切全屏时按钮同样跟着变）。
        // 窗口状态还可能改变显示形态与窗口圆角：最大化 / 全屏是直角，切回普通窗口又变圆角。
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                _viewModel.IsFullscreen = WindowState == WindowState.FullScreen;
                ApplyControlBarOverlay();
            }
        };

        Opened += (_, _) =>
        {
            _rendererSwitchReady = true;
            EnsureTouchOverlay();
            // 当前形态是悬浮的话（例如全屏默认悬浮），控制栏初始即「显示」，这里把自动隐藏倒计时一并接上
            RestartControlBarCountdown();

            // --settings：启动后直接进设置页（调试验证 / 大屏快捷入口）。放到 Dispatcher 队列里，
            // 等主窗口显示完成再开设置窗口，避免两个窗口同时首帧。
            if (Program.OpenSettingsOnStartup)
            {
                Dispatcher.UIThread.Post(OpenSettings);
            }
        };
        Closed += (_, _) => DestroyTouchOverlay();
    }

    /// <summary>
    /// 打开设置窗口（非模态）。设置窗口是独立顶层窗口，
    /// 期间先撤掉视频区透明触摸层——它同样是顶层窗口，会抢走设置界面的点击命中。
    /// 用 Show 而不是 ShowDialog：设置窗口开着时主窗口仍可交互（播放/暂停、看控制栏实时变化、
    /// 在主窗口按 F6 验证触摸模式），关闭设置窗口后再恢复触摸层。
    /// </summary>
    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        DestroyTouchOverlay();
        // 悬浮控制栏也跟着触摸层一起撤掉（它就在触摸层窗口里），倒计时同步停掉
        _controlBarHideTimer.Stop();
        _mouseOverControlBar = false;

        var window = new SettingsWindow(_settings);
        _settingsWindow = window;

        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            EnsureTouchOverlay();
            // 设置期间不跑倒计时：恢复触摸层后按最新设置重新开始
            RestartControlBarCountdown();
        };

        window.Show(this);
    }

    /// <summary>关闭应用（控制栏「关闭」控件调用）。</summary>
    public void CloseApp() => Close();

    /// <summary>最小化窗口（控制栏「最小化」控件调用）。</summary>
    public void MinimizeWindow() => WindowState = WindowState.Minimized;

    /// <summary>
    /// 进入 / 退出全屏（控制栏「全屏」控件调用）。
    /// 状态变化经 WindowState 接线同步到 ViewModel.IsFullscreen，按钮文本随之切换。
    /// </summary>
    public void ToggleFullscreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    private void ApplyRenderer(VideoRenderer renderer)
    {
        if (_rendererSwitchReady && VideoView.Renderer != renderer)
        {
            VideoView.Renderer = renderer;
        }

        EnsureTouchOverlay();
    }

    /// <summary>
    /// 管理视频区透明触摸层：只在 Native 渲染器下需要（视频是原生子窗口，会吞掉触摸）。
    /// OpenGl 渲染器下视频本身在 Avalonia 视觉树内，触摸可直接到达，无需覆盖层。
    /// </summary>
    private void EnsureTouchOverlay()
    {
        if (!_rendererSwitchReady || _viewModel.Renderer != VideoRenderer.Native)
        {
            DestroyTouchOverlay();
            return;
        }

        if (_touchOverlay is null)
        {
            _touchOverlay = new VideoTouchOverlay();
            _touchOverlay.VideoTapped += OnVideoTapped;
            _touchOverlay.VideoDoubleTapped += OnVideoDoubleTapped;
            _touchOverlay.VideoDragStarted += OnVideoDragStarted;
            _touchOverlay.VideoDragged += OnVideoDragged;
            _touchOverlay.VideoDragCompleted += OnVideoDragCompleted;
            _touchOverlay.VideoZoomRequested += OnVideoZoomRequested;
            _touchOverlay.BrightnessRestoreRequested += OnBrightnessRestoreRequested;
            _touchOverlay.ActivityDetected += OnOverlayActivityDetected;
            _touchOverlay.MouseMoved += OnOverlayMouseMoved;
            _touchOverlay.ControlBarMouseEntered += OnControlBarMouseEntered;
            _touchOverlay.ControlBarMouseExited += OnControlBarMouseExited;
            _touchOverlay.Show(this);
            // 触摸层可能刚建好（例如关闭设置窗口后重建），先把亮度提示的显隐同步过来
            _touchOverlay.SetBrightnessHintVisible(_viewModel.Brightness != 0d);
            _overlaySyncTimer.Start();
            StartupTrace.Mark("视频区透明触摸层已启用（Native 渲染器）");
        }

        // 悬浮控制栏也住在触摸层里：当前形态是悬浮就注入内容（触摸层重建后要重挂），再同步显隐与底部圆角
        if (_viewModel.IsOverlayControlBarActive)
        {
            AttachOverlayControlBar();
        }

        _touchOverlay.SetControlBarVisible(_viewModel.IsOverlayControlBarVisible);
        SyncControlBarCornerRadius();
        SyncOverlayBounds();
    }

    private void DestroyTouchOverlay()
    {
        if (_touchOverlay is null)
        {
            return;
        }

        _overlaySyncTimer.Stop();
        _touchOverlay.VideoTapped -= OnVideoTapped;
        _touchOverlay.VideoDoubleTapped -= OnVideoDoubleTapped;
        _touchOverlay.VideoDragStarted -= OnVideoDragStarted;
        _touchOverlay.VideoDragged -= OnVideoDragged;
        _touchOverlay.VideoDragCompleted -= OnVideoDragCompleted;
        _touchOverlay.VideoZoomRequested -= OnVideoZoomRequested;
        _touchOverlay.BrightnessRestoreRequested -= OnBrightnessRestoreRequested;
        _touchOverlay.ActivityDetected -= OnOverlayActivityDetected;
        _touchOverlay.MouseMoved -= OnOverlayMouseMoved;
        _touchOverlay.ControlBarMouseEntered -= OnControlBarMouseEntered;
        _touchOverlay.ControlBarMouseExited -= OnControlBarMouseExited;
        _touchOverlay.Close();
        _touchOverlay = null;
        _overlayControlBarAttached = false;
        StartupTrace.Mark("视频区透明触摸层已关闭");
    }

    /// <summary>把悬浮控制栏注入触摸层（只注入一次；触摸层重建后标志复位、由 EnsureTouchOverlay 再挂）。</summary>
    private void AttachOverlayControlBar()
    {
        if (_touchOverlay is null || _overlayControlBarAttached)
        {
            return;
        }

        _overlayControlBarAttached = true;
        _touchOverlay.SetControlBar(new PlayerControlBar { DataContext = _viewModel });
    }

    /// <summary>
    /// 应用「当前窗口状态下的控制栏显示形态」：显示方式开关变化（窗口 / 全屏分开设置）与进出全屏都会走这里。
    /// 悬浮 → 触摸层里注入并同步悬浮栏；停靠 → 同步隐藏悬浮栏（停靠栏的显隐由绑定跟着 ViewModel 走）。
    /// 顺带对齐悬浮栏底部圆角、重开自动隐藏倒计时。
    /// </summary>
    private void ApplyControlBarOverlay()
    {
        // 触摸层正被设置窗口接管时先不动它（与 OpenSettings 的撤层策略一致），关窗后会补齐
        if (_settingsWindow is null)
        {
            EnsureTouchOverlay();
        }

        SyncControlBarCornerRadius();
        RestartControlBarCountdown();
    }

    /// <summary>
    /// 悬浮控制栏底部圆角与主窗口的窗口圆角对齐：Win11 普通窗口是 8 px 圆角，悬浮栏贴窗口底边，
    /// 圆角不一致时四角会“突出”到窗口圆角外面；Win10（查不到圆角属性）与最大化 / 全屏（直角）下为 0。
    /// </summary>
    private void SyncControlBarCornerRadius()
    {
        var radius = WindowState == WindowState.Normal
            ? WindowCorners.GetRadiusDip(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero)
            : 0d;
        _touchOverlay?.SetControlBarCornerRadius(radius);
    }

    /// <summary>
    /// 把触摸层贴合到视频区（屏幕坐标）。主窗口移动时由 PositionChanged 事件即时调用，
    /// 窗口拖动过程中与主窗口同步移动；值没变化一律跳过，不给窗口做无谓的原生命令。
    /// </summary>
    private void SyncOverlayBounds()
    {
        var overlay = _touchOverlay;
        if (overlay is null || !VideoView.IsAttachedToVisualTree())
        {
            return;
        }

        // 值没变就不下发：没意义的窗口搬动/改尺寸只会让光标下方的窗口多做一次原生命令
        // （还可能让系统补发鼠标移动事件，把「鼠标支持」的倒计时无端刷新）
        var position = VideoView.PointToScreen(new Point(0, 0));
        if (overlay.Position != position)
        {
            overlay.Position = position;
        }

        if (!overlay.Width.Equals(VideoView.Bounds.Width))
        {
            overlay.Width = VideoView.Bounds.Width;
        }

        if (!overlay.Height.Equals(VideoView.Bounds.Height))
        {
            overlay.Height = VideoView.Bounds.Height;
        }
    }

    private void OnVideoTapped(object? sender, Point position) =>
        ExecuteGestureAction(_settings.Gestures.TapAction, $"单击 ({position.X:F0},{position.Y:F0})");

    /// <summary>画面双击（触摸层）：操作来自「画面手势」设置，默认播放 / 暂停。</summary>
    private void OnVideoDoubleTapped(object? sender, Point position) =>
        ExecuteGestureAction(_settings.Gestures.DoubleTapAction, $"双击 ({position.X:F0},{position.Y:F0})");

    /// <summary>
    /// 拖动开始（触摸层在超过阈值、方向锁定后上报一次）：决定这次滑动调节什么。
    /// 横向 → 「左右滑动」的设置；纵向 → 按起点落在左 / 右半区取「左 / 右半区上下滑动」的设置。
    /// 平移模式（原始大小 / 自由缩放）下拖动用于平移画面，不做调节。
    /// 基准值在这里记一次，全程按「基准值 + 位移比例 × 量程」算，因此滑过头再滑回来能原路返回。
    /// </summary>
    private void OnVideoDragStarted(object? sender, VideoDragStartedEventArgs e)
    {
        _dragAdjust = VideoSwipeAdjust.None;
        _dragTotal = default;
        _dragAppliedValue = double.NaN;
        _dragAxis = e.Axis;

        if (_settings.ScalingMode is VideoScalingMode.Original or VideoScalingMode.Free)
        {
            return;
        }

        var halfWidth = (_touchOverlay?.Bounds.Width ?? 0d) / 2d;
        var (adjust, gesture) = e.Axis == VideoDragAxis.Horizontal
            ? (_settings.Gestures.HorizontalSwipeAdjust, "左右滑动")
            : e.Start.X < halfWidth
                ? (_settings.Gestures.LeftVerticalSwipeAdjust, "左半区上下滑动")
                : (_settings.Gestures.RightVerticalSwipeAdjust, "右半区上下滑动");
        if (adjust == VideoSwipeAdjust.None)
        {
            return;
        }

        _dragAdjust = adjust;
        _dragBaseValue = adjust switch
        {
            VideoSwipeAdjust.Seek => _viewModel.PositionSeconds,
            VideoSwipeAdjust.Volume => _viewModel.Volume,
            _ => _viewModel.Brightness,
        };

        // 「拖动时画面跟随」开启时，整次拖动期间画面跟着手指走（抬手恢复原播放状态）
        if (adjust == VideoSwipeAdjust.Seek && _settings.Gestures.LiveSeekWhileSwiping)
        {
            _viewModel.BeginSeekPreview();
        }

        StartupTrace.Mark($"画面{gesture} → {VideoSwipeAdjustOptions.LabelOf(adjust)}（基准 {_dragBaseValue:F1}）");
    }

    /// <summary>
    /// 滑动过程：位移按锁定轴折算后加到基准值上——进度是固定比例（每像素 <see cref="DragSeekSecondsPerPixel"/> 秒，
    /// 与视频区大小 / 时长无关），音量 / 亮度是「滑满视频区对应方向的尺寸 = 100 个单位」。
    /// 三种调节配到任意手势上都按同一套算，且统一成「水平向右 / 垂直向上 = 加大」，
    /// 因此左右 / 左上下 / 右上下怎么配都能调，不受拖动方向限制。
    /// 三种调节都在画面中央实时显示当前值：进度是「目标位置 / 总时长」，音量 / 亮度是当前数值。
    /// 进度累积位移抬手时再跳一次——开「拖动时画面跟随」时拖动过程中也连续跳，关掉就只在抬手时跳。
    /// </summary>
    private void OnVideoDragged(object? sender, Point delta)
    {
        // 平移画面：仅「原始大小」「自由缩放」模式有效，其余模式由引擎忽略
        _viewModel.PanVideo(delta.X, delta.Y);

        if (_dragAdjust == VideoSwipeAdjust.None)
        {
            return;
        }

        _dragTotal += delta;

        // 沿锁定轴的有向位移（正 = 加大：水平向右、垂直向上）与对应方向的视频区尺寸
        var progress = _dragAxis == VideoDragAxis.Horizontal ? _dragTotal.X : -_dragTotal.Y;
        var extent = Math.Max(1d, _dragAxis == VideoDragAxis.Horizontal
            ? _touchOverlay?.Bounds.Width ?? 0d
            : _touchOverlay?.Bounds.Height ?? 0d);

        switch (_dragAdjust)
        {
            case VideoSwipeAdjust.Seek:
                var target = _dragBaseValue + progress * DragSeekSecondsPerPixel;
                _touchOverlay?.ShowCenterHint(DescribeSeekHint(target));
                // 「拖动时画面跟随」开启时边拖边跳；关闭时为空操作，抬手才跳
                _viewModel.PreviewSeek(target);
                break;
            case VideoSwipeAdjust.Volume:
                var volume = Math.Clamp(_dragBaseValue + progress / extent * DragFullRange, 0d, GestureVolumeMax);
                _touchOverlay?.ShowCenterHint($"音量 {volume:F0}%");
                ApplyDragValue(volume, value => _viewModel.Volume = value);
                break;
            case VideoSwipeAdjust.Brightness:
                var brightness = Math.Clamp(_dragBaseValue + progress / extent * DragFullRange, -DragFullRange, DragFullRange);
                _touchOverlay?.ShowCenterHint($"亮度 {brightness:F0}");
                ApplyDragValue(brightness, value => _viewModel.Brightness = value);
                break;
        }
    }

    /// <summary>滑动调进度的中央提示：「目标位置（已过 + 滑动量）/ 总时长」。</summary>
    private string DescribeSeekHint(double seconds)
    {
        var total = _viewModel.DurationSeconds;
        var target = Math.Clamp(seconds, 0d, total);

        return $"{MainWindowViewModel.Format(TimeSpan.FromSeconds(target))} / {MainWindowViewModel.Format(TimeSpan.FromSeconds(total))}";
    }

    /// <summary>右下角「恢复亮度」被点击：亮度复位为 0（提示显隐由 Brightness 变化的接线统一处理）。</summary>
    private void OnBrightnessRestoreRequested(object? sender, EventArgs e)
    {
        StartupTrace.Mark("画面右下角「恢复亮度」→ 亮度复位为 0");
        _viewModel.Brightness = 0d;
    }

    // ── 悬浮控制栏：自动隐藏倒计时与鼠标支持（触屏优先）────────────────────────
    // 触摸层上报触摸 / 鼠标活动，这里统一决定倒计时的起停：
    // · 触摸 / 笔：重新计时，并刷新保护期（系统会伴随触摸合成鼠标事件，保护期内的鼠标事件一律忽略）；
    // · 鼠标：只在「鼠标支持」开启时参与——移动呼出控制栏、停在控制栏上不隐藏。

    /// <summary>悬浮控制栏行为设置变化：窗口 / 全屏显示方式、自动隐藏秒数、鼠标支持都即时生效。</summary>
    private void OnControlBarBehaviorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var behavior = _settings.ControlBarBehavior;
        switch (e.PropertyName)
        {
            // 显示方式（窗口 / 全屏分别设置）只打点：形态应用由 ViewModel 的 IsOverlayControlBarActive
            // 通知驱动（见构造函数里的接线），这里不重复做，免得同一件事做两遍
            case nameof(ControlBarBehaviorSettings.OverlayInWindowed):
            case nameof(ControlBarBehaviorSettings.OverlayInFullscreen):
                StartupTrace.Mark($"控制栏显示方式 → 窗口：{(behavior.OverlayInWindowed ? "悬浮" : "底部")}"
                    + $" · 全屏：{(behavior.OverlayInFullscreen ? "悬浮" : "底部")}"
                    + (_viewModel.Renderer == VideoRenderer.Native ? string.Empty : "（注意：悬浮形态挂在视频区触摸层里，当前渲染器下不会出现）"));
                break;
            case nameof(ControlBarBehaviorSettings.AutoHideSeconds):
                RestartControlBarCountdown();
                break;
            case nameof(ControlBarBehaviorSettings.MouseSupport):
                // 悬停抑制只在鼠标支持开启时有效：开关一变先清掉，倒计时按新规则重来
                _mouseOverControlBar = false;
                RestartControlBarCountdown();
                break;
        }
    }

    /// <summary>
    /// 悬浮控制栏的「无操作自动隐藏」倒计时统一入口（显隐变化、活动、设置变化都走这里）。
    /// 任一条件不满足就保持停止：当前形态不是悬浮、控制栏已收起、秒数为 0、设置窗口开着、鼠标停在控制栏上。
    /// </summary>
    private void RestartControlBarCountdown()
    {
        _controlBarHideTimer.Stop();

        var behavior = _settings.ControlBarBehavior;
        if (!_viewModel.IsOverlayControlBarActive
            || !_viewModel.IsControlBarVisible
            || behavior.AutoHideSeconds <= 0d
            || _settingsWindow is not null
            || (behavior.MouseSupport && _mouseOverControlBar))
        {
            return;
        }

        _controlBarHideTimer.Interval = TimeSpan.FromSeconds(behavior.AutoHideSeconds);
        _controlBarHideTimer.Start();
    }

    /// <summary>触摸层上的指针活动：重新计时；触摸 / 笔操作同时刷新「触屏优先」的保护期。</summary>
    private void OnOverlayActivityDetected(object? sender, PointerType type)
    {
        var isTouch = type is PointerType.Touch or PointerType.Pen;
        if (!isTouch && !_settings.ControlBarBehavior.MouseSupport)
        {
            // 鼠标支持关闭时，鼠标活动不参与控制栏行为（触屏优先）
            return;
        }

        if (isTouch)
        {
            _lastTouchAt = DateTime.UtcNow;
            // 触摸了就别再看鼠标悬停：鼠标停在控制栏上也不再阻止这次自动隐藏
            _mouseOverControlBar = false;
        }

        RestartControlBarCountdown();
    }

    /// <summary>
    /// 鼠标在视频区移动：鼠标支持开启时自动呼出控制栏并重新计时。
    /// 两处关键处理：一，能收到视频区上的移动就说明指针不在控制栏上，顺手清掉悬停抑制
    /// （避免残留状态让倒计时永远不跑）；二，位移不足 <see cref="MouseMoveThreshold"/> 的移动
    /// 不算「移动了一下」——抖动不刷新倒计时，鼠标停在视频区上时控制栏才会按时收起。
    /// </summary>
    private void OnOverlayMouseMoved(object? sender, Point position)
    {
        _mouseOverControlBar = false;

        var behavior = _settings.ControlBarBehavior;
        if (!_viewModel.IsOverlayControlBarActive || !behavior.MouseSupport)
        {
            return;
        }

        // 触屏优先：触摸刚结束时系统伴随合成的鼠标事件不呼出控制栏
        if (DateTime.UtcNow - _lastTouchAt < TouchMouseGuard)
        {
            return;
        }

        if (_lastMouseMoveAt is { } last && Distance(last, position) < MouseMoveThreshold)
        {
            return;
        }

        _lastMouseMoveAt = position;
        _viewModel.IsControlBarVisible = true;
        RestartControlBarCountdown();
    }

    /// <summary>两点间距离（鼠标移动阈值判定用）。</summary>
    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>鼠标进入（或停在）悬浮控制栏：悬停期间不自动隐藏。</summary>
    private void OnControlBarMouseEntered(object? sender, EventArgs e)
    {
        if (!_settings.ControlBarBehavior.MouseSupport
            || DateTime.UtcNow - _lastTouchAt < TouchMouseGuard)
        {
            return;
        }

        _mouseOverControlBar = true;
        RestartControlBarCountdown();
    }

    /// <summary>鼠标离开悬浮控制栏：按当前设置重新开始倒计时。</summary>
    private void OnControlBarMouseExited(object? sender, EventArgs e)
    {
        _mouseOverControlBar = false;
        RestartControlBarCountdown();
    }

    /// <summary>边滑边下发：与上一次下发的值相差不到一个步进就先攒着（拖动期间事件很密，全下发没必要）。</summary>
    private void ApplyDragValue(double value, Action<double> apply)
    {
        if (!double.IsNaN(_dragAppliedValue) && Math.Abs(value - _dragAppliedValue) < DragValueStep)
        {
            return;
        }

        _dragAppliedValue = value;
        apply(value);
    }

    /// <summary>
    /// 执行手势对应的操作。操作每次都在这里现读共享设置（设置页改完即生效，无需重新接线）；
    /// 窗口级操作（全屏）直接做，播放相关的走 ViewModel 的既有命令。
    /// </summary>
    private void ExecuteGestureAction(VideoGestureAction action, string gesture)
    {
        if (action == VideoGestureAction.None)
        {
            StartupTrace.Mark($"画面{gesture}：未配置操作，忽略");
            return;
        }

        StartupTrace.Mark($"画面{gesture} → {VideoGestureActionOptions.LabelOf(action)}");
        switch (action)
        {
            case VideoGestureAction.ToggleControls:
                _viewModel.ToggleControlBar();
                break;
            case VideoGestureAction.TogglePlayPause:
                _viewModel.TogglePauseCommand.Execute(null);
                break;
            case VideoGestureAction.Stop:
                _viewModel.StopCommand.Execute(null);
                break;
            case VideoGestureAction.Previous:
                _viewModel.PreviousCommand.Execute(null);
                break;
            case VideoGestureAction.Next:
                _viewModel.NextCommand.Execute(null);
                break;
            case VideoGestureAction.SeekBackward:
                _viewModel.SeekByCommand.Execute(-GestureSeekSeconds);
                break;
            case VideoGestureAction.SeekForward:
                _viewModel.SeekByCommand.Execute(GestureSeekSeconds);
                break;
            case VideoGestureAction.VolumeUp:
                _viewModel.Volume = Math.Min(GestureVolumeMax, _viewModel.Volume + GestureVolumeStep);
                break;
            case VideoGestureAction.VolumeDown:
                _viewModel.Volume = Math.Max(0d, _viewModel.Volume - GestureVolumeStep);
                break;
            case VideoGestureAction.ToggleFullscreen:
                ToggleFullscreen();
                break;
        }
    }

    /// <summary>
    /// 拖动结束：跳到最终位置并恢复预览前的播放状态（没开预览时只是普通跳转），
    /// 累计位移与 mpv 回读值一起写进日志，便于调手感与定位问题。
    /// </summary>
    private void OnVideoDragCompleted(object? sender, Point total)
    {
        _viewModel.EndVideoDrag(total.X, total.Y);

        if (_dragAdjust == VideoSwipeAdjust.Seek)
        {
            // 抬手位置同样按锁定轴折算，和拖动过程用的是同一套（垂直手势配进度时向上为正）
            var progress = _dragAxis == VideoDragAxis.Horizontal ? total.X : -total.Y;
            var target = Math.Clamp(_dragBaseValue + progress * DragSeekSecondsPerPixel, 0d, _viewModel.DurationSeconds);
            StartupTrace.Mark($"画面{(_dragAxis == VideoDragAxis.Horizontal ? "横向" : "纵向")}滑动 {progress:F0} px → 跳转到 {target:F1}s");
            _viewModel.EndSeekPreview(target);
        }

        _touchOverlay?.HideCenterHint();
        _dragAdjust = VideoSwipeAdjust.None;
        _dragAppliedValue = double.NaN;
    }

    /// <summary>
    /// 捏合或滚轮缩放：自由缩放模式下应用倍率，并在画面中央显示「缩放 x%」——
    /// 以「像素点对点」为 100%（显示尺寸 = 画面原始像素），因此铺满视频区的 1× 不一定是 100%。
    /// 其余模式下引擎会忽略缩放，这里也就不显示提示。
    /// </summary>
    private void OnVideoZoomRequested(object? sender, double factor)
    {
        if (_settings.ScalingMode != VideoScalingMode.Free)
        {
            return;
        }

        _viewModel.ZoomVideo(factor);
        var percent = _viewModel.ZoomPercent;
        if (percent <= 0d)
        {
            return;
        }

        _touchOverlay?.ShowCenterHint($"缩放 {percent:F0}%");
        // 停止缩放（手指抬起 / 滚轮停下）后 900ms 内没有新事件才自动隐藏
        _zoomHintTimer.Stop();
        _zoomHintTimer.Start();
    }
}