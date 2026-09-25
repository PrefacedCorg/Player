using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;

namespace Player.App.Rules;

/// <summary>规则组/规则集的逻辑模式。</summary>
public enum RulesetLogicalMode
{
    /// <summary>任一规则满足。</summary>
    Or = 0,

    /// <summary>全部规则满足。</summary>
    And = 1,
}

/// <summary>规则求值状态：0 未知、1 不满足、2 已满足（用于编辑器上的状态圆点）。</summary>
public enum RuleState
{
    Unknown = 0,
    NotSatisfied = 1,
    Satisfied = 2,
}

/// <summary>
/// 一个包含若干规则组的规则集。判定是否生效用 <see cref="PlayerRuleService.IsRulesetSatisfied"/>。
/// </summary>
public partial class Ruleset : ObservableObject
{
    public Ruleset()
    {
        var group = new RuleGroup();
        group.Rules.Add(new Rule());
        Groups.Add(group);
    }

    /// <summary>规则组之间的逻辑模式。</summary>
    [ObservableProperty] private RulesetLogicalMode _mode = RulesetLogicalMode.Or;

    /// <summary>是否反转整个规则集的判断。</summary>
    [ObservableProperty] private bool _isReversed;

    /// <summary>满足状态。</summary>
    [ObservableProperty] private int _state;

    /// <summary>规则组。</summary>
    public ObservableCollection<RuleGroup> Groups { get; } = [];

    /// <summary>逻辑模式在界面下拉框里的索引（0 任一满足、1 全部满足）。</summary>
    public int ModeIndex
    {
        get => (int)Mode;
        set => Mode = (RulesetLogicalMode)value;
    }

    partial void OnModeChanged(RulesetLogicalMode value) => OnPropertyChanged(nameof(ModeIndex));

    /// <summary>把另一个规则集的内容复制进来。</summary>
    public void CopyFrom(Ruleset source)
    {
        Mode = source.Mode;
        IsReversed = source.IsReversed;
        Groups.Clear();
        foreach (var group in source.Groups)
        {
            Groups.Add(group.Clone());
        }
    }
}

/// <summary>一个规则组：组内若干规则按 <see cref="Mode"/> 组合。</summary>
public partial class RuleGroup : ObservableObject
{
    /// <summary>规则条目。</summary>
    public ObservableCollection<Rule> Rules { get; } = [];

    /// <summary>组内规则之间的逻辑模式。</summary>
    [ObservableProperty] private RulesetLogicalMode _mode = RulesetLogicalMode.And;

    /// <summary>是否反转本组的判断。</summary>
    [ObservableProperty] private bool _isReversed;

    /// <summary>是否启用本组。</summary>
    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>满足状态。</summary>
    [ObservableProperty] private int _state;

    /// <summary>逻辑模式在界面下拉框里的索引（0 任一满足、1 全部满足）。</summary>
    public int ModeIndex
    {
        get => (int)Mode;
        set => Mode = (RulesetLogicalMode)value;
    }

    partial void OnModeChanged(RulesetLogicalMode value) => OnPropertyChanged(nameof(ModeIndex));

    public RuleGroup Clone()
    {
        var clone = new RuleGroup { Mode = Mode, IsReversed = IsReversed, IsEnabled = IsEnabled };
        foreach (var rule in Rules)
        {
            clone.Rules.Add(rule.Clone());
        }

        return clone;
    }
}

/// <summary>一条规则：规则 ID + 该规则的设置对象。</summary>
public partial class Rule : ObservableObject
{
    /// <summary>规则 ID（对应 <see cref="PlayerRuleRegistry.Rules"/> 的键）。</summary>
    [ObservableProperty] private string _id = "";

    /// <summary>是否反转本条的判断。</summary>
    [ObservableProperty] private bool _isReversed;

    /// <summary>规则设置对象（类型由注册信息决定）。</summary>
    [ObservableProperty] private object? _settings;

    /// <summary>满足状态。</summary>
    [ObservableProperty] private int _state;

    /// <summary>规则注册信息（规则下拉框绑定用）。选中后把 Id 换掉，并按新规则重建一份默认设置。</summary>
    public RuleRegistryInfo? Info
    {
        get => Id != "" && PlayerRuleRegistry.Rules.TryGetValue(Id, out var info) ? info : null;
        set
        {
            if (value is null || value.Id == Id)
            {
                return;
            }

            Id = value.Id;
            Settings = PlayerRuleRegistry.CreateSettings(value.Id);
            OnPropertyChanged();
        }
    }

    public Rule Clone()
    {
        return new Rule
        {
            Id = Id,
            IsReversed = IsReversed,
            Settings = Settings switch
            {
                StringMatchingSettings match => new StringMatchingSettings { Text = match.Text, UseRegex = match.UseRegex },
                TimeRangeRuleSettings time => time.Clone(),
                _ => Settings,
            },
        };
    }
}

/// <summary>字符匹配规则的设置。</summary>
public partial class StringMatchingSettings : ObservableObject
{
    /// <summary>要匹配的字符串/正则。</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>是否使用正则。</summary>
    [ObservableProperty] private bool _useRegex;

    /// <summary>判断给定字符串是否满足此匹配设置。</summary>
    public bool IsMatching(string str)
    {
        if (!UseRegex)
        {
            return str == Text;
        }

        try
        {
            return Regex.Match(str, Text).Success;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>时间/星期条件的设置：当前时间在指定时段、且今天是选中的星期几。</summary>
public partial class TimeRangeRuleSettings : ObservableObject
{
    /// <summary>是否满足的时段起点（含）。</summary>
    [ObservableProperty] private TimeSpan _startTime = TimeSpan.Zero;

    /// <summary>是否满足的时段终点（不含，小于起点时按跨零点处理）。</summary>
    [ObservableProperty] private TimeSpan _endTime = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59);

    /// <summary>七个星期选项（周一在前，与界面顺序一致）。</summary>
    public ObservableCollection<WeekdayOption> Weekdays { get; } =
    [
        new("周一"), new("周二"), new("周三"), new("周四"), new("周五"), new("周六"), new("周日"),
    ];

    /// <summary>判断给定时间是否满足。</summary>
    public bool IsMatching(DateTime now)
    {
        // DayOfWeek 是 周日=0，界面上是 周一=0，这里换算一下
        var index = ((int)now.DayOfWeek + 6) % 7;
        if (!Weekdays[index].IsSelected)
        {
            return false;
        }

        var time = now.TimeOfDay;
        return StartTime <= EndTime
            ? time >= StartTime && time < EndTime
            : time >= StartTime || time < EndTime;
    }

    public TimeRangeRuleSettings Clone()
    {
        var clone = new TimeRangeRuleSettings { StartTime = StartTime, EndTime = EndTime };
        for (var i = 0; i < Weekdays.Count; i++)
        {
            clone.Weekdays[i].IsSelected = Weekdays[i].IsSelected;
        }

        return clone;
    }
}

/// <summary>时间/星期条件里的一个可选星期。</summary>
public partial class WeekdayOption(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty] private bool _isSelected = true;
}

/// <summary>规则的注册信息。</summary>
public sealed class RuleRegistryInfo(string id, string name, FASymbol icon = FASymbol.Help)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>规则图标。</summary>
    public FASymbol Icon { get; } = icon;

    /// <summary>规则设置类型（无设置的规则为 null）。</summary>
    public Type? SettingsType { get; init; }

    /// <summary>规则设置控件的类型（无设置界面时为 null）。</summary>
    public Type? SettingsControlType { get; init; }

    /// <summary>规则处理程序：返回 true/false，null 表示无法判定。</summary>
    public Func<object?, bool?>? Handle { get; init; }
}