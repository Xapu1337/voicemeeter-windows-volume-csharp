using VoicemeeterWindowsVolume.Models;

namespace VoicemeeterWindowsVolume.Workers;

/// <summary>
/// Polls Windows audio volume and mute state via a persistent PowerShell worker.
/// Raises events when values change.
/// </summary>
public class WindowsAudioScanner
{
    private static WindowsAudioScanner? _instance;
    public static WindowsAudioScanner Instance => _instance ??= new WindowsAudioScanner();

    private const string Label = "AudioScanner";
    private const string LabelAudioDevices = "AudioDeviceScanner";
    private const string LabelAllDevices = "AllDeviceScanner";

    private int? _volume;
    private bool? _muted;
    private List<string> _audioDevices = new();
    private List<string> _allDevices = new();
    private bool _started;
    private NativeVolumeListener? _nativeListener;

    // Events
    public event EventHandler? Started;
    public event EventHandler<VolumeChange>? VolumeChanged;
    public event EventHandler<MuteChange>? MuteChanged;
    public event EventHandler<DeviceChange>? AudioDeviceChanged;
    public event EventHandler<DeviceChange>? AnyDeviceChanged;

    private static readonly string AudioClassSetup = @"
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;

[Guid(""5CDF2C82-841E-4546-9722-0CF74078229A""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
    int f(); int g(); int h(); int i();
    int SetMasterVolumeLevelScalar(float fLevel, System.Guid pguidEventContext);
    int j();
    int GetMasterVolumeLevelScalar(out float pfLevel);
    int k(); int l(); int m(); int n();
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, System.Guid pguidEventContext);
    int GetMute(out bool pbMute);
};

[Guid(""D666063F-1587-4E43-81F1-B948E807363F""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    int Activate(ref System.Guid id, int clsCtx, int activationParams, out IAudioEndpointVolume aev);
};

[Guid(""A95664D2-9614-4F35-A746-DE8DB63617E6""), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    int f();
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
};

[ComImport, Guid(""BCDE0395-E52F-467C-8E3D-C4579291692E"")] class MMDeviceEnumeratorComObject { };

public class Audio {
    static IAudioEndpointVolume Vol() {
        var enumerator = new MMDeviceEnumeratorComObject() as IMMDeviceEnumerator;
        IMMDevice dev = null;
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out dev));
        IAudioEndpointVolume epv = null;
        var epvid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref epvid, 23, 0, out epv));
        return epv;
    }
    public static float Volume {
        get { float v = -1; Marshal.ThrowExceptionForHR(Vol().GetMasterVolumeLevelScalar(out v)); return v; }
        set { Marshal.ThrowExceptionForHR(Vol().SetMasterVolumeLevelScalar(value, System.Guid.Empty)); }
    }
    public static bool Mute {
        get { bool mute; Marshal.ThrowExceptionForHR(Vol().GetMute(out mute)); return mute; }
        set { Marshal.ThrowExceptionForHR(Vol().SetMute(value, System.Guid.Empty)); }
    }
};
'@
";

    public int? GetVolume() => _volume;
    public bool? GetMuted() => _muted;

    public void SetVolume(int volumePercent)
    {
        if (_nativeListener != null)
            _nativeListener.SetVolume(volumePercent * 0.01f);
        else
            Controllers.PowerShellRunner.SendToWorker(Label, $"[Audio]::Volume = {volumePercent * 0.01f:F2}");
    }

    public void StartAudioScanner(int intervalMs)
    {
        if (_started) return;
        _started = true;

        // Prefer event-driven native WASAPI
        // we do have a fallback path if this fails
        var native = new NativeVolumeListener();
        if (native.Start())
        {
            _nativeListener = native;

            // Fire initial state so callers receive the Started event
            var (vol, muted) = native.GetCurrentState();
            HandleAudioValues(vol, muted, initial: true);

            native.VolumeNotification += (vol, muted) => HandleAudioValues(vol, muted, initial: false);
        }
        else
        {
            native.Dispose();
            Controllers.PowerShellRunner.StartWorker(
                label: Label,
                command: "[Audio]::Volume | Out-Host; [Audio]::Mute | Out-Host;",
                intervalMs: intervalMs,
                onResponse: HandleAudioResponse,
                setup: AudioClassSetup
            );
        }
    }

    public void StopAudioScanner()
    {
        if (_nativeListener != null)
        {
            _nativeListener.Dispose();
            _nativeListener = null;
        }
        else
        {
            Controllers.PowerShellRunner.StopWorker(Label);
        }
        _started = false;
    }

    // PowerShell fallback path 
    private void HandleAudioResponse(List<string> lines)
    {
        if (lines.Count < 2) return;

        bool parsed = float.TryParse(
            lines[0].Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float rawVolume
        );
        if (!parsed) return;

        bool newMuted = lines[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        HandleAudioValues(rawVolume, newMuted, initial: false);
    }

    // Shared handler for both native and PowerShell paths
    private void HandleAudioValues(float rawVolume, bool newMuted, bool initial)
    {
        int newVolume = (int)Math.Round(rawVolume * 100);

        if (_volume != newVolume)
        {
            bool wasNull = _volume == null;
            int old = _volume ?? 0;
            _volume = newVolume;

            if (wasNull || initial)
                Started?.Invoke(this, EventArgs.Empty);
            else
                VolumeChanged?.Invoke(this, new VolumeChange { Old = old, New = newVolume });
        }

        if (_muted != newMuted)
        {
            bool old = _muted ?? false;
            bool wasNull = _muted == null;
            _muted = newMuted;

            if (!wasNull && !initial)
                MuteChanged?.Invoke(this, new MuteChange { Old = old, New = newMuted });
        }
    }

    public void StartAudioDeviceScanner()
    {
        Controllers.PowerShellRunner.StartWorker(
            label: LabelAudioDevices,
            command: "get-wmiobject win32_sounddevice | Out-Host;",
            intervalMs: 5000,
            onResponse: HandleAudioDeviceResponse
        );
    }

    public void StopAudioDeviceScanner()
        => Controllers.PowerShellRunner.StopWorker(LabelAudioDevices);

    private void HandleAudioDeviceResponse(List<string> lines)
    {
        var newDevices = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (!newDevices.SequenceEqual(_audioDevices))
        {
            int old = _audioDevices.Count;
            int @new = newDevices.Count;
            var added = newDevices.Except(_audioDevices).ToList();
            var removed = _audioDevices.Except(newDevices).ToList();
            _audioDevices = newDevices;
            AudioDeviceChanged?.Invoke(this, new DeviceChange
            {
                Old = old,
                New = @new,
                Added = added,
                Removed = removed
            });
        }
    }

    public void StartAllDeviceScanner()
    {
        Controllers.PowerShellRunner.StartWorker(
            label: LabelAllDevices,
            command: "Get-PnpDevice | Where-Object { $_.Status -eq 'OK' } | Select-Object FriendlyName | Out-Host;",
            intervalMs: 5000,
            onResponse: HandleAllDeviceResponse
        );
    }

    public void StopAllDeviceScanner()
        => Controllers.PowerShellRunner.StopWorker(LabelAllDevices);

    private void HandleAllDeviceResponse(List<string> lines)
    {
        var newDevices = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (!newDevices.SequenceEqual(_allDevices))
        {
            int old = _allDevices.Count;
            int @new = newDevices.Count;
            var added = newDevices.Except(_allDevices).ToList();
            var removed = _allDevices.Except(newDevices).ToList();
            _allDevices = newDevices;
            AnyDeviceChanged?.Invoke(this, new DeviceChange
            {
                Old = old,
                New = @new,
                Added = added,
                Removed = removed
            });
        }
    }
}
