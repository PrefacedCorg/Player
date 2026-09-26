using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Player.App.Rules;

namespace Player.App.Controls;

/// <summary>
/// 控制栏控件的宿主（对应 ClassIsland 的 ComponentPresenter 的职责）：把 PlayerControlItem 上的
/// 不透明度/背景/圆角/宽度/边距/对齐/字号/前景色套在真正的控件外面，并按"按规则隐藏"的求值结果
/// 控制可见性。容器型控件的子控件也各自有一个宿主。
/// </summary>
public class PlayerControlHost : Border
{
    /// <summary>未自定义字体颜色时的文字默认前景色（与各控件原有的硬编码一致）。</summary>
    private static readonly IBrush DefaultForeground = new SolidColorBrush(Color.Parse("#F0F0F0"));

    /// <summary>未自定义字体颜色时的次要文字（标签一类）默认前景色。</summary>
    private static readonly IBrush DefaultSecondaryForeground = new SolidColorBrush(Color.Parse("#A8A8B0"));

    private readonly PlayerControlItem _item;
    private double _secondaryFontSize;
    private double _bodyFontSize;
    private double _emphasizedFontSize;
    private double _largeFontSize;

    public static readonly DirectProperty<PlayerControlHost, double> SecondaryFontSizeProperty =
        AvaloniaProperty.RegisterDirect<PlayerControlHost, double>(nameof(SecondaryFontSize), o => o.SecondaryFontSize);

    public static readonly DirectProperty<PlayerControlHost, double> BodyFontSizeProperty =
        AvaloniaProperty.RegisterDirect<PlayerControlHost, double>(nameof(BodyFontSize), o => o.BodyFontSize);

    public static readonly DirectProperty<PlayerControlHost, double> EmphasizedFontSizeProperty =
        AvaloniaProperty.RegisterDirect<PlayerControlHost, double>(nameof(EmphasizedFontSize), o => o.EmphasizedFontSize);

    public static readonly DirectProperty<PlayerControlHost, double> LargeFontSizeProperty =
        AvaloniaProperty.RegisterDirect<PlayerControlHost, double>(nameof(LargeFontSize), o => o.LargeFontSize);

    public PlayerControlHost(PlayerControlItem item)
    {
        _item = item;
        item.Settings.PropertyChanged += OnSettingsChanged;
        item.PropertyChanged += OnItemChanged;
        PlayerRuleService.StatusUpdated += OnRuleStatusUpdated;
        AttachedToVisualTree += (_, _) => RefreshRuleVisibility();
        DetachedFromVisualTree += (_, _) => Unsubscribe();

        ApplyAppearance();
        RefreshRuleVisibility();
    }

    /// <summary>控制栏控件项。</summary>
    public PlayerControlItem Item => _item;

    /// <summary>次级字号（时间点附加信息一类的小字）。</summary>
    public double SecondaryFontSize
    {
        get => _secondaryFontSize;
        private set => SetAndRaise(SecondaryFontSizeProperty, ref _secondaryFontSize, value);
    }

    /// <summary>正文字号（控件里的默认文字）。</summary>
    public double BodyFontSize
    {
        get => _bodyFontSize;
        private set => SetAndRaise(BodyFontSizeProperty, ref _bodyFontSize, value);
    }

    /// <summary>强调字号（主要信息）。</summary>
    public double EmphasizedFontSize
    {
        get => _emphasizedFontSize;
        private set => SetAndRaise(EmphasizedFontSizeProperty, ref _emphasizedFontSize, value);
    }

    /// <summary>大号字号（标题一类）。</summary>
    public double LargeFontSize
    {
        get => _largeFontSize;
        private set => SetAndRaise(LargeFontSizeProperty, ref _largeFontSize, value);
    }

    /// <summary>把设置里的外观项应用到宿主要上。</summary>
    private void ApplyAppearance()
    {
        var settings = _item.Settings;

        Opacity = settings.Opacity;
        Background = settings.IsCustomBackgroundColorEnabled ? CreateBackgroundBrush(settings) : null;
        CornerRadius = settings.IsCustomCornerRadiusEnabled ? new CornerRadius(settings.CustomCornerRadius) : default;
        Width = settings.IsFixedWidthEnabled ? settings.FixedWidth : double.NaN;
        MinWidth = settings.IsMinWidthEnabled ? settings.MinWidth : 0;
        MaxWidth = settings.IsMaxWidthEnabled ? settings.MaxWidth : double.PositiveInfinity;
        Margin = settings.IsCustomMarginEnabled
            ? new Thickness(settings.MarginLeft, settings.MarginTop, settings.MarginRight, settings.MarginBottom)
            : default;

        // 对齐方式参与行面板的整行排布（居左/居中/居右/拉伸分组）：改它除了改自身，
        // 还要让父面板重新测量——Avalonia 的对齐属性只影响自身排列，不会触发父面板
        // 重新 Arrange，行面板不重排的话对齐怎么调都不动。
        if (HorizontalAlignment != settings.HorizontalAlignment)
        {
            HorizontalAlignment = settings.HorizontalAlignment;
            InvalidateMeasure();
        }

        SecondaryFontSize = settings.SecondaryFontSize;
        BodyFontSize = settings.BodyFontSize;
        EmphasizedFontSize = settings.EmphasizedFontSize;
        LargeFontSize = settings.LargeFontSize;

        // 字号/前景色作用在宿主上，靠 TextElement 的继承性传给控件内的文本；
        // 未启用覆盖时清掉，避免影响相邻控件。
        if (settings.IsResourceOverridingEnabled)
        {
            SetValue(TextElement.FontSizeProperty, settings.BodyFontSize);
        }
        else
        {
            ClearValue(TextElement.FontSizeProperty);
        }

        if (settings.IsCustomForegroundColorEnabled)
        {
            SetValue(TextElement.ForegroundProperty, new SolidColorBrush(settings.ForegroundColor));
        }
        else
        {
            ClearValue(TextElement.ForegroundProperty);
        }

        // 字号与前景色同时以资源形式挂到宿主上：控件里的文本用 {DynamicResource ...} 取用。
        // 只靠 TextElement 的继承会被控件自己的显式值盖掉（按钮模板、TextBlock 上的硬编码），
        // 走资源才能让"字体大小/字体颜色"真正作用到控制栏控件上。
        Resources["PlayerControlSecondaryFontSize"] = settings.SecondaryFontSize;
        Resources["PlayerControlBodyFontSize"] = settings.BodyFontSize;
        Resources["PlayerControlEmphasizedFontSize"] = settings.EmphasizedFontSize;
        Resources["PlayerControlLargeFontSize"] = settings.LargeFontSize;
        var foreground = settings.IsCustomForegroundColorEnabled
            ? new SolidColorBrush(settings.ForegroundColor)
            : DefaultForeground;
        Resources["PlayerControlForeground"] = foreground;
        Resources["PlayerControlSecondaryForeground"] =
            settings.IsCustomForegroundColorEnabled ? foreground : DefaultSecondaryForeground;
    }

    private static IBrush CreateBackgroundBrush(PlayerControlSettings settings)
    {
        var color = settings.BackgroundColor;
        if (settings.IsCustomBackgroundOpacityEnabled)
        {
            color = Color.FromArgb((byte)Math.Clamp(settings.BackgroundOpacity * 255d, 0d, 255d), color.R, color.G, color.B);
        }

        return new SolidColorBrush(color);
    }

    /// <summary>按隐藏规则刷新可见性。</summary>
    private void RefreshRuleVisibility()
    {
        var hide = _item.HideOnRule && PlayerRuleService.IsRulesetSatisfied(_item.HidingRules);
        _item.IsVisible = !hide;
        IsVisible = !hide;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ApplyAppearance();

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerControlItem.HideOnRule))
        {
            RefreshRuleVisibility();
        }
    }

    private void OnRuleStatusUpdated(object? sender, EventArgs e) => RefreshRuleVisibility();

    private void Unsubscribe()
    {
        _item.Settings.PropertyChanged -= OnSettingsChanged;
        _item.PropertyChanged -= OnItemChanged;
        PlayerRuleService.StatusUpdated -= OnRuleStatusUpdated;
    }
}