using VoicemeeterWindowsVolume.Models;
using VoicemeeterWindowsVolume.Views;
using VoicemeeterWindowsVolume.Workers;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// Orchestrates audio synchronization between Windows volume and Voicemeeter.
/// MVC Controller: core business logic coordinating workers and Voicemeeter API.
/// 
/// Note: This uses the VoicemeeterRemote C API via P/Invoke through a wrapper.
/// For a full Voicemeeter SDK integration you would reference the official
/// VoicemeeterRemote.dll - here we provide the integration scaffold.
/// </summary>
public class AudioSyncController
{
    private static AudioSyncController? _instance;
    public static AudioSyncController Instance => _instance ??= new AudioSyncController();

    private VoicemeeterBridge? _vm;
    private bool _voicemeeterLoaded;
    private long _lastEventTimestamp;
    private System.Threading.Timer? _engineWaiter;

    private int? _lastVolume;
    private long _lastVolumeTime;

    public VoicemeeterBridge? GetVoicemeeterConnection() => _vm;

    private readonly SettingsController _settings = SettingsController.Instance;
    private readonly WindowsAudioScanner _audio = WindowsAudioScanner.Instance;

    public void StartAudioSync()
    {
        _audio.Started += (_, _) => SetInitialVolume();

        ConnectVoicemeeter();
    }

    private void ConnectVoicemeeter()
    {
        ProcessController.WaitForProcess(@"voicemeeter(?!.*setup).*\.exe", () =>
        {
            try
            {
                _vm = new VoicemeeterBridge();
                _vm.Connect();

                _lastEventTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                _vm.OnParametersChange += () =>
                {
                    if (!_voicemeeterLoaded)
                        _lastEventTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    OnVoicemeeterChanged();
                };

                // Wait until events stop firing for 3s to consider VM fully loaded
                _engineWaiter = new System.Threading.Timer(_ =>
                {
                    long delta = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastEventTimestamp;
                    if (delta >= 3000)
                    {
                        _engineWaiter?.Dispose();
                        _voicemeeterLoaded = true;
                        System.Console.WriteLine("Voicemeeter: Fully Initialized");
                        OnVoicemeeterReady();
                    }
                }, null, 1000, 1000);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error connecting to Voicemeeter: {ex.Message}");
                Application.Exit();
            }
        });
    }

    private void OnVoicemeeterReady()
    {
        RunWinAudio();
        TrayViewController.Instance.UpdateBindingLabels();

        if (_settings.IsToggleChecked("restart_audio_engine_on_app_launch"))
        {
            System.Console.WriteLine(string.Format(
                AppStrings.Console.RestartAudioEngine,
                AppStrings.Console.RestartReasons.AppLaunch));
            _vm?.SendCommand("Restart", 1);
        }
    }

    private void OnVoicemeeterChanged()
    {
        TrayViewController.Instance.UpdateBindingLabels();
    }

    private void SetInitialVolume()
    {
        var settings = _settings.GetSettings();
        if (_settings.IsToggleChecked("remember_volume") && settings.InitialVolume.HasValue)
        {
            _lastVolumeTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            System.Console.WriteLine($"Set initial volume to {settings.InitialVolume}%");
            _audio.SetVolume(settings.InitialVolume.Value);
        }
    }

    public void RememberCurrentVolume()
    {
        int? vol = _audio.GetVolume();
        if (vol == null) return;
        System.Console.WriteLine($"remembering volume: {vol}");
        var settings = _settings.GetSettings();
        settings.InitialVolume = vol;
        _settings.SetSettings(settings);
        _settings.SaveSettings();
    }

    private void RunWinAudio()
    {
        var settings = _settings.GetSettings();
        _audio.StartAudioScanner(settings.PollingRate);

        _audio.VolumeChanged += (_, volume) =>
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long timeSinceLastVolume = currentTime - _lastVolumeTime;

            // Volume spike prevention
            if (volume.New == 100 && timeSinceLastVolume >= 1000)
            {
                bool fixingVolume = _settings.IsToggleChecked("apply_volume_fix");
                if (fixingVolume && _lastVolume.HasValue && settings.InitialVolume != 100)
                {
                    System.Console.WriteLine(
                        $"Driver Anomaly Detected: Volume reached 100% from {_lastVolume}%. Reverting to {_lastVolume}%");
                    _audio.SetVolume(_lastVolume.Value);
                }
            }

            _lastVolume = volume.New;
            _lastVolumeTime = currentTime;

            if (_settings.IsToggleChecked("remember_volume"))
                RememberCurrentVolume();

            // Propagate to Voicemeeter
            if (_vm != null)
            {
                foreach (var binding in TrayViewController.Instance.GetActiveBindings())
                {
                    float gain = _settings.IsToggleChecked("linear_volume_scale")
                        ? ConvertVolumeLinear(volume.New, settings.GainMin, settings.GainMax)
                        : ConvertVolumeLogarithmic(volume.New, settings.GainMin, settings.GainMax);

                    var tokens = binding.Split('_');
                    if (tokens.Length == 2)
                    {
                        try { _vm.SetParameter(tokens[0], int.Parse(tokens[1]), "Gain", gain); }
                        catch { /* ignore parameter errors */ }
                    }
                }
            }
        };

        _audio.MuteChanged += (_, status) =>
        {
            if (!_settings.IsToggleChecked("sync_mute") || _vm == null) return;
            int isMute = status.New ? 1 : 0;
            foreach (var binding in TrayViewController.Instance.GetActiveBindings())
            {
                var tokens = binding.Split('_');
                if (tokens.Length == 2)
                {
                    try { _vm.SetParameter(tokens[0], int.Parse(tokens[1]), "Mute", isMute); }
                    catch { /* ignore */ }
                }
            }
        };
    }

    private float ConvertVolumeLinear(int windowsVolume, float gainMin, float gainMax)
    {
        if (_settings.IsToggleChecked("limit_db_gain_to_0")) gainMax = 0;
        float gain = (windowsVolume * (gainMax - gainMin)) / 100f + gainMin;
        return MathF.Round(gain * 10f) / 10f;
    }

    private float ConvertVolumeLogarithmic(int windowsVolume, float gainMin, float gainMax)
    {
        if (_settings.IsToggleChecked("limit_db_gain_to_0")) gainMax = 0;
        float amp = windowsVolume > 0
            ? MathF.Log10(windowsVolume / 100f)
            : -1000f;
        float gain = MathF.Max(20f * amp + gainMax, gainMin);
        return MathF.Round(gain * 10f) / 10f;
    }

    public void Disconnect()
    {
        _engineWaiter?.Dispose();
        _vm?.Disconnect();
        _vm = null;
    }
}
