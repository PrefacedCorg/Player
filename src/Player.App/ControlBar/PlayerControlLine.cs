using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Player.App;

/// <summary>
/// 控制栏中的一行（照抄 ClassIsland-2.0 主界面行 MainWindowLineSettings 的结构）：
/// 行本身只持一个控件列表，行内的控件横向排列，控制栏宿主把多行从上到下排开。
/// 行可以增删，控件可以在行间拖动。
/// </summary>
public partial class PlayerControlLine : ObservableObject
{
    /// <summary>这一行里的控件，顺序即左右顺序。</summary>
    public ObservableCollection<PlayerControlItem> Children { get; } = [];
}
