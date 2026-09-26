using Avalonia.Controls;
using Player.App.Controls;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 分组容器（对应 ClassIsland 的 GroupComponent）。内部用 <see cref="PlayerControlLinePanel"/>——
/// 与整行同一套布局规则：组内放「对齐分割线」即可做居左/居中/居右，开「列内填充」的子控件充满所在段。
/// </summary>
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
