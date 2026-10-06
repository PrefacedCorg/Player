using Avalonia.Controls;
using Player.App.Controls;
using Player.App.ViewModels;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「播放 / 暂停」控件：按钮文字与图标都随播放状态切换
/// （文字「播放 / 暂停」，图标显示将执行的动作：暂停中 ▶、播放中 ⏸）。
/// </summary>
public partial class PlayPauseControl : UserControl
{
    private MainWindowViewModel? _subscribedVm;

    public PlayPauseControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedVm = DataContext as MainWindowViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyStateIcon(_subscribedVm.PlayPauseText);
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.PlayPauseText))
        {
            ApplyStateIcon(_subscribedVm?.PlayPauseText);
        }
    }

    /// <summary>图标与文字同义：文字是「播放」时显示 ▶，是「暂停」时显示 ⏸。</summary>
    private void ApplyStateIcon(string? text) =>
        Button.Icon = text == "播放" ? PlayerControlIcons.Play : PlayerControlIcons.Pause;
}
