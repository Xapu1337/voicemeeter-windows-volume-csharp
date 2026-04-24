using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// Manages Windows processes: waiting, restarting, priority and affinity.
/// MVC Controller: wraps process management side effects.
/// </summary>
public static class ProcessController
{
    public static class Priorities
    {
        public const int Realtime = 256;
        public const int High = 128;
        public const int AboveNormal = 32768;
        public const int Normal = 32;
        public const int BelowNormal = 16384;
        public const int Low = 64;
    }

    /// <summary>
    /// Waits until a process matching the regex is running, then invokes the callback.
    /// Uses a WMI event subscription so the thread is idle rather than polling.
    /// Falls back to 5-second polling if WMI is unavailable.
    /// </summary>
    public static void WaitForProcess(string processNamePattern, Action callback)
    {
        Task.Run(() =>
        {
            // Check if already running before subscribing.
            string? found = GetRunningProcess(processNamePattern);
            if (found != null)
            {
                System.Console.WriteLine($"Process found: {found}");
                callback();
                return;
            }

            System.Console.WriteLine($"Waiting for Voicemeeter to start...");

            try
            {
                // WMI fires an event whenever any process is created (WITHIN 2 = 2-second internal poll).
                using var watcher = new ManagementEventWatcher(
                    new WqlEventQuery(
                        "SELECT * FROM __InstanceCreationEvent WITHIN 2 " +
                        "WHERE TargetInstance ISA 'Win32_Process'"));

                var ready = new System.Threading.ManualResetEventSlim(false);

                watcher.EventArrived += (_, args) =>
                {
                    try
                    {
                        var target = (ManagementBaseObject)args.NewEvent["TargetInstance"];
                        string? name = target["Name"]?.ToString();
                        if (name != null && Regex.IsMatch(name, processNamePattern, RegexOptions.IgnoreCase))
                        {
                            System.Console.WriteLine($"Process started: {name}");
                            ready.Set();
                        }
                    }
                    catch { /* ignore malformed WMI events */ }
                };

                watcher.Start();
                ready.Wait();
                watcher.Stop();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"WMI watch unavailable, falling back to polling: {ex.Message}");
                while (GetRunningProcess(processNamePattern) == null)
                    System.Threading.Thread.Sleep(5000);
            }

            System.Console.WriteLine($"Process detected, connecting...");
            callback();
        });
    }

    public static string? GetRunningProcess(string processNamePattern)
    {
        var regex = new Regex(processNamePattern, RegexOptions.IgnoreCase);
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (regex.IsMatch(proc.ProcessName + ".exe"))
                    return proc.ProcessName + ".exe";
            }
            catch { /* access denied on some processes */ }
        }
        return null;
    }

    public static bool IsProcessRunning(string processNamePattern)
        => GetRunningProcess(processNamePattern) != null;

    /// <summary>
    /// Kills and restarts a process by name.
    /// </summary>
    public static void RestartProcess(string processName)
    {
        string name = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        PowerShellRunner.Run(
            $"$processes = Get-Process {name}; " +
            "foreach($process in $processes) { $path = $process.Path; $process.Kill(); $process.WaitForExit(); } " +
            "Start-Process \"$path\""
        );
    }

    /// <summary>
    /// Sets a process priority using WMI.
    /// </summary>
    public static void SetProcessPriority(string processName, int priorityCode)
    {
        string name = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        PowerShellRunner.Run(
            $"Get-WmiObject Win32_process -filter 'name = \"{name}.exe\"' | " +
            $"foreach-object {{ $_.SetPriority({priorityCode}) }}"
        );
    }

    /// <summary>
    /// Sets a process CPU affinity mask using WMI.
    /// </summary>
    public static void SetProcessAffinity(string processName, int affinityMask)
    {
        string name = processName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        PowerShellRunner.Run(
            $"$proc = Get-Process -Name {name}; " +
            $"$proc.ProcessorAffinity = {affinityMask}"
        );
    }
}
