namespace Player.App.Rules;

/// <summary>
/// 规则求值要用到的运行时状态。由主窗口 ViewModel 在播放状态/媒体变化时写入，
/// 规则处理程序（<see cref="PlayerRuleRegistry"/>）从这里读取。
/// </summary>
public static class PlayerRuleContext
{
    /// <summary>当前是否正在播放。</summary>
    public static bool IsPlaying { get; set; }

    /// <summary>当前是否处于暂停。</summary>
    public static bool IsPaused { get; set; }

    /// <summary>当前媒体的标题（未加载媒体时为 null）。</summary>
    public static string? MediaTitle { get; set; }

    /// <summary>当前时间（每次求值实时取，时间/星期规则用）。</summary>
    public static DateTime Now => DateTime.Now;
}