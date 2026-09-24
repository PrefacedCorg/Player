namespace Player.Playback;

/// <summary>
/// 播放器状态快照。由引擎在轮询线程上产生，订阅方负责切回 UI 线程。
/// </summary>
public readonly record struct MediaEngineState(
    bool IsPaused,
    TimeSpan Position,
    TimeSpan Duration,
    string? Title)
{
    public static readonly MediaEngineState Empty = new(false, TimeSpan.Zero, TimeSpan.Zero, null);

    public bool IsPlaying => !IsPaused && (Position > TimeSpan.Zero || Duration > TimeSpan.Zero);
}