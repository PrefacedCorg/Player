using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Player.App.Controls;

/// <summary>
/// 跟踪「全局设置 + 组件高级设置」合成的控件显示形式（图标 / 文字）：
/// Attach 时沿视觉树找最近的 <see cref="PlayerControlHost"/> 拿该控件项的设置，
/// 与 <see cref="App.Settings"/>（全局）一起订阅；任一边变化即回调。控件卸载时必须 Detach。
/// </summary>
internal sealed class PlayerControlDisplayModeTracker
{
    private readonly Control _owner;
    private readonly Action _changed;
    private PlayerControlSettings? _settings;

    public PlayerControlDisplayModeTracker(Control owner, Action changed)
    {
        _owner = owner;
        _changed = changed;
    }

    /// <summary>当前合成的显示形式（组件设了 FollowGlobal 或找不到组件设置时只看全局设置）。</summary>
    public ControlDisplayMode Current =>
        _settings is { DisplayMode: not ControlDisplayMode.FollowGlobal } settings
            ? settings.DisplayMode
            : App.Settings.ControlDisplayMode;

    public void Attach()
    {
        _settings = _owner.FindAncestorOfType<PlayerControlHost>()?.Item.Settings;
        if (_settings is not null)
        {
            _settings.PropertyChanged += OnSettingsChanged;
        }

        App.Settings.PropertyChanged += OnGlobalSettingsChanged;
    }

    public void Detach()
    {
        if (_settings is not null)
        {
            _settings.PropertyChanged -= OnSettingsChanged;
            _settings = null;
        }

        App.Settings.PropertyChanged -= OnGlobalSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerControlSettings.DisplayMode))
        {
            _changed();
        }
    }

    private void OnGlobalSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerSettings.ControlDisplayMode))
        {
            _changed();
        }
    }
}
