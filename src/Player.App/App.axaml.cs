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
        }

        base.OnFrameworkInitializationCompleted();
    }
}