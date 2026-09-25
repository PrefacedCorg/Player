namespace Player.App.Rules;

/// <summary>
/// 规则集求值服务：判断规则集/规则组/单条规则是否满足，并在状态变化时通知订阅者刷新。
/// 求值逻辑与 ClassIsland 的 RulesetService 一致（组内 And/Or、非、三态状态）。
/// </summary>
public static class PlayerRuleService
{
    /// <summary>规则状态可能发生变化时触发（播放状态、媒体、时间等）。</summary>
    public static event EventHandler? StatusUpdated;

    /// <summary>判断一个规则集是否满足。</summary>
    public static bool IsRulesetSatisfied(Ruleset ruleset)
    {
        var isSatisfied = ruleset.Mode == RulesetLogicalMode.And;
        if (ruleset.Groups.Count <= 0)
        {
            ruleset.State = (int)RuleState.NotSatisfied;
            return false;
        }

        foreach (var group in ruleset.Groups)
        {
            group.State = (int)RuleState.Unknown;
            foreach (var rule in group.Rules)
            {
                rule.State = (int)RuleState.Unknown;
            }
        }

        foreach (var group in ruleset.Groups.Where(static x => x.IsEnabled))
        {
            var result = IsRulesetGroupSatisfied(group);
            group.State = ToState(result);
            if (result == null)
            {
                continue;
            }

            if (result == false && ruleset.Mode == RulesetLogicalMode.And)
            {
                isSatisfied = false;
                break;
            }

            if (result == true && ruleset.Mode == RulesetLogicalMode.Or)
            {
                isSatisfied = true;
                break;
            }
        }

        isSatisfied ^= ruleset.IsReversed;
        ruleset.State = (int)(isSatisfied ? RuleState.Satisfied : RuleState.NotSatisfied);
        return isSatisfied;
    }

    private static bool? IsRulesetGroupSatisfied(RuleGroup group)
    {
        var isSatisfied = group.Mode == RulesetLogicalMode.And;
        if (group.Rules.Count(static rule => rule.Id != "") <= 0)
        {
            return null;
        }

        foreach (var rule in group.Rules)
        {
            var result = IsRuleSatisfied(rule);
            if (result == null)
            {
                rule.State = (int)RuleState.Unknown;
                continue;
            }

            var value = (bool)result;
            value ^= rule.IsReversed;
            rule.State = (int)(value ? RuleState.Satisfied : RuleState.NotSatisfied);
            if (value == false && group.Mode == RulesetLogicalMode.And)
            {
                isSatisfied = false;
                break;
            }

            if (value && group.Mode == RulesetLogicalMode.Or)
            {
                isSatisfied = true;
                break;
            }
        }

        isSatisfied ^= group.IsReversed;
        return isSatisfied;
    }

    private static bool? IsRuleSatisfied(Rule rule)
    {
        if (rule.Id == "")
        {
            return null;
        }

        if (!PlayerRuleRegistry.Rules.TryGetValue(rule.Id, out var info))
        {
            return false;
        }

        var settings = rule.Settings;
        if (settings == null && info.SettingsType is { } type)
        {
            settings = Activator.CreateInstance(type);
            rule.Settings = settings;
        }

        try
        {
            return info.Handle?.Invoke(settings) ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>通知所有订阅者重新求值。</summary>
    public static void NotifyStatusChanged() => StatusUpdated?.Invoke(null, EventArgs.Empty);

    private static int ToState(bool? value) => (int)(value switch
    {
        true => RuleState.Satisfied,
        false => RuleState.NotSatisfied,
        _ => RuleState.Unknown,
    });
}