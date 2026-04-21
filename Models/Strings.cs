namespace VoicemeeterWindowsVolume.Models;

public static class AppStrings
{
    public const string AppName = "voicemeeter-windows-volume";
    public const string FriendlyName = "Voicemeeter Windows Volume";
    public const string Version = "1.8.0.2";

    public static class MenuItems
    {
        public const string VmTitle = "VOICEMEETER";
        public const string SupportTitle = "SUPPORT";
        public const string TitleInputs = "INPUTS";
        public const string TitleOutputs = "OUTPUTS";
        public const string TitleSettings = "SETTINGS";
        public const string TitleDriverWorkarounds = "PATCHES AND WORKAROUNDS";
        public const string ListBindings = "Bind Windows Volume To...";
        public const string ListRestarts = "Auto-Restart Audio Engine...";
        public const string ListPatches = "Settings and Patches...";
        public const string ShowVoicemeeter = "Show Voicemeeter";
        public const string RestartVoicemeeter = "Restart Voicemeeter";
        public const string RestartAudioEngine = "Restart Audio Engine";
        public const string VisitGithub = "Visit Github";
        public const string Donate = "Donate";
        public const string OpenApplicationFolder = "Open Application Folder";
        public const string Exit = "Exit";
        public const string StartWithWindows = "Start With Windows";
        public const string LimitDbGain = "Limit Max Gain to 0dB";
        public const string LinearVolumeScale = "Use Linear Volume Scale";
        public const string SyncMute = "Sync Mute State";
        public const string RestoreVolume = "Restore Volume On Launch";
        public const string PreventVolumeSpikes = "Prevent Volume Spikes";
        public const string CrackleFix = "Crackle Fix (audiodg priority)";
    }

    public static class Console
    {
        public const string RestartAudioEngine = "Restarting audio engine. Reason: {0}";

        public static class RestartReasons
        {
            public const string UserInput = "User Input";
            public const string AppLaunch = "App Launch";
            public const string DeviceChange = "Audio Device Connection Changes";
            public const string AnyDeviceChange = "Any Device Connection Changes";
            public const string Resume = "Resume From Standby";
            public const string ModernResume = "Resume From Modern Standby";
            public const string MonitorResume = "Monitor Resumed From Standby";
        }

        public static class DeviceMessages
        {
            public const string Added = "Added devices:";
            public const string Removed = "Removed devices:";
        }
    }

    public static class VoicemeeterFriendlyNames
    {
        public static readonly Dictionary<string, List<string>> VoicemeeterStrips = new()
        {
            ["voicemeeter"] = new() { "Hardware 1", "Hardware 2", "Virtual 1 [VAIO]" },
            ["voicemeeterBanana"] = new() { "Hardware 1", "Hardware 2", "Hardware 3", "Virtual 1 [VAIO]", "Virtual 2 [AUX]" },
            ["voicemeeterPotato"] = new() { "Hardware 1", "Hardware 2", "Hardware 3", "Hardware 4", "Hardware 5", "Virtual 1 [VAIO]", "Virtual 2 [AUX]", "Virtual 3 [VAIO 3]" },
        };

        public static readonly Dictionary<string, List<string>> VoicemeeterBuses = new()
        {
            ["voicemeeter"] = new() { "Hardware A1", "Hardware A2" },
            ["voicemeeterBanana"] = new() { "Hardware A1", "Hardware A2", "Hardware A3", "Virtual 1 [VAIO]", "Virtual 2 [AUX]" },
            ["voicemeeterPotato"] = new() { "Hardware A1", "Hardware A2", "Hardware A3", "Hardware A4", "Hardware A5", "Virtual 1 [VAIO]", "Virtual 2 [AUX]", "Virtual 3 [VAIO 3]" },
        };
    }
}
