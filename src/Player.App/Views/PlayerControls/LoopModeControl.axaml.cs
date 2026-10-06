using Avalonia.Controls;
using Player.App.Controls;
using Player.App.ViewModels;
using Player.Playback;

namespace Player.App.Views.PlayerControls;

/// <summary>控制栏的「循环模式」控件：点击循环切换 单个循环 / 列表循环 / 单个播放 / 随机播放。</summary>
public partial class LoopModeControl : UserControl
{
    private MainWindowViewModel? _subscribedVm;

    public LoopModeControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _subscribedVm = DataContext as MainWindowViewModel;
        if (_subscribedVm is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyLoopIcon(vm.LoopMode);
        }
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_subscribedVm is { } vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.LoopMode) && _subscribedVm is { } vm)
        {
            ApplyLoopIcon(vm.LoopMode);
        }
    }

    /// <summary>按循环模式切换图标：单曲循环用 LoopModeOnce，列表循环用 LoopMode，顺序/随机用 LoopModeSequential。</summary>
    private void ApplyLoopIcon(PlaybackLoopMode mode)
    {
        var btn = this.FindControl<PlayerControlButton>("LoopButton");
        if (btn == null) return;

        btn.Icon = mode switch
        {
            PlaybackLoopMode.SingleLoop => PlayerControlIcons.LoopModeOnce,
            PlaybackLoopMode.ListLoop => PlayerControlIcons.LoopMode,
            PlaybackLoopMode.SinglePlay or PlaybackLoopMode.RandomPlay => PlayerControlIcons.LoopModeSequential,
            _ => PlayerControlIcons.LoopMode
        };
    }
}
