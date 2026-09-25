using Avalonia.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>堆叠容器（对应 ClassIsland 的 StackComponent）：把容器里的控件叠放在一起。</summary>
public partial class StackControl : UserControl
{
    // 无参构造让 XAML 运行时加载器也能创建（AVLN3001）；工厂走下面那个带子控件的重载。
    public StackControl()
    {
        InitializeComponent();
    }

    public StackControl(IReadOnlyList<Control> children) : this()
    {
        foreach (var child in children)
        {
            Root.Children.Add(child);
        }
    }
}