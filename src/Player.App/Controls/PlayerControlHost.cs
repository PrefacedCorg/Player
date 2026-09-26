using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using Player.App.Rules;

namespace Player.App.Controls;

/// <summary>
/// 控制栏控件的宿主（对应 ClassIsland 的 ComponentPresenter 的职责）：把 PlayerControlItem 上的
/// 不透明度/背景/圆角/宽度/边距/对齐/字号/前景色套在真正的控件外面，并按"按规则隐藏"的求值结果
/// 控制可见性。容器型控件的子控件也各自有一个宿主。
/// <para>
/// 用 <see cref="ContentControl"/> 而不是 Border：对齐方式是<b>组件内部</b>的对齐（ClassIsland 同款）——
/// 开了固定宽度时内容在这个组件的区域里靠左/中/右，没开固定宽度时组件宽=内容宽，对齐没有可见效果。
/// Border 会把子内容拉满没有内容对齐概念，ContentControl 的 HorizontalContentAlignment 才承载得了这个语义。
/// </para>
/// </summary>
public class PlayerControlHost : ContentControl
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

        // 对齐方式是组件内部的对齐（ClassIsland 同款）：开了固定宽度时内容在这个组件里
        // 靠左/中/右；没开固定宽度时组件宽=内容宽，对齐没有可见效果。行内的整行排布
        // （左右/左中右分区）由「对齐分割线」组件负责，跟这个属性无关。
        HorizontalContentAlignment = settings.HorizontalAlignment;

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

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyAppearance();

        // 「列内填充」等影响行面板排布的设置变化时，沿视觉树找到所在行面板让它重排——
        // 改属性只影响组件自身，不会自动触发父面板的 ArrangeOverride
        for (var visual = (Visual)this; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is PlayerControlLinePanel panel)
            {
                panel.InvalidateArrange();
                break;
            }
        }
    }

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