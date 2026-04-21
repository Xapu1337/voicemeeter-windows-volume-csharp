using System.Text.Json;
using VoicemeeterWindowsVolume.Models;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// Manages loading and saving application settings to/from disk.
/// MVC Controller: handles settings state and persistence.
/// </summary>
public class SettingsController
{
    private static SettingsController? _instance;
    public static SettingsController Instance => _instance ??= new SettingsController();

    private AppSettings _settings = new();
    private string _settingsFilePath = "";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public AppSettings GetSettings() => _settings;

    public void SetSettings(AppSettings settings) => _settings = settings;

    public bool IsToggleChecked(string sid) => _settings.GetToggle(sid);

    public void LoadSettings(string settingsPath, AppSettings defaults, Action? callback = null)
    {
        _settingsFilePath = Path.GetFullPath(settingsPath);

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            // backfill missing toggles from defaults
            bool backfilled = false;
            foreach (var defaultToggle in defaults.Toggles)
            {
                if (!loaded.Toggles.Any(t => t.Setting == defaultToggle.Setting))
                {
                    loaded.Toggles.Add(defaultToggle);
                    backfilled = true;
                }
            }

            _settings = loaded;

            if (backfilled)
            {
                SaveSettings();
                System.Console.WriteLine($"Updated settings file: {_settingsFilePath}");
            }
            else
            {
                System.Console.WriteLine($"Using settings file: {_settingsFilePath}");
            }
        }
        catch
        {
            System.Console.WriteLine($"Creating settings file: {_settingsFilePath}");
            _settings = defaults;
            SaveSettings();
        }

        callback?.Invoke();
    }

    public void SaveSettings()
    {
        if (string.IsNullOrEmpty(_settingsFilePath)) return;
        string json = JsonSerializer.Serialize(_settings, _jsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Sync a toggle value from the tray menu back into settings, then persist.
    /// </summary>
    public void UpdateToggle(string sid, bool value)
    {
        _settings.SetToggle(sid, value);
        SaveSettings();
    }
}
