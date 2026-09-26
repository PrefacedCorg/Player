using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>控制栏的「调试信息」控件：播放状态、起播时序与解码 / CPU 诊断（两行算一个组件）。</summary>
public partial class DebugInfoControl : UserControl
{
    public DebugInfoControl() => InitializeComponent();
}
