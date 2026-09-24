using System.Diagnostics;
using System.Text;

namespace Player.Platform;

/// <summary>
/// 冷启动打点器。每个打点即时落盘，进程崩溃也能拿到最后一条记录。
/// 这是 M0 建立冷启动基线的唯一测量手段，后续优化全部以它为准。
/// </summary>
public static class StartupTrace
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly DateTime ProcessStart = ResolveProcessStart();

    public static string LogPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Player",
        "logs",
        "startup.log");

    public static double ElapsedMs => Watch.Elapsed.TotalMilliseconds;

    /// <summary>写入本次会话的分隔头。</summary>
    public static void NewSession()
    {
        Write($"{Environment.NewLine}===== 会话开始 {DateTime.Now:yyyy-MM-dd HH:mm:ss} · pid={Environment.ProcessId} =====");
    }

    public static void Mark(string label)
    {
        var sinceProcessStart = (DateTime.Now - ProcessStart).TotalMilliseconds;
        Write($"[trace +{ElapsedMs,7:F0} ms | process +{sinceProcessStart,7:F0} ms] {label}");
    }

    private static void Write(string line)
    {
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                var dir = System.IO.Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 打点失败不能影响主流程。
        }
    }

    private static DateTime ResolveProcessStart()
    {
        try
        {
            return Process.GetCurrentProcess().StartTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}