using System.Collections.ObjectModel;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Player.App;

/// <summary>
/// 容器型控件的设置所实现的接口：提供子控件列表。非容器控件的设置不实现此接口。
/// </summary>
public interface IPlayerContainerSettings
{
    ObservableCollection<PlayerControlItem> Children { get; }
}

/// <summary>
/// 控制栏控件的外观设置（对应 ClassIsland 的 ComponentSettings 里的资源/外观/布局部分）。
/// 容器型控件（轮播/滚动/分组/堆叠）用派生类追加自己的设置，并实现 <see cref="IPlayerContainerSettings"/>。
/// </summary>
public partial class PlayerControlSettings : ObservableObject
{
    /// <summary>对齐方式候选项（界面上是按枚举值直接绑定的下拉框）。</summary>
    public static IReadOnlyList<HorizontalAlignment> HorizontalAlignmentOptions { get; } =
        [HorizontalAlignment.Left, HorizontalAlignment.Center, HorizontalAlignment.Right, HorizontalAlignment.Stretch];

    #region 字体

    [ObservableProperty] private bool _isResourceOverridingEnabled;
    [ObservableProperty] private double _secondaryFontSize = 12;
    [ObservableProperty] private double _bodyFontSize = 15;
    [ObservableProperty] private double _emphasizedFontSize = 18;
    [ObservableProperty] private double _largeFontSize = 20;
    [ObservableProperty] private bool _isCustomForegroundColorEnabled;
    [ObservableProperty] private Color _foregroundColor = Colors.DodgerBlue;

    #endregion

    #region 外观

    [ObservableProperty] private double _opacity = 1.0;
    [ObservableProperty] private bool _isCustomBackgroundColorEnabled;
    [ObservableProperty] private Color _backgroundColor = Colors.Black;
    [ObservableProperty] private bool _isCustomBackgroundOpacityEnabled;
    [ObservableProperty] private double _backgroundOpacity = 0.5;
    [ObservableProperty] private bool _isCustomCornerRadiusEnabled;
    [ObservableProperty] private double _customCornerRadius = 8.0;

    #endregion

    #region 布局

    [ObservableProperty] private HorizontalAlignment _horizontalAlignment = HorizontalAlignment.Stretch;
    [ObservableProperty] private bool _isColumnFillEnabled;
    [ObservableProperty] private bool _isFixedWidthEnabled;
    [ObservableProperty] private double _fixedWidth = 200;
    [ObservableProperty] private bool _isMinWidthEnabled;
    [ObservableProperty] private double _minWidth = 100;
    [ObservableProperty] private bool _isMaxWidthEnabled;
    [ObservableProperty] private double _maxWidth = 300;
    [ObservableProperty] private bool _isCustomMarginEnabled;
    [ObservableProperty] private double _marginLeft;
    [ObservableProperty] private double _marginTop;
    [ObservableProperty] private double _marginRight;
    [ObservableProperty] private double _marginBottom;

    #endregion

    /// <summary>把一个设置对象的基础项复制到当前对象（创建副本用）。</summary>
    public virtual void CopyFrom(PlayerControlSettings source)
    {
        IsResourceOverridingEnabled = source.IsResourceOverridingEnabled;
        SecondaryFontSize = source.SecondaryFontSize;
        BodyFontSize = source.BodyFontSize;
        EmphasizedFontSize = source.EmphasizedFontSize;
        LargeFontSize = source.LargeFontSize;
        IsCustomForegroundColorEnabled = source.IsCustomForegroundColorEnabled;
        ForegroundColor = source.ForegroundColor;
        Opacity = source.Opacity;
        IsCustomBackgroundColorEnabled = source.IsCustomBackgroundColorEnabled;
        BackgroundColor = source.BackgroundColor;
        IsCustomBackgroundOpacityEnabled = source.IsCustomBackgroundOpacityEnabled;
        BackgroundOpacity = source.BackgroundOpacity;
        IsCustomCornerRadiusEnabled = source.IsCustomCornerRadiusEnabled;
        CustomCornerRadius = source.CustomCornerRadius;
        HorizontalAlignment = source.HorizontalAlignment;
        IsColumnFillEnabled = source.IsColumnFillEnabled;
        IsFixedWidthEnabled = source.IsFixedWidthEnabled;
        FixedWidth = source.FixedWidth;
        IsMinWidthEnabled = source.IsMinWidthEnabled;
        MinWidth = source.MinWidth;
        IsMaxWidthEnabled = source.IsMaxWidthEnabled;
        MaxWidth = source.MaxWidth;
        IsCustomMarginEnabled = source.IsCustomMarginEnabled;
        MarginLeft = source.MarginLeft;
        MarginTop = source.MarginTop;
        MarginRight = source.MarginRight;
        MarginBottom = source.MarginBottom;
    }
}

/// <summary>轮播容器的设置：定时切换显示容器内的子控件。</summary>
public partial class SlideControlSettings : PlayerControlSettings, IPlayerContainerSettings
{
    /// <summary>每个子控件停留的秒数。</summary>
    [ObservableProperty] private double _slideSeconds = 5;

    /// <summary>轮播模式：0 循环、1 随机、2 往复。</summary>
    [ObservableProperty] private int _slideMode;

    [System.Text.Json.Serialization.JsonIgnore]
    public ObservableCollection<PlayerControlItem> Children { get; } = [];

    public override void CopyFrom(PlayerControlSettings source)
    {
        base.CopyFrom(source);
        if (source is SlideControlSettings slide)
        {
            SlideSeconds = slide.SlideSeconds;
            SlideMode = slide.SlideMode;
        }
    }
}

/// <summary>滚动容器的设置：子控件按像素速度横向滚动（跑马灯）。</summary>
public partial class RollingControlSettings : PlayerControlSettings, IPlayerContainerSettings
{
    /// <summary>滚动速度（像素/秒）。</summary>
    [ObservableProperty] private double _speedPixelPerSecond = 40;

    /// <summary>滚动到末尾后是否暂停。</summary>
    [ObservableProperty] private bool _isPauseEnabled = true;

    /// <summary>末尾暂停的秒数。</summary>
    [ObservableProperty] private double _pauseSeconds = 2;

    /// <summary>开始滚动前的初始偏移（像素）。</summary>
    [ObservableProperty] private double _pauseOffsetX;

    [System.Text.Json.Serialization.JsonIgnore]
    public ObservableCollection<PlayerControlItem> Children { get; } = [];

    public override void CopyFrom(PlayerControlSettings source)
    {
        base.CopyFrom(source);
        if (source is RollingControlSettings rolling)
        {
            SpeedPixelPerSecond = rolling.SpeedPixelPerSecond;
            IsPauseEnabled = rolling.IsPauseEnabled;
            PauseSeconds = rolling.PauseSeconds;
            PauseOffsetX = rolling.PauseOffsetX;
        }
    }
}

/// <summary>分组容器的设置：子控件在容器内横向排开。</summary>
public partial class GroupControlSettings : PlayerControlSettings, IPlayerContainerSettings
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ObservableCollection<PlayerControlItem> Children { get; } = [];
}

/// <summary>
/// 对齐分割线的设置：左右两列的宽度比例（Grid 的 2*/1* 语义）。
/// 一根分割线时「左列比例:右列比例」两个都用；两根时第一根的左比例管左列、
/// 第二根的右比例管右列（中间列是 Auto 自适应，不吃比例）。
/// </summary>
public partial class DividerControlSettings : PlayerControlSettings
{
    /// <summary>左列宽度比例（默认 1 = 1*）。</summary>
    [ObservableProperty] private double _leftWeight = 1d;

    /// <summary>右列宽度比例（默认 1 = 1*）。</summary>
    [ObservableProperty] private double _rightWeight = 1d;

    public override void CopyFrom(PlayerControlSettings source)
    {
        base.CopyFrom(source);
        if (source is DividerControlSettings divider)
        {
            LeftWeight = divider.LeftWeight;
            RightWeight = divider.RightWeight;
        }
    }
}

/// <summary>堆叠容器的设置：子控件在容器内叠放。</summary>
public partial class StackControlSettings : PlayerControlSettings, IPlayerContainerSettings
{
    [System.Text.Json.Serialization.JsonIgnore]
    public ObservableCollection<PlayerControlItem> Children { get; } = [];
}