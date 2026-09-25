using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Player.Platform;

namespace Player.App.Assists;

/// <summary>
/// 触摸模式辅助（照 ClassIsland 的 PointerStateAssist）：一个继承式的附加属性，窗口在指针按下时按指针类型置位。
/// 触摸屏点一下 → 触摸模式打开，拖动用的手柄图标（TouchDragThumb）随之出现；用鼠标点一下 → 触摸模式关闭、手柄隐藏。
/// <para>
/// 与 ClassIsland 原版有两处差别，都是 Player 里实际踩到的：
/// 一是状态做成全局的，任一窗口触发都对所有窗口生效（手柄只在设置窗口里，用 F6 在主窗口验证时也要能看到）；
/// 二是事件从隧道阶段订阅并打开 handledEventsToo，不会因为列表项、拖动行为把按下事件标记为已处理而漏掉。
/// </para>
/// </summary>
public class PointerStateAssist
{
    private static readonly List<Window> AttachedWindows = [];
    private static bool _isTouchMode;

    public static readonly AttachedProperty<bool> IsTouchModeProperty =
        AvaloniaProperty.RegisterAttached<PointerStateAssist, Control, bool>("IsTouchMode", inherits: true);

    internal static void SetIsTouchMode(Control obj, bool value) => obj.SetValue(IsTouchModeProperty, value);

    public static bool GetIsTouchMode(Control obj) => obj.GetValue(IsTouchModeProperty);

    /// <summary>当前是否处于触摸模式（全局状态，所有窗口共用）。</summary>
    public static bool IsTouchMode
    {
        get => _isTouchMode;
        private set
        {
            if (_isTouchMode == value)
            {
                return;
            }

            _isTouchMode = value;
            foreach (var window in AttachedWindows)
            {
                SetIsTouchMode(window, value);
            }

            // 落一条打点：手柄没出现时先看这条日志，能分清是"模式没切"还是"样式没生效"
            StartupTrace.Mark($"(debug) 触摸模式 = {(value ? "开" : "关")}");
        }
    }

    /// <summary>
    /// 让窗口跟随指针类型切换触摸模式（对应 ClassIsland 在 MyWindow.SetupMyWindowExt 里的做法）。
    /// 另外支持 F6 手动切换，方便在非触摸设备上验证触摸布局。
    /// </summary>
    public static void Attach(Window window)
    {
        if (!AttachedWindows.Contains(window))
        {
            AttachedWindows.Add(window);
        }

        SetIsTouchMode(window, _isTouchMode);

        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        window.Closed += (_, _) => AttachedWindows.Remove(window);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
        IsTouchMode = e.Pointer.Type == PointerType.Touch;

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F6)
        {
            return;
        }

        IsTouchMode = !IsTouchMode;
        e.Handled = true;
    }
}