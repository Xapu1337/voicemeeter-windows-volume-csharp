using System.Text.Json.Serialization;

namespace VoicemeeterWindowsVolume.Models;

public class ToggleSetting
{
    [JsonPropertyName("setting")]
    public string Setting { get; set; } = "";

    [JsonPropertyName("value")]
    public bool Value { get; set; }
}

public class AudiodgSettings
{
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 128; // HIGH

    [JsonPropertyName("affinity")]
    public int Affinity { get; set; } = 2;
}

public class AppSettings
{
    [JsonPropertyName("polling_rate")]
    public int PollingRate { get; set; } = 100;

    [JsonPropertyName("gain_min")]
    public float GainMin { get; set; } = -60f;

    [JsonPropertyName("gain_max")]
    public float GainMax { get; set; } = 12f;

    [JsonPropertyName("start_with_windows")]
    public bool StartWithWindows { get; set; } = true;

    [JsonPropertyName("limit_db_gain_to_0")]
    public bool LimitDbGainTo0 { get; set; } = false;

    [JsonPropertyName("sync_mute")]
    public bool SyncMute { get; set; } = true;

    [JsonPropertyName("remember_volume")]
    public bool RememberVolume { get; set; } = false;

    [JsonPropertyName("disable_donate")]
    public bool DisableDonate { get; set; } = false;

    [JsonPropertyName("initial_volume")]
    public int? InitialVolume { get; set; }

    [JsonPropertyName("audiodg")]
    public AudiodgSettings Audiodg { get; set; } = new();

    [JsonPropertyName("toggles")]
    public List<ToggleSetting> Toggles { get; set; } = new()
    {
        new ToggleSetting { Setting = "restart_audio_engine_on_device_change", Value = false },
        new ToggleSetting { Setting = "restart_audio_engine_on_app_launch", Value = false },
    };

    [JsonPropertyName("device_blacklist")]
    public List<string> DeviceBlacklist { get; set; } = new()
    {
        "Microsoft Streaming Service Proxy", "Volume", "Xvd"
    };

    // Runtime helpers (not serialized)
    public bool GetToggle(string sid)
    {
        var t = Toggles.FirstOrDefault(x => x.Setting == sid);
        return t?.Value ?? false;
    }

    public void SetToggle(string sid, bool value)
    {
        var t = Toggles.FirstOrDefault(x => x.Setting == sid);
        if (t != null)
            t.Value = value;
        else
            Toggles.Add(new ToggleSetting { Setting = sid, Value = value });
    }
}
