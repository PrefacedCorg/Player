using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>分组容器（对应 ClassIsland 的 GroupComponent）：把容器里的控件横向排成一组。</summary>
public partial class GroupControl : UserControl
{
    public GroupControl(IReadOnlyList<Control> children)
    {
        InitializeComponent();
        foreach (var child in children)
        {
            Root.Children.Add(child);
        }
    }
}