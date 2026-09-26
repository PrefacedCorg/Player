using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Player.App.Controls;

/// <summary>
/// 控制栏行面板：把「居中」解释为<b>整行居中</b>，而不是排在左右内容之间的剩余空间里。
/// <para>
/// 排列规则（水平方向，各按 <see cref="HorizontalAlignment"/> 分组、保持相对顺序）：
/// 「居左」的子项从行首向右排；「居右」的从行尾向左排；「居中」的子项连续打包后
/// <b>整体中心对准整行的水平中心</b>——左右两侧内容增减、宽度变化都不会带动中间偏移，
/// 所以中间那组永远在正中（要「暂停/播放」精确居中，把居中组排成左右对称即可，如
/// 上一个 · 回退 · 暂停/播放 · 快进 · 下一个）。「拉伸」的子项占满整行（进度条、
/// 调试信息独占一行就靠它，会与其它子项叠放）。
/// </para>
/// <para>
/// 内容过宽时中间可能与两侧重叠（左右包夹进来），这与网页播放器控制条的绝对居中行为一致。
/// </para>
/// </summary>
public class PlayerControlLinePanel : Panel
{
    /// <summary>同侧相邻子项之间的间距。</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PlayerControlLinePanel, double>(nameof(Spacing), 16d);

    /// <summary>同侧相邻子项之间的间距。</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        foreach (var child in Children)
        {
            // 无限宽度测自然宽度（拉伸项也先按内容测，排列时再放大到整行）
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        // 宽度返回 0：行总是占满控制栏宽度，最终宽度由 Arrange 给出
        return new Size(0, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = new List<Control>();
        var center = new List<Control>();
        var right = new List<Control>();
        foreach (var child in Children)
        {
            switch (child.HorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    left.Add(child);
                    break;
                case HorizontalAlignment.Right:
                    right.Add(child);
                    break;
                case HorizontalAlignment.Center:
                    center.Add(child);
                    break;
                default:
                    // 拉伸：占满整行（与其它子项叠放；进度条/调试信息独占一行就靠它）
                    child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                    break;
            }
        }

        // 居左：从行首向右排
        var x = 0d;
        foreach (var child in left)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        // 居右：从行尾向左排，保持相对顺序（列表里第一个还是最靠右）
        x = finalSize.Width;
        foreach (var child in right)
        {
            x -= child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x -= Spacing;
        }

        // 居中：连续打包，整体中心 = 整行的水平中心（左右两侧内容宽度变化不影响这里）
        var total = center.Count > 0
            ? center.Sum(c => c.DesiredSize.Width) + Spacing * (center.Count - 1)
            : 0d;
        x = (finalSize.Width - total) / 2;
        foreach (var child in center)
        {
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width + Spacing;
        }

        return finalSize;
    }
}
