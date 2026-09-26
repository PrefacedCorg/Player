using Avalonia.Controls;
using Avalonia.Interactivity;
using Player.App.Rules;

namespace Player.App.Views.SettingsPages.ControlSettings;

/// <summary>
/// 控件高级设置视图（外观/字体/布局 + 按规则隐藏）。规则集编辑器由"编辑规则集…"按钮上的弹出面板承载。
/// 「列比例」区块只对对齐分割线显示：选中分割线时才有意义，其它组件隐藏（code-behind 切换）。
/// </summary>
public partial class AdvancedSettingsView : UserControl
{
    public AdvancedSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateDividerVisibility();
    }

    /// <summary>规则集面板关闭后重新求值一次：编辑结果立即反映到控制栏上的隐藏状态。</summary>
    private void RulesetFlyout_OnClosed(object? sender, EventArgs e) => PlayerRuleService.NotifyStatusChanged();

    private void UpdateDividerVisibility() =>
        DividerSettings.IsVisible = DataContext is PlayerControlItem { Settings: DividerControlSettings };
}
