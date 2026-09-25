using FluentAvalonia.UI.Controls;

namespace Player.App.Rules;

/// <summary>
/// 可用规则的注册表。结构与 ClassIsland 的 IRulesetService.Rules 一致：
/// 规则 ID → 注册信息（名称、图标、设置类型、处理程序）。
/// </summary>
public static class PlayerRuleRegistry
{
    private static readonly Dictionary<string, RuleRegistryInfo> RegisteredRules = [];

    static PlayerRuleRegistry()
    {
        Register(new RuleRegistryInfo("player.playback.playing", "正在播放", FASymbol.Play)
        {
            Handle = _ => PlayerRuleContext.IsPlaying,
        });
        Register(new RuleRegistryInfo("player.playback.paused", "已暂停", FASymbol.Pause)
        {
            Handle = _ => PlayerRuleContext.IsPaused,
        });
        Register(new RuleRegistryInfo("player.media.title", "媒体标题匹配", FASymbol.Video)
        {
            SettingsType = typeof(StringMatchingSettings),
            Handle = settings => settings is StringMatchingSettings match && PlayerRuleContext.MediaTitle is { } title &&
                                 match.IsMatching(title),
        });
        Register(new RuleRegistryInfo("player.time.range", "当前时间在指定时段", FASymbol.Clock)
        {
            SettingsType = typeof(TimeRangeRuleSettings),
            Handle = settings => settings is TimeRangeRuleSettings time && time.IsMatching(PlayerRuleContext.Now),
        });
        Register(new RuleRegistryInfo("player.test.true", "总是为真", FASymbol.Accept) { Handle = _ => true });
        Register(new RuleRegistryInfo("player.test.false", "总是为假", FASymbol.Cancel) { Handle = _ => false });
    }

    /// <summary>全部已注册规则（键为规则 ID）。</summary>
    public static IReadOnlyDictionary<string, RuleRegistryInfo> Rules => RegisteredRules;

    /// <summary>全部已注册规则（编辑器里的下拉框用）。</summary>
    public static IReadOnlyList<RuleRegistryInfo> All { get; } = [.. RegisteredRules.Values];

    private static void Register(RuleRegistryInfo info) => RegisteredRules.Add(info.Id, info);

    /// <summary>按规则 ID 创建一个默认设置对象；该规则没有设置时返回 null。</summary>
    public static object? CreateSettings(string ruleId) =>
        RegisteredRules.TryGetValue(ruleId, out var info) && info.SettingsType is { } type
            ? Activator.CreateInstance(type)
            : null;
}