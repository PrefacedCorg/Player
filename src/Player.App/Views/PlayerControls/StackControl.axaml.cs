using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>堆叠容器（对应 ClassIsland 的 StackComponent）：把容器里的控件叠放在一起。</summary>
public partial class StackControl : UserControl
{
    public StackControl(IReadOnlyList<Control> children)
    {
        InitializeComponent();
        foreach (var child in children)
        {
            Root.Children.Add(child);
        }
    }
}