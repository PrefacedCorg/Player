using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Win32;
using HanumanInstitute.LibMpv.Avalonia;
using Player.Platform;

namespace Player.App;

internal static class Program
{
    /// <summary>
    /// 启动时要播放的媒体文件。支持三种进入方式：
    /// 1) 双击/打开方式（Windows 传裸文件路径）2) 拖到 exe 上（同上）3) 命令行 --file 指定。
    /// </summary>
    public static IReadOnlyList<string> StartupFiles { get; private set; } = [];

    /// <summary>命令行指定的渲染器（未指定时用 OpenGl，避免 Auto 在 Windows 上落到会抢输入的 NativeView）。</summary>
    public static VideoRenderer? StartupRenderer { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartupTrace.NewSession();
        StartupTrace.Mark("Main() 进入");

        // 预热 libmpv 原生库：约 117MB 的 DLL 装载与重定位放到后台线程，
        // 不占用"进程→窗口可交互"的关键路径（mpv 上下文创建时会命中已装载的库）。
        _ = Task.Run(WarmUpNativeLibrary);

        StartupFiles = ParseMediaFiles(args);
        StartupRenderer = ParseStartupRenderer(args);

        if (StartupFiles.Count > 0)
        {
            StartupTrace.Mark($"启动参数携带 {StartupFiles.Count} 个媒体文件：{string.Join(" | ", StartupFiles)}");
        }

        if (StartupRenderer is not null)
        {
            StartupTrace.Mark($"启动参数指定渲染器：{StartupRenderer}");
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            StartupTrace.Mark("未处理异常导致退出：" + ex);
            throw;
        }
        finally
        {
            StartupTrace.Mark("进程退出");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();

        // Windows 上必须走 ANGLE(EGL over D3D11)：libmpv 的 OpenGL 渲染上下文
        // 需要与 Avalonia 共享同一条 GPU 管线，否则退回软件渲染。
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
            });
        }

        return builder;
    }

    /// <summary>后台预热 libmpv 原生库，避免大 DLL 装载落在启动关键路径上。</summary>
    private static void WarmUpNativeLibrary()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll");
            if (File.Exists(path))
            {
                NativeLibrary.Load(path);
                StartupTrace.Mark("libmpv 原生库预热完成（后台）");
            }
        }
        catch (Exception ex)
        {
            StartupTrace.Mark("libmpv 原生库预热失败（不影响后续加载）：" + ex.Message);
        }
    }

    /// <summary>
    /// 解析启动参数为媒体文件列表。
    /// 兼容 "打开方式 / 拖放到 exe" 传裸路径的形态，也兼容 --file 显式指定；多个文件按顺序入播放队列。
    /// </summary>
    private static IReadOnlyList<string> ParseMediaFiles(string[] args)
    {
        var files = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim().Trim('"');

            if (arg is "--file" or "-f")
            {
                if (i + 1 < args.Length)
                {
                    AddFile(files, args[++i]);
                }

                continue;
            }

            // 跳过开关类参数（Windows 有时会附带 /dde 之类）
            if (arg.Length == 0 || arg[0] is '-' or '/')
            {
                continue;
            }

            AddFile(files, arg);
        }

        return files;
    }

    private static void AddFile(List<string> files, string rawPath)
    {
        var path = rawPath.Trim().Trim('"');
        if (path.Length > 0 && File.Exists(path))
        {
            var fullPath = Path.GetFullPath(path);
            if (!files.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(fullPath);
            }
        }
    }

    private static VideoRenderer? ParseStartupRenderer(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--renderer" or "-r"
                && Enum.TryParse<VideoRenderer>(args[i + 1], ignoreCase: true, out var renderer))
            {
                return renderer;
            }
        }

        return null;
    }
}