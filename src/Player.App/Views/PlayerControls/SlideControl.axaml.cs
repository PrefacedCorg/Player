using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Player.App.Views.PlayerControls;

/// <summary>
/// 轮播容器（对应 ClassIsland 的 SlideComponent）：按设置的间隔轮流显示容器里的子控件，
/// 支持循环、随机、往复三种模式；被规则隐藏的子控件会被跳过。
/// </summary>
public partial class SlideControl : UserControl
{
    private readonly PlayerControlItem? _item;
    private readonly DispatcherTimer _timer = new();
    private readonly List<Panel> _wrappers = [];
    private readonly Queue<int> _randomPlaylist = new();
    private int _selectedIndex;
    private int _playingDirection = 1;

    // 无参构造让 XAML 运行时加载器也能创建（AVLN3001）。
    // 没有绑定控件项就没有轮播设置，此时只显示第一个子控件，不自动切换。
    public SlideControl() : this([])
    {
    }

    public SlideControl(IReadOnlyList<Control> children)
    {
        InitializeComponent();

        // 每个子控件套一层面板：宿主自己要用 IsVisible 表达"按规则隐藏"，
        // 轮播的"显示哪一个"改在这层面板上表达，两者互不干扰。
        foreach (var child in children)
        {
            var wrapper = new Panel();
            wrapper.Children.Add(child);
            _wrappers.Add(wrapper);
            Root.Children.Add(wrapper);
        }

        _selectedIndex = 0;
        ApplyVisibility();
    }

    public SlideControl(PlayerControlItem item, IReadOnlyList<Control> children) : this(children)
    {
        _item = item;

        var settings = (SlideControlSettings)item.Settings;
        settings.PropertyChanged += OnSettingsChanged;
        _timer.Interval = GetInterval();
        _timer.Tick += (_, _) => ShowNext();
        AttachedToVisualTree += (_, _) =>
        {
            _timer.Interval = GetInterval();
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _timer.Stop();
            settings.PropertyChanged -= OnSettingsChanged;
        };
    }

    private TimeSpan GetInterval()
    {
        if (_item?.Settings is not SlideControlSettings settings)
        {
            return TimeSpan.FromSeconds(1);
        }

        return TimeSpan.FromSeconds(Math.Clamp(settings.SlideSeconds, 0.5, 3600));
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlideControlSettings.SlideSeconds))
        {
            _timer.Interval = GetInterval();
        }
    }

    private void ShowNext()
    {
        if (_wrappers.Count <= 1)
        {
            _selectedIndex = 0;
            ApplyVisibility();
            return;
        }

        // 与 ClassIsland 一致：不断尝试下一个，直到找到一个没被规则隐藏的子控件
        var checkedFlags = new bool[_wrappers.Count];
        var checkedCount = 0;
        do
        {
            Advance();
            if (!checkedFlags[_selectedIndex] && !IsChildVisible(_selectedIndex))
            {
                checkedFlags[_selectedIndex] = true;
                checkedCount++;
            }

            if (checkedCount >= _wrappers.Count)
            {
                break;
            }
        }
        while (checkedFlags[_selectedIndex]);

        ApplyVisibility();
    }

    private bool IsChildVisible(int index) =>
        _wrappers[index].Children.Count > 0 && _wrappers[index].Children[0].IsVisible;

    private void Advance()
    {
        var mode = (_item?.Settings as SlideControlSettings)?.SlideMode ?? 0;
        switch (mode)
        {
            case 1: // 随机
                if (_randomPlaylist.Count <= 0)
                {
                    CreateRandomPlaylist();
                }

                _selectedIndex = _randomPlaylist.Dequeue();
                break;

            case 2: // 往复
                var next = _selectedIndex + _playingDirection;
                if (next < 0 || next >= _wrappers.Count)
                {
                    _playingDirection = -_playingDirection;
                }

                _selectedIndex += _playingDirection;
                break;

            default: // 循环
                _selectedIndex = _selectedIndex + 1 >= _wrappers.Count ? 0 : _selectedIndex + 1;
                break;
        }
    }

    private void CreateRandomPlaylist()
    {
        _randomPlaylist.Clear();
        var indices = Enumerable.Range(0, _wrappers.Count).ToArray();
        Random.Shared.Shuffle(indices);
        foreach (var index in indices)
        {
            _randomPlaylist.Enqueue(index);
        }
    }

    private void ApplyVisibility()
    {
        for (var i = 0; i < _wrappers.Count; i++)
        {
            _wrappers[i].IsVisible = i == _selectedIndex;
        }
    }
}