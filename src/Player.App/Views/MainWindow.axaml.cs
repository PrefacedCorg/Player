using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HanumanInstitute.LibMpv.Avalonia;
using Player.App.Assists;
using Player.App.ViewModels;
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

    /// <summary>纵向滑动调节滑满视频区一整个高对应的量程：音量 / 亮度是 100。</summary>
    private const double DragFullRange = 100d;

    /// <summary>左右滑动调进度的固定比例：4 像素 = 1 秒，与视频区大小、视频时长都无关。</summary>
    private const double DragSeekSecondsPerPixel = 0.25d;

    /// <summary>滑动过程中下发的步进：与上一次下发的值相差不到它就先攒着。</summary>
    private const double DragValueStep = 0.5d;

    private readonly MainWindowViewModel _viewModel;

    // ── 画面滑动调节的会话状态（拖动开始到抬手之间有效）──

    /// <summary>本次滑动调节什么；None 表示这次拖动不平移也不调节（例如平移模式下由引擎平移画面）。</summary>
    private VideoSwipeAdjust _dragAdjust = VideoSwipeAdjust.None;

    /// <summary>本次拖动的累计位移（DIP）。</summary>
    private Point _dragTotal;

    /// <summary>调节的基准值（拖动开始时的进度 / 音量 / 亮度），全程以它为基准加位移比例。</summary>
    private double _dragBaseValue;

    /// <summary>最近一次下发的值（节流比较用；NaN 表示还没下发过）。</summary>
    private double _dragAppliedValue = double.NaN;

    /// <summary>应用级实时设置：主窗口与设置窗口共用同一个实例（见 PlayerSettings）。</summary>
    private readonly PlayerSettings _settings;

    /// <summary>渲染器切换允许生效的时机：窗口显示之后。原生渲染视图（NativeView）未上树就创建会阻塞启动。</summary>
    private bool _rendererSwitchReady;

    private readonly DispatcherTimer _overlaySyncTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
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
        };

        // 全屏状态接线：WindowState ↔ ViewModel.IsFullscreen（控制栏「全屏」控件的按钮文本跟随它，
        // 用户按 F11 或系统快捷键切全屏时按钮同样跟着变）
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                _viewModel.IsFullscreen = WindowState == WindowState.FullScreen;
            }
        };

        Opened += (_, _) =>
        {
            _rendererSwitchReady = true;
            EnsureTouchOverlay();

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
        var window = new SettingsWindow(_settings);
        _settingsWindow = window;

        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            EnsureTouchOverlay();
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
            _touchOverlay.Show(this);
            // 触摸层可能刚建好（例如关闭设置窗口后重建），先把亮度提示的显隐同步过来
            _touchOverlay.SetBrightnessHintVisible(_viewModel.Brightness != 0d);
            _overlaySyncTimer.Start();
            StartupTrace.Mark("视频区透明触摸层已启用（Native 渲染器）");
        }

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
        _touchOverlay.Close();
        _touchOverlay = null;
        StartupTrace.Mark("视频区透明触摸层已关闭");
    }

    /// <summary>把触摸层贴合到视频区。独立窗口无法随父窗口自动移动，需按屏幕坐标同步。</summary>
    private void SyncOverlayBounds()
    {
        var overlay = _touchOverlay;
        if (overlay is null || !VideoView.IsAttachedToVisualTree())
        {
            return;
        }

        overlay.Position = VideoView.PointToScreen(new Point(0, 0));
        overlay.Width = VideoView.Bounds.Width;
        overlay.Height = VideoView.Bounds.Height;
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
    /// 滑动过程：位移按比例折算后加到基准值上——
    /// 横向是固定比例（每像素 <see cref="DragSeekSecondsPerPixel"/> 秒，与视频区宽 / 时长无关），
    /// 纵向滑满视频区高 = 音量 / 亮度的 100 个单位。
    /// 音量与亮度边滑边下发（有听觉 / 画面反馈）；进度在画面中央显示目标位置，
    /// 并累积位移抬手时再跳一次——开「拖动时画面跟随」时拖动过程中也连续跳（内部节流，
    /// 避免狂发 seek 把播放打顿），关掉就只在抬手时跳。
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
        var height = Math.Max(1d, _touchOverlay?.Bounds.Height ?? 0d);

        switch (_dragAdjust)
        {
            case VideoSwipeAdjust.Seek:
                var target = _dragBaseValue + _dragTotal.X * DragSeekSecondsPerPixel;
                _touchOverlay?.ShowCenterHint(DescribeSeekHint(target));
                // 「拖动时画面跟随」开启时边拖边跳（内部按间隔节流）；关闭时为空操作，抬手才跳
                _viewModel.PreviewSeek(target);
                break;
            case VideoSwipeAdjust.Volume:
                ApplyDragValue(
                    _dragBaseValue - _dragTotal.Y / height * DragFullRange,
                    value => _viewModel.Volume = Math.Clamp(value, 0d, GestureVolumeMax));
                break;
            case VideoSwipeAdjust.Brightness:
                ApplyDragValue(
                    _dragBaseValue - _dragTotal.Y / height * DragFullRange,
                    value => _viewModel.Brightness = Math.Clamp(value, -DragFullRange, DragFullRange));
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
            var target = Math.Clamp(_dragBaseValue + total.X * DragSeekSecondsPerPixel, 0d, _viewModel.DurationSeconds);
            StartupTrace.Mark($"画面横向滑动 {total.X:F0} px → 跳转到 {target:F1}s");
            _viewModel.EndSeekPreview(target);
        }

        _touchOverlay?.HideCenterHint();
        _dragAdjust = VideoSwipeAdjust.None;
        _dragAppliedValue = double.NaN;
    }

    /// <summary>捏合或滚轮缩放（仅自由缩放模式生效）。</summary>
    private void OnVideoZoomRequested(object? sender, double factor) =>
        _viewModel.ZoomVideo(factor);
}