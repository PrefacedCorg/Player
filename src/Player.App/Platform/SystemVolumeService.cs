using System.Runtime.InteropServices;

namespace Player.App.Platform;

/// <summary>
/// Windows 系统主音量（Core Audio IAudioEndpointVolume 的精简 P/Invoke 封装）。
/// <para>只提供主音量的读/写与变化轮询——音量控件「直接调整系统音量」模式需要：
/// 0–100% 只动系统、超过 100% 后系统固定 100 由软件增益；系统被外部（键盘/其它应用）
/// 调低时控件跟随。COM 对象进程内单例缓存，首次失败则视为不可用，静默降级。</para>
/// </summary>
public static class SystemVolumeService
{
    private const int Render = 0;           // eRender：输出设备
    private const int Multimedia = 1;       // eMultimedia：多媒体角色
    private const int ClsctxInprocServer = 1; // CLSCTX_INPROC_SERVER（mmdeviceapi 都是进程内）

    public static bool IsAvailable { get; private set; }

    /// <summary>系统音量变化（0–100），500ms 轮询触发，UI 线程。</summary>
    public static event EventHandler<double>? Changed;

    private static readonly Avalonia.Threading.DispatcherTimer Timer =
        new() { Interval = TimeSpan.FromMilliseconds(500) };

    private static IAudioEndpointVolume? _endpoint;
    private static double _last;

    static SystemVolumeService()
    {
        try
        {
            var iid = typeof(IAudioEndpointVolume).GUID;
            ((IMMDeviceEnumerator)new MMDeviceEnumerator()).GetDefaultAudioEndpoint(Render, Multimedia, out var device);
            _endpoint = (IAudioEndpointVolume)device.Activate(ref iid, ClsctxInprocServer, IntPtr.Zero);
            IsAvailable = true;
            _last = Get();
            Timer.Tick += (_, _) => Poll();
        }
        catch
        {
            IsAvailable = false;
        }
    }

    /// <summary>当前系统主音量（0–100）。不可用时返回 -1。</summary>
    public static double Get()
    {
        if (!IsAvailable || _endpoint is null)
        {
            return -1d;
        }

        try
        {
            _endpoint.GetMasterVolumeLevelScalar(out var level);
            return Math.Round(level * 100d, 0, MidpointRounding.AwayFromZero);
        }
        catch
        {
            return -1d;
        }
    }

    /// <summary>设置系统主音量（0–100）。失败静默（保持现状）。</summary>
    public static void Set(double volume)
    {
        if (!IsAvailable || _endpoint is null)
        {
            return;
        }

        try
        {
            _endpoint.SetMasterVolumeLevelScalar((float)Math.Clamp(volume, 0d, 100d) / 100f, Guid.Empty);
        }
        catch
        {
            // 设备热切换等瞬时失败：忽略，下次轮询/设置再试
        }
    }

    /// <summary>启动变化轮询（音量控件挂载时调用）。不可用时为空操作。</summary>
    public static void StartPolling()
    {
        if (IsAvailable)
        {
            Timer.Start();
        }
    }

    public static void StopPolling() => Timer.Stop();

    private static void Poll()
    {
        var current = Get();
        if (current >= 0 && Math.Abs(current - _last) >= 0.5d)
        {
            _last = current;
            Changed?.Invoke(null, current);
        }
    }

    #region Core Audio COM 精简定义（vtable 顺序必须与真实接口一致，未用到的方法也要占位）

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        // vtable 3：EnumAudioEndpoints(EDataFlow, EDeviceState, out IMMDeviceCollection)
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int state, out IntPtr devices);

        // vtable 4：GetDefaultAudioEndpoint(EDataFlow, ERole, out IMMDevice)
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        // vtable 3：Activate(REFIID, DWORD, PROPVARIANT*, out object)
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object Activate(ref Guid iid, int clsCtx, IntPtr activationParams);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        // vtable 3–6：用不到的方法按真实签名占位，保证后面的方法不错位
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr callback);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr callback);

        [PreserveSig]
        int GetChannelCount(out int count);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, Guid context);

        // vtable 7：写主音量（0.0–1.0）
        void SetMasterVolumeLevelScalar(float level, Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        // vtable 9：读主音量（0.0–1.0）
        void GetMasterVolumeLevelScalar(out float level);
    }

    #endregion
}
