using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Player.App.ViewModels;
using Player.Platform;

namespace Player.App.Views;

/// <summary>
/// 设置窗口：左侧导航（FANavigationView）+ 右侧内容页。
/// 导航项按 ViewModel 列表生成（见 <see cref="BuildNavigationItems"/>），
/// 内容页按 ViewModel 类型由 DataTemplates 匹配到独立 UserControl（见 Views/SettingsPages/）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsWindowViewModel _viewModel;

    /// <summary>
    /// XAML 运行期加载器与设计器要求有公共无参构造（AVLN3001）。
    /// 它取的是 App 上那份共享设置，与带参构造完全等价，不会出现"改了个孤立实例"的情况。
    /// </summary>
    public SettingsWindow()
        : this(App.Settings)
    {
    }

    /// <param name="settings">与主窗口共用的实时设置实例，改动即时下发到播放引擎。</param>
    public SettingsWindow(PlayerSettings settings)
    {
        InitializeComponent();

        _viewModel = new SettingsWindowViewModel(settings);
        DataContext = _viewModel;

        BuildNavigationItems();

        StartupTrace.Mark($"设置窗口已构建（{_viewModel.Pages.Count} 个分区）");
    }

    /// <summary>
    /// 按 ViewModel 的分区列表生成导航项。用真实的 FANavigationViewItem 容器而不是
    /// MenuItemsSource + MenuItemTemplate：后者会让 FA 用 FAItemTemplateWrapper 包装模板，
    /// 而该包装类的 Match() 抛 NotImplementedException，窗格布局阶段会直接崩。
    /// </summary>
    private void BuildNavigationItems()
    {
        foreach (var page in _viewModel.Pages)
        {
            NavView.MenuItems.Add(new FANavigationViewItem
            {
                Content = page.Title,
                IconSource = new FASymbolIconSource { Symbol = page.Symbol, FontSize = 20 },
                Tag = page,
            });
        }

        // 默认选中第一个分区（同时触发 SelectionChanged，把内容区带上首屏）
        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem { Tag: SettingsPageViewModel page })
        {
            _viewModel.SelectedPage = page;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}