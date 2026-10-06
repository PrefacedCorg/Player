namespace Player.App.Controls;

/// <summary>
/// 控制栏控件的 SVG 图标库（Tabler Icons stroke 风格）：
/// 每个图标是一段 24×24 坐标系的矢量路径（SVG path data），
/// 由 <see cref="PlayerControlButton"/> 解析成 <see cref="Avalonia.Media.StreamGeometry"/> 渲染，
/// 统一使用 Stroke 绘制（stroke-width="2"、round 端点和连接）。
/// </summary>
public static class PlayerControlIcons
{
    /// <summary>打开文件：file-import。</summary>
    public const string OpenFile =
        "M14 3v4a1 1 0 0 0 1 1h4 M5 13v-8a2 2 0 0 1 2 -2h7l5 5v11a2 2 0 0 1 -2 2h-5.5m-9.5 -2h7m-3 -3l3 3l-3 3";

    /// <summary>播放：player-play。</summary>
    public const string Play =
        "M7 4v16l13 -8l-13 -8";

    /// <summary>暂停：player-pause。</summary>
    public const string Pause =
        "M6 6a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v12a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -12 M14 6a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v12a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -12";

    /// <summary>停止：player-stop。</summary>
    public const string Stop =
        "M5 7a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2l0 -10";

    /// <summary>音量最大：volume。</summary>
    public const string Volume =
        "M15 8a5 5 0 0 1 0 8 M17.7 5a9 9 0 0 1 0 14 M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5l-3.5 -4.5";

    /// <summary>音量中：volume-2。</summary>
    public const string Volume2 =
        "M15 8a5 5 0 0 1 0 8 M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5l-3.5 -4.5";

    /// <summary>音量最低/静音：volume-3。</summary>
    public const string Volume3 =
        "M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5l-3.5 -4.5 M16 10l4 4m0 -4l-4 4";

    /// <summary>渲染器：gpu。</summary>
    public const string Renderer =
        "M3 4v14 M3 6h17a1 1 0 0 1 1 1v8a1 1 0 0 1 -1 1h-17 M9 13a2 2 0 1 0 0 -4a2 2 0 0 0 0 4 M15 9v4 M18 9v4 M7 16v3h8v-3";

    /// <summary>设置：settings-2。</summary>
    public const string Settings =
        "M19.875 6.27a2.225 2.225 0 0 1 1.125 1.948v7.284c0 .809 -.443 1.555 -1.158 1.948l-6.75 4.27a2.269 2.269 0 0 1 -2.184 0l-6.75 -4.27a2.225 2.225 0 0 1 -1.158 -1.948v-7.285c0 -.809 .443 -1.554 1.158 -1.947l6.75 -3.98a2.33 2.33 0 0 1 2.25 0l6.75 3.98h-.033 M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0";

    /// <summary>关闭：x。</summary>
    public const string Close =
        "M18 6l-12 12 M6 6l12 12";

    /// <summary>最小化：window-minimize。</summary>
    public const string Minimize =
        "M3 17a1 1 0 0 1 1 -1h3a1 1 0 0 1 1 1v3a1 1 0 0 1 -1 1h-3a1 1 0 0 1 -1 -1l0 -3 M4 12v-6a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-6 M15 13h-4v-4 M11 13l5 -5";

    /// <summary>全屏：arrows-maximize。</summary>
    public const string Fullscreen =
        "M16 4l4 0l0 4 M14 10l6 -6 M8 20l-4 0l0 -4 M4 20l6 -6 M16 20l4 0l0 -4 M14 14l6 6 M8 4l-4 0l0 4 M4 4l6 6";

    /// <summary>上一个：player-track-prev。</summary>
    public const string Previous =
        "M21 5v14l-8 -7l8 -7 M10 5v14l-8 -7l8 -7";

    /// <summary>下一个：player-track-next。</summary>
    public const string Next =
        "M3 5v14l8 -7l-8 -7 M14 5v14l8 -7l-8 -7";

    /// <summary>快退（10s）：rewind-backward-10。</summary>
    public const string Rewind =
        "M7 9l-3 -3l3 -3 M15.997 17.918a6.002 6.002 0 0 0 -.997 -11.918h-11 M6 14v6 M9 15.5v3a1.5 1.5 0 0 0 3 0v-3a1.5 1.5 0 0 0 -3 0";

    /// <summary>快进（10s）：rewind-forward-10。</summary>
    public const string FastForward =
        "M17 9l3 -3l-3 -3 M8 17.918a5.997 5.997 0 0 1 -5 -5.918a6 6 0 0 1 6 -6h11 M12 14v6 M15 15.5v3a1.5 1.5 0 0 0 3 0v-3a1.5 1.5 0 0 0 -3 0";

    /// <summary>列表循环：repeat。</summary>
    public const string LoopMode =
        "M4 12v-3a3 3 0 0 1 3 -3h13m-3 -3l3 3l-3 3 M20 12v3a3 3 0 0 1 -3 3h-13m3 3l-3 -3l3 -3";

    /// <summary>单曲循环：repeat-once。</summary>
    public const string LoopModeOnce =
        "M4 12v-3a3 3 0 0 1 3 -3h13m-3 -3l3 3l-3 3 M20 12v3a3 3 0 0 1 -3 3h-13m3 3l-3 -3l3 -3 M11 11l1 -1v4";

    /// <summary>顺序播放：line-height。</summary>
    public const string LoopModeSequential =
        "M3 8l3 -3l3 3 M3 16l3 3l3 -3 M6 5l0 14 M13 6l7 0 M13 12l7 0 M13 18l7 0";
}
