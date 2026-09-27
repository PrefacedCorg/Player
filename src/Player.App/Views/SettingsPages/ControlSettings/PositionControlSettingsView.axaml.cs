using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Player.App.Views.SettingsPages.ControlSettings;

/// <summary>进度条组件的设置视图：左右时间显示模式 + 精度双头滑块 + 实时格式预览。</summary>
public partial class PositionControlSettingsView : UserControl
{
    /// <summary>预览样例：1:01:01.500（1 小时 1 分 1.5 秒），能同时看出各级单位与补零效果。</summary>
    private static readonly TimeSpan Sample = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1.5);

    private INotifyPropertyChanged? _subscribed;

    public PositionControlSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnSettingsPropertyChanged;
        }

        _subscribed = DataContext as INotifyPropertyChanged;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnSettingsPropertyChanged;
        }

        UpdatePreview();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PositionControlSettings.PrecisionStart) or nameof(PositionControlSettings.PrecisionEnd))
        {
            UpdatePreview();
        }
    }

    /// <summary>用样例时长按当前精度渲染，直观看到格式效果。</summary>
    private void UpdatePreview()
    {
        if (DataContext is PositionControlSettings settings)
        {
            FormatPreview.Text = $"预览：{PositionControlSettings.FormatTime(Sample, settings.PrecisionStart, settings.PrecisionEnd)}";
        }
    }
}
