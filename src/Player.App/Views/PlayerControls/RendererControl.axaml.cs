using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Player.App.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 控制栏的「渲染器」控件：选择 Native / OpenGl / Software，切换立即重建渲染视图。
/// 左侧标签按「全局设置 + 组件高级设置」合成的显示形式切换图标 / 文字（下拉框本身不变）。
/// </summary>
public partial class RendererControl : UserControl
{
    private readonly PlayerControlDisplayModeTracker _tracker;

    public RendererControl()
    {
        InitializeComponent();
        LabelIcon.Data = StreamGeometry.Parse(PlayerControlIcons.Renderer);
        _tracker = new PlayerControlDisplayModeTracker(this, ApplyDisplayMode);
        AttachedToVisualTree += (_, _) =>
        {
            _tracker.Attach();
            ApplyDisplayMode();
        };
        DetachedFromVisualTree += (_, _) => _tracker.Detach();
    }

    private void ApplyDisplayMode()
    {
        var icon = _tracker.Current == ControlDisplayMode.Icon;
        LabelText.IsVisible = !icon;
        LabelIcon.IsVisible = icon;
    }
}
