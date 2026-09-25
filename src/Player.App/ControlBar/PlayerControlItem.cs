using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using Player.App.Rules;

namespace Player.App;

/// <summary>
/// 控制栏中的一个控件项：种类 + 名称 + 外观设置，容器型控件通过 <see cref="Children"/> 承载子控件。
/// 它出现在控制栏布局列表里就表示"已放置"，<see cref="IsVisible"/> 由规则求值在运行时刷新。
/// </summary>
public partial class PlayerControlItem : ObservableObject
{
    public PlayerControlItem(PlayerControlKind kind, string title, PlayerControlSettings? settings = null)
    {
        Kind = kind;
        Title = title;
        Settings = settings ?? PlayerControlCatalog.CreateSettings(kind);
        IconSource = new FASymbolIconSource { Symbol = PlayerControlCatalog.SymbolFor(kind), FontSize = 14 };
    }

    /// <summary>唯一标识（高级设置里展示，也用于排查配置）。</summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    public PlayerControlKind Kind { get; }

    /// <summary>显示名称（设置页与组件库用）。</summary>
    public string Title { get; }

    /// <summary>
    /// 图标（设置页已放置行用，14x14）。
    /// 字号必须写在图标源上：FAIconSourceElement 生成子图标时是把 IconSource 的 FontSize
    /// 绑过去的（见 FluentAvalonia 的 FAIconHelpers.CreateSymbolIconFromSymbolIconSource），
    /// 元素上的 TextElement.FontSize 不起作用。
    /// </summary>
    public FASymbolIconSource IconSource { get; }

    /// <summary>该控件的外观/容器设置。容器型控件的子控件在 <see cref="Children"/>。</summary>
    public PlayerControlSettings Settings { get; }

    /// <summary>是否是容器型控件。</summary>
    public bool IsContainer => Settings is IPlayerContainerSettings;

    /// <summary>容器型控件的子控件列表；非容器控件为 null。</summary>
    public ObservableCollection<PlayerControlItem>? Children =>
        (Settings as IPlayerContainerSettings)?.Children;

    /// <summary>是否在规则满足时自动隐藏。</summary>
    [ObservableProperty] private bool _hideOnRule;

    /// <summary>隐藏规则集。</summary>
    public Ruleset HidingRules { get; } = new();

    /// <summary>运行时可见性（由规则求值刷新，不落盘）。</summary>
    [ObservableProperty] private bool _isVisible = true;

    /// <summary>深拷贝一份（右键菜单"创建副本"用）。</summary>
    public PlayerControlItem Clone()
    {
        var clone = new PlayerControlItem(Kind, Title, PlayerControlCatalog.CreateSettings(Kind));
        clone.Settings.CopyFrom(Settings);
        clone.HideOnRule = HideOnRule;
        clone.HidingRules.CopyFrom(HidingRules);
        if (clone.Children is { } children && Children is { } sourceChildren)
        {
            foreach (var child in sourceChildren)
            {
                children.Add(child.Clone());
            }
        }

        return clone;
    }
}