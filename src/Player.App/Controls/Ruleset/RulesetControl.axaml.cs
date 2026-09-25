using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Player.App.Rules;

namespace Player.App.Controls.Ruleset;

/// <summary>
/// 规则集编辑器（照 ClassIsland 的 RulesetControl）：添加/删除/复制规则组与规则、反转与逻辑模式切换，
/// 每条规则从注册表里选，并按规则类型显示对应的设置控件。求值交给 <see cref="PlayerRuleService"/>。
/// </summary>
public partial class RulesetControl : UserControl
{
    public static readonly StyledProperty<Rules.Ruleset?> RulesetProperty =
        AvaloniaProperty.Register<RulesetControl, Rules.Ruleset?>(nameof(Ruleset));

    public RulesetControl() => InitializeComponent();

    /// <summary>要编辑的规则集。</summary>
    public Rules.Ruleset? Ruleset
    {
        get => GetValue(RulesetProperty);
        set => SetValue(RulesetProperty, value);
    }

    /// <summary>可用规则（规则下拉框的数据源）。</summary>
    public IReadOnlyList<RuleRegistryInfo> RuleInfos { get; } = PlayerRuleRegistry.All;

    private void ButtonAddGroup_OnClick(object? sender, RoutedEventArgs e) =>
        Ruleset?.Groups.Add(new RuleGroup());

    [RelayCommand]
    private void AddRule(RuleGroup? group) => group?.Rules.Add(new Rule());

    [RelayCommand]
    private void RemoveRule(Rule? rule)
    {
        if (Ruleset is null || rule is null)
        {
            return;
        }

        foreach (var group in Ruleset.Groups)
        {
            group.Rules.Remove(rule);
        }
    }

    [RelayCommand]
    private void RemoveGroup(RuleGroup? group)
    {
        if (Ruleset is not null && group is not null)
        {
            Ruleset.Groups.Remove(group);
        }
    }

    [RelayCommand]
    private void DuplicateGroup(RuleGroup? group)
    {
        if (Ruleset is not null && group is not null)
        {
            Ruleset.Groups.Add(group.Clone());
        }
    }
}