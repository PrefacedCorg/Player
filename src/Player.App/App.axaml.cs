using Avalonia;
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
    /// </summary>
    public static PlayerSettings Settings { get; } = new();

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
            StartupTrace.Mark("构建 MainWindow（含 XAML 解析与 mpv 上下文创建）");
            var window = new MainWindow();
            StartupTrace.Mark("MainWindow 构建完成");
            desktop.MainWindow = window;
            window.Opened += (_, _) => StartupTrace.Mark("窗口 Opened：首帧上屏，可交互");
        }

        base.OnFrameworkInitializationCompleted();
    }
}