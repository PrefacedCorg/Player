using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>控制栏的「渲染器」控件：选择 Native / OpenGl / Software，切换立即重建渲染视图。</summary>
public partial class RendererControl : UserControl
{
    public RendererControl() => InitializeComponent();
}