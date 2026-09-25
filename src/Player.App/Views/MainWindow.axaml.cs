using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HanumanInstitute.LibMpv.Avalonia;
using Player.App.ViewModels;
using Player.Platform;

namespace Player.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

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
        };

        Opened += (_, _) =>
        {
            _rendererSwitchReady = true;
            EnsureTouchOverlay();

            // --settings：启动后直接进设置页（调试验证 / 大屏快捷入口）。放到 Dispatcher 队列里，
            // 避免在 Opened 回调内直接 ShowDialog 触发未完成显示就弹模态窗口的问题。
            if (Program.OpenSettingsOnStartup)
            {
                Dispatcher.UIThread.Post(() => _ = OpenSettingsAsync());
            }
        };
        Closed += (_, _) => DestroyTouchOverlay();
    }

    /// <summary>
    /// 打开设置窗口（模态）。设置窗口是独立顶层窗口，
    /// 期间先撤掉视频区透明触摸层——它同样是顶层窗口，会抢走设置界面的点击命中。
    /// </summary>
    public async Task OpenSettingsAsync()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        DestroyTouchOverlay();
        var window = new SettingsWindow(_settings);
        _settingsWindow = window;

        try
        {
            await window.ShowDialog(this);
        }
        finally
        {
            _settingsWindow = null;
            EnsureTouchOverlay();
        }
    }

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
            _touchOverlay.VideoDragged += OnVideoDragged;
            _touchOverlay.VideoDragCompleted += OnVideoDragCompleted;
            _touchOverlay.VideoZoomRequested += OnVideoZoomRequested;
            _touchOverlay.Show(this);
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
        _touchOverlay.VideoDragged -= OnVideoDragged;
        _touchOverlay.VideoDragCompleted -= OnVideoDragCompleted;
        _touchOverlay.VideoZoomRequested -= OnVideoZoomRequested;
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
        _viewModel.OnVideoTapped(position.X, position.Y);

    /// <summary>视频区拖动：点对点模式下平移画面（其余模式由引擎侧忽略）。</summary>
    private void OnVideoDragged(object? sender, Point delta) =>
        _viewModel.PanVideo(delta.X, delta.Y);

    /// <summary>拖动结束：把本次累计位移与 mpv 回读值写进日志，便于调手感与定位问题。</summary>
    private void OnVideoDragCompleted(object? sender, Point total) =>
        _viewModel.EndVideoDrag(total.X, total.Y);

    /// <summary>捏合或滚轮缩放（仅自由缩放模式生效）。</summary>
    private void OnVideoZoomRequested(object? sender, double factor) =>
        _viewModel.ZoomVideo(factor);
}