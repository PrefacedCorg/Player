using CommunityToolkit.Mvvm.ComponentModel;

namespace Player.App.ViewModels;

/// <summary>
/// 设置窗口 ViewModel：只负责"左侧导航分区 ↔ 当前页"的选择，页面内容全部由
/// <see cref="SettingsPageViewModel"/> 派生类承载（见 SettingsPages.cs）。
/// </summary>
public partial class SettingsWindowViewModel : ObservableObject
{
    /// <summary>当前选中的设置分区。导航栏与右侧内容区共用这一个来源。</summary>
    [ObservableProperty]
    private SettingsPageViewModel _selectedPage;

    public SettingsWindowViewModel(PlayerSettings settings)
    {
        Pages =
        [
            new PlaybackSettingsViewModel(),
            new DisplaySettingsViewModel(settings),
            new ControlBarSettingsViewModel(settings),
            new AudioSettingsViewModel(),
            new LibrarySettingsViewModel(),
            new AboutSettingsViewModel(),
        ];

        _selectedPage = Pages[0];
    }

    /// <summary>导航分区列表。新增一个设置页 = 在这里加一项 + 在 SettingsWindow.axaml 加一段 DataTemplate。</summary>
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    /// <summary>底部提示：说明哪些项已经真的生效，避免把界面状态误当成已落地。</summary>
    public string StatusHint => "画面缩放方式与控制栏布局已接通播放器，改动即时生效；其余项仍是界面状态，尚未接入持久化。";
}