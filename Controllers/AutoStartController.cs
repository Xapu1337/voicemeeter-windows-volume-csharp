using Microsoft.Win32;
using VoicemeeterWindowsVolume.Models;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// Manages auto-start at login via the HKCU Run registry key.
/// No elevation required; scoped to current user.
/// </summary>
public static class AutoStartController
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string ExePath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "VMWV.exe"));

    public static void EnableStartOnLaunch()
    {
        System.Console.WriteLine("Enabling automatic start with Windows");
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.SetValue(AppStrings.AppName, $"\"{ExePath}\"");
    }

    public static void DisableStartOnLaunch()
    {
        System.Console.WriteLine("Disabling automatic start with Windows");
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppStrings.AppName, throwOnMissingValue: false);
    }

    /// <summary>Returns true if the run entry currently exists and points to this exe.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppStrings.AppName) is string val &&
               val.Trim('"') == ExePath;
    }
}
