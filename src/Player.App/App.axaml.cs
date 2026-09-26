using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Player.App.Views;
using Player.Platform;

namespace Player.App;

public partial class App : Application
{
    /// <summary>
    /// 应用级实时设置：主窗口与设置窗口共用这一份，改动即时生效（见 <see cref="PlayerSettings"/>）。
    /// 放在 App 上而不是各自新建，是为了保证设置窗口改的值与播放窗口读的值是同一个对象。
    /// 启动时从 settings.json 读入（没有或损坏时用默认布局），变更即保存（见 <see cref="PlayerSettingsStore"/>）。
    /// </summary>
    public static PlayerSettings Settings { get; } = PlayerSettingsStore.Load() ?? new();

    public override void Initialize()
    {
        StartupTrace.Mark("Application.Initialize 开始");
        AvaloniaXamlLoader.Load(this);
        StartupTrace.Mark("Application.Initialize 完成");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 变更即保存：行/组件/设置属性任何变化立即写盘（ClassIsland 同款机制）
            PlayerSettingsStore.Attach(Settings);

            StartupTrace.Mark("构建 MainWindow（含 XAML 解析与 mpv 上下文创建）");
            var window = new MainWindow();
            StartupTrace.Mark("MainWindow 构建完成");
            desktop.MainWindow = window;
            window.Opened += (_, _) => StartupTrace.Mark("窗口 Opened：首帧上屏，可交互");

            // 设置窗口是非模态的，默认的 OnLastWindowClose 会让"关掉播放窗口后留下一个设置窗口"，
            // 这里按播放窗口的生命周期退出。
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 退出兜底：变更即保存之外，退出时再写一次盘（ClassIsland 停止流程同款）
            desktop.Exit += (_, _) => PlayerSettingsStore.Save(Settings);

            // 托盘（ClassIsland TaskBarIconService 同款）：TrayIcon 在 XAML 里定义会在填充阶段
            // 崩（Icon/Menu 属性赋值 NRE），因此纯代码创建：图标从输出目录按相对路径构造
            //（ClassIsland 的 AppLogo 同款）；菜单在 App.axaml Resources 里定义，这里取出挂上；
            // 退出时隐藏，避免进程结束后托盘残留一块空图标。
            var trayIcon = new TrayIcon
            {
                Icon = new WindowIcon("Assets/TrayIcon.png"),
                ToolTipText = "Player",
                Menu = Resources["AppTrayMenu"] as NativeMenu,
            };
            trayIcon.Clicked += OnTrayIconClicked;
            TrayIcon.SetIcons(this, [trayIcon]);
            desktop.Exit += (_, _) => trayIcon.IsVisible = false;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>托盘菜单「打开设置…」：转发给主窗口（设置窗口由它管理，非模态可重复调用）。</summary>
    private void OnTrayOpenSettingsClick(object? sender, EventArgs e) =>
        ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow)?.OpenSettings();

    /// <summary>托盘菜单「显示主界面」。</summary>
    private void OnTrayShowMainWindowClick(object? sender, EventArgs e) => ShowMainWindow();

    /// <summary>托盘菜单「退出」：与控制栏「关闭」同一路径（关主窗口，ShutdownMode 接管退出）。</summary>
    private void OnTrayExitClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (desktop.MainWindow is { } window)
        {
            window.Close();
        }
        else
        {
            desktop.Shutdown();
        }
    }

    /// <summary>左键点击托盘图标：显示并激活主窗口（Windows 惯例，右键才弹菜单）。</summary>
    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();

    /// <summary>把主窗口带回前台：最小化先还原，再 Show + Activate。</summary>
    private void ShowMainWindow()
    {
        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
    }
}