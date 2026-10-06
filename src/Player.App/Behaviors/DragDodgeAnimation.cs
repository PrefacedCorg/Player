using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace Player.App.Behaviors;

/// <summary>
/// 拖拽避让动画辅助类
/// 为列表项容器提供独立的 TransformOperationsTransition，实现平滑的开缝/避让/收缝效果
/// </summary>
public static class DragDodgeAnimation
{
    private const double AnimationDurationSeconds = 0.25;

    /// <summary>
    /// 为控件附加独立的避让过渡效果
    /// </summary>
    public static void AttachTransition(Control control)
    {
        if (control == null) return;

        // 避免重复附加：检查是否已有 RenderTransform 过渡
        if (control.Transitions != null)
        {
            foreach (var transition in control.Transitions)
            {
                if (transition is TransformOperationsTransition)
                    return;
            }
        }

        var transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromSeconds(AnimationDurationSeconds),
                Easing = new CubicEaseOut()
            }
        };

        control.Transitions = transitions;
    }

    /// <summary>
    /// 对控件施加平移变换（开缝避让）
    /// </summary>
    /// <param name="control">目标控件</param>
    /// <param name="translateX">X方向平移量</param>
    /// <param name="translateY">Y方向平移量</param>
    public static void ApplyDodge(Control control, double translateX, double translateY)
    {
        if (control == null) return;

        AttachTransition(control);

        var builder = new TransformOperations.Builder(1);
        builder.AppendTranslate(translateX, translateY);
        control.RenderTransform = builder.Build();
    }

    /// <summary>
    /// 清除控件的平移变换（收缝）
    /// </summary>
    public static void ClearDodge(Control control)
    {
        if (control == null) return;

        AttachTransition(control);
        control.RenderTransform = null;
    }

    /// <summary>
    /// 批量清除多个控件的平移变换
    /// </summary>
    public static void ClearAllDodge(System.Collections.Generic.IEnumerable<Control> controls)
    {
        if (controls == null) return;

        foreach (var control in controls)
        {
            ClearDodge(control);
        }
    }
}
