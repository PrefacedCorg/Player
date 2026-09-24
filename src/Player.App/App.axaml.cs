using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Player.App.Views;
using Player.Platform;

namespace Player.App;

public partial class App : Application
{
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