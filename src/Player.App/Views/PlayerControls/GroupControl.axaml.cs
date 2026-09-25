using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>分组容器（对应 ClassIsland 的 GroupComponent）：把容器里的控件横向排成一组。</summary>
public partial class GroupControl : UserControl
{
    // 无参构造让 XAML 运行时加载器也能创建（AVLN3001）；工厂走下面那个带子控件的重载。
    public GroupControl()
    {
        InitializeComponent();
    }

    public GroupControl(IReadOnlyList<Control> children) : this()
    {
        foreach (var child in children)
        {
            Root.Children.Add(child);
        }
    }
}