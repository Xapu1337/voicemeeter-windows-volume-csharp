using System.Runtime.InteropServices;

namespace VoicemeeterWindowsVolume.Workers;

/// <summary>
/// Event-driven Windows volume listener using WASAPI IAudioEndpointVolumeCallback.
/// </summary>
public class NativeVolumeListener : IDisposable
{
    public event Action<float, bool>? VolumeNotification;

    private IAudioEndpointVolume? _endpointVolume;
    private VolumeCallback? _callback;
    private bool _disposed;

    public bool Start()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device));

            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out object aev));
            _endpointVolume = (IAudioEndpointVolume)aev;

            _callback = new VolumeCallback(this);
            Marshal.ThrowExceptionForHR(_endpointVolume.RegisterControlChangeNotify(_callback));

            Console.WriteLine("NativeVolumeListener: started (event-driven WASAPI)");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NativeVolumeListener: failed to start, will fall back to PowerShell: {ex.Message}");
            return false;
        }
    }

    public (float volume, bool muted) GetCurrentState()
    {
        _endpointVolume!.GetMasterVolumeLevelScalar(out float vol);
        _endpointVolume!.GetMute(out bool muted);
        return (vol, muted);
    }

    public void SetVolume(float volume)
    {
        var empty = Guid.Empty;
        _endpointVolume?.SetMasterVolumeLevelScalar(volume, ref empty);
    }

    public void Stop()
    {
        if (_callback != null && _endpointVolume != null)
        {
            try { _endpointVolume.UnregisterControlChangeNotify(_callback); }
            catch { /* ignore — process may be shutting down */ }
        }
        if (_endpointVolume != null)
        {
            Marshal.ReleaseComObject(_endpointVolume);
            _endpointVolume = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    internal void OnNotification(float volume, bool muted)
        => VolumeNotification?.Invoke(volume, muted);

    //  COM callback 

    [ComVisible(true)]
    private class VolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly NativeVolumeListener _parent;
        public VolumeCallback(NativeVolumeListener parent) => _parent = parent;

        public int OnNotify(IntPtr pNotify)
        {
            try
            {
                var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(pNotify);
                _parent.OnNotification(data.fMasterVolume, data.bMuted);
            }
            catch { /* never throw across COM boundary */ }
            return 0; // S_OK
        }
    }

    //  Structs 

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioVolumeNotificationData
    {
        public Guid guidEventContext;
        [MarshalAs(UnmanagedType.Bool)] public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
        // afChannelVolumes[] follows but is not needed
    }

    //  COM interface definitions 
    //
    // Vtable order must exactly match endpointvolume.h / mmdeviceapi.h.
    // All methods use [PreserveSig] so we handle HRESULTs ourselves.

    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotify);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }
}
