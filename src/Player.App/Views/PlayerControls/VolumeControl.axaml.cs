using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>控制栏的「音量」控件：滑块上限 200%（与引擎侧一致）+ 实时百分比。</summary>
public partial class VolumeControl : UserControl
{
    public VolumeControl() => InitializeComponent();
}