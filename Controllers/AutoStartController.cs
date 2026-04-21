using VoicemeeterWindowsVolume.Models;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// Manages Windows scheduled task for auto-starting the app at login.
/// MVC Controller: wraps startup management side effects.
/// </summary>
public static class AutoStartController
{
    public static void EnableStartOnLaunch()
    {
        System.Console.WriteLine("Enabling automatic start with Windows");

        string appPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, $"{AppStrings.AppName}.ps1")
        );

        string psCommand =
            $"$name = \"{AppStrings.AppName}\";\n" +
            $"$description = \"Runs {AppStrings.FriendlyName} app at login\";\n" +
            $"$argument = '-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{appPath}\" -FFFeatureOff';\n" +
            "$action = New-ScheduledTaskAction -Execute \"powershell.exe\" -Argument $argument;\n" +
            "$trigger = New-ScheduledTaskTrigger -AtLogon;\n" +
            "$principal = New-ScheduledTaskPrincipal -GroupId \"BUILTIN\\Administrators\" -RunLevel Highest;\n" +
            "$settings = New-ScheduledTaskSettingsSet -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries -DontStopOnIdleEnd -ExecutionTimeLimit 0;\n" +
            "$task = New-ScheduledTask -Description $description -Action $action -Principal $principal -Trigger $trigger -Settings $settings;\n" +
            "Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue;\n" +
            "Register-ScheduledTask $name -InputObject $task;";

        PowerShellRunner.Run(psCommand);
    }

    public static void DisableStartOnLaunch()
    {
        System.Console.WriteLine("Disabling automatic start with Windows");

        string psCommand =
            $"$name = \"{AppStrings.AppName}\";\n" +
            "Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue;";

        PowerShellRunner.Run(psCommand);
    }
}
