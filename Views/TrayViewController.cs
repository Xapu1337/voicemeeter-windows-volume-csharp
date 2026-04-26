using VoicemeeterWindowsVolume.Controllers;
using VoicemeeterWindowsVolume.Models;
using VoicemeeterWindowsVolume.Workers;

namespace VoicemeeterWindowsVolume.Views;

/// <summary>
/// MVC View: Builds and manages the system tray context menu.
/// All menu item construction lives here, mirroring the JS menu item modules.
/// Breaks MVC as we dont really have a "View", so this does more than a view class would.
/// </summary>
public class TrayViewController : IDisposable
{
    private static TrayViewController? _instance;
    public static TrayViewController Instance => _instance ??= new TrayViewController();

    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;

    // Invisible helper form used as a stable InvokeRequired target (ContextMenuStrip
    // has no HWND until shown, so BeginInvoke on it would throw).
    private Form? _syncForm;
    // Binding checkboxes keyed by their sid (e.g. "Strip_0", "Bus_1")
    private readonly Dictionary<string, ToolStripMenuItem> _bindingItems = new();

    // Toggle checkboxes keyed by their sid
    private readonly Dictionary<string, ToolStripMenuItem> _toggleItems = new();

    // Status item shown when Voicemeeter is not connected
    private ToolStripMenuItem? _vmNotDetectedItem;

    private readonly SettingsController _settings = SettingsController.Instance;

    // -------------------------------------------------------------------------
    // Public API used by AudioSyncController
    // -------------------------------------------------------------------------

    public IEnumerable<string> GetActiveBindings()
        => _bindingItems.Where(kv => kv.Value.Checked).Select(kv => kv.Key);

    public void UpdateBindingLabels()
    {
        if (_syncForm == null || !_syncForm.IsHandleCreated) return;
        _syncForm.BeginInvoke(RefreshBindingLabels);
    }

    public void ShowVmNotDetected()
    {
        if (_syncForm == null || !_syncForm.IsHandleCreated) return;
        _syncForm.BeginInvoke(() =>
        {
            if (_vmNotDetectedItem != null)
                _vmNotDetectedItem.Visible = true;
        });
    }

    public void HideVmNotDetected()
    {
        if (_syncForm == null || !_syncForm.IsHandleCreated) return;
        _syncForm.BeginInvoke(() =>
        {
            if (_vmNotDetectedItem != null)
                _vmNotDetectedItem.Visible = false;
        });
    }

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    public void Initialize(string iconColor = "default")
    {
        // Invisible sync form — created on the STA thread, has a real HWND,
        // used for safe cross-thread UI invocations.
        _syncForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Size = new System.Drawing.Size(1, 1),
            WindowState = FormWindowState.Minimized,
        };
        _syncForm.Load += (_, _) => _syncForm.Hide();
        _syncForm.Show(); // creates HWND

        _contextMenu = new ContextMenuStrip();
        BuildMenu(_contextMenu);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(iconColor),
            Text = AppStrings.FriendlyName,
            ContextMenuStrip = _contextMenu,
            Visible = true,
        };
    }

    private static System.Drawing.Icon LoadIcon(string color)
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", $"app-{color}.ico");
        if (File.Exists(iconPath))
            return new System.Drawing.Icon(iconPath);
        return SystemIcons.Application;
    }

    // -------------------------------------------------------------------------
    // Menu construction
    // -------------------------------------------------------------------------

    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Add(new ToolStripMenuItem(
            $"{AppStrings.FriendlyName.ToUpperInvariant()}\t V{AppStrings.Version}")
        { Enabled = false });

        menu.Items.Add(new ToolStripSeparator());

        // Shown while Voicemeeter is not connected; hidden once the bridge connects
        _vmNotDetectedItem = new ToolStripMenuItem("VoiceMeeter not detected.") { Enabled = false };
        menu.Items.Add(_vmNotDetectedItem);

        // Bindings submenu
        menu.Items.Add(BuildBindingsMenu());

        // Restarts submenu
        menu.Items.Add(BuildRestartsMenu());

        // Patches / Settings submenu
        menu.Items.Add(BuildPatchesMenu());

        menu.Items.Add(new ToolStripSeparator());

        // Voicemeeter section
        menu.Items.Add(new ToolStripMenuItem(AppStrings.MenuItems.VmTitle) { Enabled = false });
        menu.Items.Add(ItemShowVoicemeeter());
        menu.Items.Add(ItemRestartVoicemeeter());
        menu.Items.Add(ItemRestartAudioEngine());

        menu.Items.Add(new ToolStripSeparator());

        // Support section
        menu.Items.Add(new ToolStripMenuItem(AppStrings.MenuItems.SupportTitle) { Enabled = false });
        menu.Items.Add(ItemVisitGithub());
        menu.Items.Add(ItemDonate());

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(ItemOpenApplicationFolder());
        menu.Items.Add(ItemExit());

        // Apply saved toggle states after building
        ApplySavedToggles();
    }

    // -------------------------------------------------------------------------
    // Bindings submenu
    // -------------------------------------------------------------------------

    private ToolStripMenuItem BuildBindingsMenu()
    {
        var sub = new ToolStripMenuItem(AppStrings.MenuItems.ListBindings);
        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleInputs) { Enabled = false });

        for (int i = 0; i <= 7; i++)
        {
            string sid = $"Strip_{i}";
            var item = new ToolStripMenuItem($"Input Strip {i}") { CheckOnClick = true, Checked = false };
            item.Click += (_, _) => OnToggleChanged(sid, item.Checked);
            _bindingItems[sid] = item;
            sub.DropDownItems.Add(item);
        }

        sub.DropDownItems.Add(new ToolStripSeparator());
        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleOutputs) { Enabled = false });

        for (int i = 0; i <= 7; i++)
        {
            string sid = $"Bus_{i}";
            var item = new ToolStripMenuItem($"Output Bus {i}") { CheckOnClick = true, Checked = false };
            item.Click += (_, _) => OnToggleChanged(sid, item.Checked);
            _bindingItems[sid] = item;
            sub.DropDownItems.Add(item);
        }

        return sub;
    }

    // -------------------------------------------------------------------------
    // Restarts submenu
    // -------------------------------------------------------------------------

    private ToolStripMenuItem BuildRestartsMenu()
    {
        var sub = new ToolStripMenuItem(AppStrings.MenuItems.ListRestarts);

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_device_change",
            title: AppStrings.Console.RestartReasons.DeviceChange,
            onActivate: checked_ =>
            {
                if (checked_) WindowsAudioScanner.Instance.StartAudioDeviceScanner();
                else WindowsAudioScanner.Instance.StopAudioDeviceScanner();
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_any_device_change",
            title: AppStrings.Console.RestartReasons.AnyDeviceChange,
            onActivate: checked_ =>
            {
                if (checked_)
                {
                    WindowsAudioScanner.Instance.StopAudioDeviceScanner();
                    WindowsAudioScanner.Instance.StartAllDeviceScanner();
                }
                else
                {
                    if (_settings.IsToggleChecked("restart_audio_engine_on_device_change"))
                        WindowsAudioScanner.Instance.StartAudioDeviceScanner();
                    WindowsAudioScanner.Instance.StopAllDeviceScanner();
                }
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_resume",
            title: AppStrings.Console.RestartReasons.Resume,
            onActivate: checked_ =>
            {
                if (checked_) Workers.WindowsEventScanner.Instance.StartWindowsEventScanner();
                else Workers.WindowsEventScanner.Instance.StopWindowsEventScanner();
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "restart_audio_engine_on_app_launch",
            title: AppStrings.Console.RestartReasons.AppLaunch
        ));

        // Wire up device change events to restart logic
        WindowsAudioScanner.Instance.AudioDeviceChanged += (_, devices) =>
        {
            bool enabled = _settings.IsToggleChecked("restart_audio_engine_on_device_change") &&
                           !_settings.IsToggleChecked("restart_audio_engine_on_any_device_change");
            if (enabled && devices.New > 0)
            {
                Task.Delay(1000).ContinueWith(_ =>
                {
                    System.Console.WriteLine(string.Format(AppStrings.Console.RestartAudioEngine,
                        AppStrings.Console.RestartReasons.DeviceChange));
                    AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);
                });
            }
        };

        WindowsAudioScanner.Instance.AnyDeviceChanged += (_, devices) =>
        {
            if (_settings.IsToggleChecked("restart_audio_engine_on_any_device_change") && devices.New > 0)
            {
                Task.Delay(1000).ContinueWith(_ =>
                {
                    System.Console.WriteLine(string.Format(AppStrings.Console.RestartAudioEngine,
                        AppStrings.Console.RestartReasons.AnyDeviceChange));
                    System.Console.WriteLine($"{AppStrings.Console.DeviceMessages.Added} {string.Join(", ", devices.Added)}");
                    System.Console.WriteLine($"{AppStrings.Console.DeviceMessages.Removed} {string.Join(", ", devices.Removed)}");
                    AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);
                });
            }
        };

        // Wire up Windows resume events
        Workers.WindowsEventScanner.Instance.Resume += (_, _) => RestartVmForReason(AppStrings.Console.RestartReasons.Resume);
        Workers.WindowsEventScanner.Instance.ModernResume += (_, _) => RestartVmForReason(AppStrings.Console.RestartReasons.ModernResume);
        Workers.WindowsEventScanner.Instance.MonitorResume += (_, _) => RestartVmForReason(AppStrings.Console.RestartReasons.MonitorResume);

        return sub;
    }

    private void RestartVmForReason(string reason)
    {
        Task.Delay(1000).ContinueWith(_ =>
        {
            System.Console.WriteLine(string.Format(AppStrings.Console.RestartAudioEngine, reason));
            AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);

            if (_settings.IsToggleChecked("apply_crackle_fix"))
            {
                Task.Delay(3000).ContinueWith(__ => ApplyCrackleFix(true));
            }
        });
    }

    // -------------------------------------------------------------------------
    // Settings & Patches submenu
    // -------------------------------------------------------------------------

    private ToolStripMenuItem BuildPatchesMenu()
    {
        var sub = new ToolStripMenuItem(AppStrings.MenuItems.ListPatches);

        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleSettings) { Enabled = false });

        sub.DropDownItems.Add(ToggleItem(
            sid: "start_with_windows",
            title: AppStrings.MenuItems.StartWithWindows,
            defaultChecked: AutoStartController.IsEnabled(),
            onActivate: checked_ =>
            {
                if (checked_) AutoStartController.EnableStartOnLaunch();
                else AutoStartController.DisableStartOnLaunch();
            },
            initIfChecked: false
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "limit_db_gain_to_0",
            title: AppStrings.MenuItems.LimitDbGain,
            onActivate: checked_ =>
                System.Console.WriteLine(checked_ ? "Limiting max gain to 0dB" : "No longer limiting max gain to 0dB")
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "linear_volume_scale",
            title: AppStrings.MenuItems.LinearVolumeScale,
            onActivate: checked_ =>
                System.Console.WriteLine(checked_ ? "Now using linear volume scaling" : "Now using logarithmic volume scaling")
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "sync_mute",
            title: AppStrings.MenuItems.SyncMute,
            defaultChecked: true,
            onActivate: checked_ =>
                System.Console.WriteLine(checked_ ? "Syncing mute" : "No longer syncing mute")
        ));

        sub.DropDownItems.Add(new ToolStripSeparator());
        sub.DropDownItems.Add(new ToolStripMenuItem(AppStrings.MenuItems.TitleDriverWorkarounds) { Enabled = false });

        sub.DropDownItems.Add(ToggleItem(
            sid: "remember_volume",
            title: AppStrings.MenuItems.RestoreVolume,
            onActivate: checked_ =>
            {
                if (checked_) AudioSyncController.Instance.RememberCurrentVolume();
            }
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "apply_volume_fix",
            title: AppStrings.MenuItems.PreventVolumeSpikes
        ));

        sub.DropDownItems.Add(ToggleItem(
            sid: "apply_crackle_fix",
            title: AppStrings.MenuItems.CrackleFix,
            onActivate: checked_ => ApplyCrackleFix(checked_),
            initIfChecked: true
        ));

        return sub;
    }

    private void ApplyCrackleFix(bool enabled)
    {
        var settings = _settings.GetSettings();
        int priority = settings.Audiodg?.Priority ?? ProcessController.Priorities.High;
        int affinity = settings.Audiodg?.Affinity ?? 2;

        if (enabled)
        {
            System.Console.WriteLine($"Setting audiodg.exe priority to {priority} and affinity to {affinity}");
            ProcessController.SetProcessPriority("audiodg", priority);
            ProcessController.SetProcessAffinity("audiodg", affinity);
        }
        else
        {
            System.Console.WriteLine($"Restoring audiodg.exe priority to {ProcessController.Priorities.Normal} and affinity to 255");
            ProcessController.SetProcessPriority("audiodg", ProcessController.Priorities.Normal);
            ProcessController.SetProcessAffinity("audiodg", 255);
        }
    }

    // -------------------------------------------------------------------------
    // Voicemeeter action items
    // -------------------------------------------------------------------------

    private static ToolStripMenuItem ItemShowVoicemeeter()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.ShowVoicemeeter);
        item.Click += (_, _) => AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Show", 1);
        return item;
    }

    private static ToolStripMenuItem ItemRestartVoicemeeter()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.RestartVoicemeeter);
        item.Click += (_, _) =>
        {
            string? proc = ProcessController.GetRunningProcess(@"voicemeeter(?!.*setup).*\.exe");
            if (proc != null) ProcessController.RestartProcess(proc);

            Task.Delay(7000).ContinueWith(_ =>
            {
                if (SettingsController.Instance.IsToggleChecked("restart_audio_engine_on_app_launch"))
                {
                    System.Console.WriteLine(string.Format(
                        AppStrings.Console.RestartAudioEngine,
                        AppStrings.Console.RestartReasons.AppLaunch));
                    AudioSyncController.Instance.GetVoicemeeterConnection()?.SendCommand("Restart", 1);
                }
            });
        };
        return item;
    }

    private static ToolStripMenuItem ItemRestartAudioEngine()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.RestartAudioEngine);
        item.Click += (_, _) =>
        {
            var vm = AudioSyncController.Instance.GetVoicemeeterConnection();
            if (vm != null)
            {
                System.Console.WriteLine(string.Format(
                    AppStrings.Console.RestartAudioEngine,
                    AppStrings.Console.RestartReasons.UserInput));
                vm.SendCommand("Restart", 1);
            }
        };
        return item;
    }

    // -------------------------------------------------------------------------
    // Support items
    // -------------------------------------------------------------------------

    private ToolStripMenuItem ItemDonate()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.Donate);
        item.Click += (_, _) =>
            PowerShellRunner.Run("Start-Process \"https://www.paypal.com/donate?hosted_button_id=JBDM2H96RNKH8\"");

        // Hide if disabled in settings
        if (_settings.GetSettings().DisableDonate)
            item.Visible = false;

        return item;
    }

    private static ToolStripMenuItem ItemVisitGithub()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.VisitGithub);
        item.Click += (_, _) =>
            PowerShellRunner.Run("Start-Process \"https://github.com/Frosthaven/voicemeeter-windows-volume\"");
        return item;
    }

    // -------------------------------------------------------------------------
    // Utility items
    // -------------------------------------------------------------------------

    private static ToolStripMenuItem ItemOpenApplicationFolder()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.OpenApplicationFolder);
        item.Click += (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", AppContext.BaseDirectory);
        return item;
    }

    private static ToolStripMenuItem ItemExit()
    {
        var item = new ToolStripMenuItem(AppStrings.MenuItems.Exit);
        item.Click += (_, _) => Application.Exit();
        return item;
    }

    // -------------------------------------------------------------------------
    // Toggle helper
    // -------------------------------------------------------------------------

    private ToolStripMenuItem ToggleItem(
        string sid,
        string title,
        bool defaultChecked = false,
        Action<bool>? onActivate = null,
        bool initIfChecked = false)
    {
        var item = new ToolStripMenuItem(title) { CheckOnClick = true, Checked = defaultChecked };
        _toggleItems[sid] = item;

        item.Click += (_, _) =>
        {
            _settings.UpdateToggle(sid, item.Checked);
            // Also sync binding items if they have sids
            onActivate?.Invoke(item.Checked);
        };

        // If the saved setting says checked, activate on init
        if (initIfChecked)
        {
            bool saved = _settings.IsToggleChecked(sid);
            if (saved) onActivate?.Invoke(true);
        }

        return item;
    }

    private void OnToggleChanged(string sid, bool value)
    {
        _settings.UpdateToggle(sid, value);
    }

    // -------------------------------------------------------------------------
    // Apply saved settings to UI after load
    // -------------------------------------------------------------------------

    public void ApplySavedToggles()
    {
        var settings = _settings.GetSettings();
        foreach (var toggle in settings.Toggles)
        {
            if (_toggleItems.TryGetValue(toggle.Setting, out var menuItem))
                menuItem.Checked = toggle.Value;
            if (_bindingItems.TryGetValue(toggle.Setting, out var bindItem))
                bindItem.Checked = toggle.Value;
        }
    }

    // -------------------------------------------------------------------------
    // Binding label refresh from Voicemeeter
    // -------------------------------------------------------------------------

    private System.Threading.Timer? _bindingDebounceTimer;

    private void RefreshBindingLabels()
    {
        // Debounce: wait 5s after last call before actually running
        _bindingDebounceTimer?.Dispose();
        _bindingDebounceTimer = new System.Threading.Timer(_ =>
        {
            var vm = AudioSyncController.Instance.GetVoicemeeterConnection();
            if (vm == null) return;

            var strips = AppStrings.VoicemeeterFriendlyNames.VoicemeeterStrips.GetValueOrDefault(vm.VmType);
            var buses = AppStrings.VoicemeeterFriendlyNames.VoicemeeterBuses.GetValueOrDefault(vm.VmType);

            if (strips == null || buses == null) return;

            _syncForm?.BeginInvoke(() =>
            {
                foreach (var (sid, item) in _bindingItems)
                {
                    var tokens = sid.Split('_');
                    if (tokens.Length != 2 || !int.TryParse(tokens[1], out int idx)) continue;

                    string type = tokens[0];
                    var names = type == "Strip" ? strips : buses;

                    if (idx < names.Count)
                    {
                        string label = vm.GetParameterString(type, idx, "Label");
                        string deviceName = vm.GetParameterString(type, idx, "device.name");
                        string friendlyName = string.IsNullOrEmpty(label) ? names[idx] : label;
                        string full = string.IsNullOrEmpty(deviceName)
                            ? friendlyName
                            : $"{friendlyName} : <{deviceName}>";

                        item.Text = full;
                        item.Visible = true;
                    }
                    else
                    {
                        item.Visible = false;
                    }
                }
            });
        }, null, 5000, Timeout.Infinite);
    }

    // -------------------------------------------------------------------------

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        _contextMenu?.Dispose();
        _bindingDebounceTimer?.Dispose();
        _syncForm?.Dispose();
    }
}
