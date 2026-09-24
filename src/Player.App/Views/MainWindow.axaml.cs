using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HanumanInstitute.LibMpv.Avalonia;
using Player.App.ViewModels;
using Player.Platform;
using Player.Playback;

namespace Player.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    /// <summary>渲染器切换允许生效的时机：窗口显示之后。原生渲染视图（NativeView）未上树就创建会阻塞启动。</summary>
    private bool _rendererSwitchReady;

    private readonly DispatcherTimer _overlaySyncTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private VideoTouchOverlay? _touchOverlay;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(Program.StartupFiles)
        {
            Renderer = Program.StartupRenderer ?? VideoRenderer.Native,
        };

        // 必须在设置 DataContext（即首次建立 mpv 上下文）之前定好渲染器：
        // 否则会先创建一个渲染上下文、加载一次文件，切换后上下文重建、文件丢失。
        VideoView.Renderer = _viewModel.Renderer;

        // 设置 DataContext 后，MpvView 通过 OneWayToSource 绑定把 mpv 上下文交给 ViewModel。
        DataContext = _viewModel;

        _overlaySyncTimer.Tick += (_, _) => SyncOverlayBounds();

        // 进度条的拖动判定必须挂在隧道阶段并允许已处理事件：
        // Slider 内部的拇指会接管 PointerPressed/PointerReleased 并标记为已处理，
        // 用 XAML 的冒泡事件（handledEventsToo=false）会完全收不到，导致拖动判定失效、位置被引擎回写弹回。
        PositionSlider.AddHandler(
            PointerPressedEvent, OnPositionSliderPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PositionSlider.AddHandler(
            PointerReleasedEvent, OnPositionSliderPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        Opened += (_, _) =>
        {
            _rendererSwitchReady = true;
            EnsureTouchOverlay();
        };
        Closed += (_, _) => DestroyTouchOverlay();
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

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择媒体文件（可多选，按顺序连续播放）",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("媒体文件")
                {
                    Patterns = MediaKindResolver.SupportedExtensions.Select(static ext => "*" + ext).ToArray(),
                },
            ],
        });

        var paths = files
            .Select(static f => f.TryGetLocalPath())
            .Where(static p => !string.IsNullOrEmpty(p))
            .Select(static p => p!)
            .ToList();

        if (paths.Count > 0)
        {
            _viewModel.OpenFiles(paths);
        }
    }

    /// <summary>
    /// 进度条按下即视为开始拖动（引擎回传的位置在此期间不覆盖滑块）。
    /// 注意：绝不能用 PointerCaptureLost 当作松手信号——Slider 内部的拇指一接管指针捕获
    /// 就会触发它，会导致刚按下就结束拖动，滑块被引擎回写顶回原位置（表现为"拖不动、回弹"）。
    /// </summary>
    private void OnPositionSliderPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _viewModel.BeginScrub();

    private void OnPositionSliderPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        _viewModel.EndScrub();

    /// <summary>切换渲染器会重建渲染视图与 mpv 上下文，M0 阶段用于在真机上对比 OpenGl / Native / Software。</summary>
    private void OnRendererChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: VideoRenderer renderer })
        {
            ApplyRenderer(renderer);
        }
    }
}