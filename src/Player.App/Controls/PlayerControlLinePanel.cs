using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Player.App.Controls;

/// <summary>
/// 控制栏行面板：按「对齐分割线」组件把一行分区，分割线根数对应整行的列定义：
/// <para>
/// · 无分割线（* 居中）：组件整组居中；行里只有一个组件时占满整行
///   （进度条、调试信息独占一行就靠这个）。
/// · 一根分割线（*,*）：左边的组件靠左，右边的靠右。
/// · 两根分割线（*,Auto,*）：第一根左边的靠左，两根中间的连续打包后整体压在<b>整行的
///   水平中心</b>（左右两列均分，中间列自适应——左右内容增减、宽度变化都不会带动中间
///   偏移；要「播放/暂停」精确居中就把中间排成左右对称，如 上一个 · 回退 · 播放/暂停 ·
///   快进 · 下一个），第二根右边的靠右。
/// · 一行最多两根，第三根起没有分区意义，当作宽 0 的项排在所在区里。
/// </para>
/// <para>
/// 高级设置里的「对齐方式」与行布局无关——那是组件内部的对齐（固定宽度时内容在这个
/// 组件里靠左/中/右，ClassIsland 同款）。内容过宽时中间可能与两侧重叠（左右包夹进来），
/// 这与网页播放器控制条的绝对居中行为一致。
/// </para>
/// </summary>
public class PlayerControlLinePanel : Panel
{
    /// <summary>同区相邻子项之间的间距（与分组容器内部的 8 保持一致）。</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<PlayerControlLinePanel, double>(nameof(Spacing), 8d);

    /// <summary>同区相邻子项之间的间距。</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// 中列（两根分割线之间）的居中语义：整行用绝对居中（压整行几何中心，左右内容宽度变化
    /// 不影响）；分组容器内用内容之间居中（左右子项到中列的间隙相等）。默认绝对居中。
    /// </summary>
    public static readonly StyledProperty<bool> CenterBetweenContentProperty =
        AvaloniaProperty.Register<PlayerControlLinePanel, bool>(nameof(CenterBetweenContent));

    /// <summary>中列在左右列内容之间居中（两侧间隙相等），而不是压容器几何中心。</summary>
    public bool CenterBetweenContent
    {
        get => GetValue(CenterBetweenContentProperty);
        set => SetValue(CenterBetweenContentProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0d;
        var width = 0d;
        var counted = 0;
        foreach (var child in Children)
        {
            // 无限宽度测自然宽度（充满的组件排列时再放大到所在段）
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            // 宽 0 的子项（对齐分割线）不参与间距：分组自然宽 = 内容依次排的总宽，
            // 否则每根分割线都会凭空贡献两侧间距，把容器撑得比内容宽
            if (child.DesiredSize.Width > 0)
            {
                width += child.DesiredSize.Width;
                counted++;
            }
        }

        // 期望宽度 = 内容依次排的总宽。行场景（纵向 StackPanel 的子项）Arrange 时仍会被拉满整行；
        // 分组场景（按内容自然宽测量）靠这个得到真实宽度——返回 0 会让分组整个塌掉
        return new Size(width + Spacing * Math.Max(0, counted - 1), height);
    }

    /// <summary>只有「对齐分割线」的宿主参与分区，其余子项都是普通组件。</summary>
    private static bool IsDivider(Control control) =>
        control is PlayerControlHost { Item.Kind: PlayerControlKind.Divider };

    /// <summary>这个组件开了高级设置里的「列内填充」（在左右两列里充满它占的那一段）。</summary>
    private static bool FillsColumn(Control control) =>
        control is PlayerControlHost { Item.Settings.IsColumnFillEnabled: true };

    /// <summary>分割线的左右列宽度比例（Grid 的 2*/1* 语义），默认 1:1。</summary>
    private static (double Left, double Right) DividerWeights(PlayerControlHost divider) =>
        divider.Item.Settings is DividerControlSettings settings
            ? (Math.Max(0.01, settings.LeftWeight), Math.Max(0.01, settings.RightWeight))
            : (1d, 1d);

    protected override Size ArrangeOverride(Size finalSize)
    {
        // 分割线把子项切成最多三个区（第三根起不再切区）；空区保留占位（比如一根分割线
        // 左边没放组件，右边那区照样靠右），只是没有内容可排
        var zones = new List<List<Control>> { new() };
        var dividers = new List<PlayerControlHost>();
        foreach (var child in Children)
        {
            if (IsDivider(child))
            {
                // 分割线本身不显示也不占位（宽 0），但仍要 Arrange 过，否则布局不完整
                child.Arrange(new Rect(0, 0, 0, finalSize.Height));
                if (dividers.Count < 2)
                {
                    zones.Add([]);
                    dividers.Add((PlayerControlHost)child);
                }
                continue;
            }

            zones[^1].Add(child);
        }

        switch (zones.Count)
        {
            case 1:
                // 无分割线（* 居中）：整组居中；行里只有一个组件时占满整行
                //（进度条、调试信息独占一行靠这个）
                if (zones[0].Count == 1)
                {
                    zones[0][0].Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
                }
                else
                {
                    ArrangeCentered(zones[0], finalSize.Width);
                }
                break;

            case 2:
                // 一根分割线（左*:右*）：按分割线的左右比例分列，默认 1:1 均分
                {
                    var (left, right) = DividerWeights(dividers[0]);
                    var leftWidth = finalSize.Width * left / (left + right);
                    ArrangeSide(zones[0], 0, leftWidth, fromEnd: false);
                    ArrangeSide(zones[1], leftWidth, finalSize.Width - leftWidth, fromEnd: true);
                    break;
                }

            default:
                // 两根分割线（左*,Auto,右*）：左右两列按 第一根的左比例 : 第二根的右比例
                // 分掉整行减去中区与两侧间距的剩余宽度，中区整体压在整行的水平中心。
                // 中列的居中位置被夹在左右列内容之间（至少各留一个间距）——分组自然宽时
                // 正好退化成依次排的等间距，有富余时照常压中心
                {
                    var (left, _) = DividerWeights(dividers[0]);
                    var (_, right) = DividerWeights(dividers[1]);
                    var side = Math.Max(0, finalSize.Width - ZoneWidth(zones[1]) - Spacing * 2);
                    var leftWidth = side * left / (left + right);
                    var rightColumnStart = finalSize.Width - (side - leftWidth);
                    var leftEdge = zones[0].Any(FillsColumn) ? leftWidth : ZoneWidth(zones[0]);
                    var rightEdge = zones[2].Any(FillsColumn) ? rightColumnStart : finalSize.Width - ZoneWidth(zones[2]);
                    ArrangeSide(zones[0], 0, leftWidth, fromEnd: false);
                    ArrangeCentered(zones[1], finalSize.Width, leftEdge + Spacing, rightEdge - Spacing);
                    ArrangeSide(zones[2], rightColumnStart, side - leftWidth, fromEnd: true);
                    break;
                }
        }

        return finalSize;

        double ZoneWidth(List<Control> zone) => zone.Count > 0
            ? zone.Sum(c => c.DesiredSize.Width) + Spacing * (zone.Count - 1)
            : 0d;

        // Star 列，统一模型（左右对称）：
        // · 列内没有开「列内填充」的组件 → 整块贴行边依次排（右列靠行尾、左列靠行首），顺序即从左到右
        // · 有（每列第一个开的生效）→ 中区侧的自然宽贴列边依次排，填充组件充满中间剩余段，行边侧的贴行边依次排
        void ArrangeSide(List<Control> zone, double start, double width, bool fromEnd)
        {
            var fillIndex = zone.FindIndex(FillsColumn);
            if (fillIndex < 0)
            {
                ArrangeSequence(zone, fromEnd ? start + width - ZoneWidth(zone) : start);
                return;
            }

            // 填充组件行边一侧的整块宽度（含与填充组件的间距）
            var outerWidth = fillIndex < zone.Count - 1
                ? ZoneWidth(zone.Skip(fillIndex + 1).ToList()) + Spacing
                : 0d;
            if (fromEnd)
            {
                // 右列：中区侧贴列左依次排 → 填充组件撑到行边整块左侧 → 行边整块贴行尾
                var x = start;
                for (var i = 0; i < fillIndex; i++)
                {
                    Arrange(zone[i], x);
                    x += zone[i].DesiredSize.Width + Spacing;
                }

                var fillEnd = start + width - outerWidth;
                zone[fillIndex].Arrange(new Rect(x, 0, Math.Max(0, fillEnd - x), finalSize.Height));
                ArrangeSequence(zone.Skip(fillIndex + 1).ToList(), fillEnd);
            }
            else
            {
                // 左列：行边整块贴行首 → 填充组件从前块右侧撑到中区侧尾块左 → 中区侧尾块贴列右
                var x = start;
                ArrangeSequence(zone.Take(fillIndex).ToList(), x);
                x += ZoneWidth(zone.Take(fillIndex).ToList()) + (fillIndex > 0 ? Spacing : 0d);
                var fillEnd = start + width - ZoneWidth(zone.Skip(fillIndex + 1).ToList());
                zone[fillIndex].Arrange(new Rect(x, 0, Math.Max(0, fillEnd - x), finalSize.Height));
                ArrangeSequence(zone.Skip(fillIndex + 1).ToList(), fillEnd);
            }

            return;

            // 从 x 开始向右依次排（组件顺序即从左到右的显示顺序）
            void ArrangeSequence(List<Control> children, double x)
            {
                foreach (var child in children)
                {
                    Arrange(child, x);
                    x += child.DesiredSize.Width + Spacing;
                }
            }

            void Arrange(Control child, double x) =>
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
        }

        // 中列的居中：默认（整行）压容器的几何中心——左右两区宽度变化不影响；
        // CenterBetweenContent（分组容器内）在左右列内容之间居中——两侧间隙相等，
        // 左右子项宽度不同时也不偏。minStart/maxStart 把位置夹在左右列内容之间
        //（至少各留一个间距），内容过宽时从约束下界开始排
        void ArrangeCentered(List<Control> zone, double width, double minStart = 0d, double maxStart = double.PositiveInfinity)
        {
            var total = ZoneWidth(zone);
            var x = CenterBetweenContent && maxStart < double.PositiveInfinity
                ? minStart + (maxStart - minStart - total) / 2
                : (width - total) / 2;
            x = Math.Clamp(x, Math.Min(minStart, maxStart), Math.Max(minStart, maxStart));
            foreach (var child in zone)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width + Spacing;
            }
        }
    }
}
