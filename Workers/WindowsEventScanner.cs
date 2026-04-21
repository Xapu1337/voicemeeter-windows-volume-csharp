namespace VoicemeeterWindowsVolume.Workers;

/// <summary>
/// Detects Windows resume-from-sleep events by monitoring SetInterval drift
/// and polling the Windows Event Log.
/// </summary>
public class WindowsEventScanner
{
    private static WindowsEventScanner? _instance;
    public static WindowsEventScanner Instance => _instance ??= new WindowsEventScanner();

    private const string Label = "StandbyScanner";
    private const int IntervalMs = 5000;

    private System.Threading.Timer? _standbyTimer;
    private long _lastTime;
    private long _lastResumeEvent;
    private string? _lastStandbyScan;

    public event EventHandler? Resume;
    public event EventHandler? ModernResume;
    public event EventHandler? MonitorResume;

    public void StartWindowsEventScanner()
    {
        _lastResumeEvent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _lastTime = _lastResumeEvent;

        // Detect resume by interval drift
        _standbyTimer = new System.Threading.Timer(_ =>
        {
            long current = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (current - _lastTime > IntervalMs + 2000)
                EmitEvent("resume");
            _lastTime = current;
        }, null, IntervalMs, IntervalMs);

        Controllers.PowerShellRunner.StartWorker(
            label: Label,
            command:
                "echo \"resume-event:\"; " +
                "Get-EventLog -LogName system -Source \"Microsoft-Windows-Kernel-Power\" -Newest 15 | " +
                "Where-Object {$_.EventID -eq 507} | " +
                "Select-Object -Property Source, TimeWritten, InstanceID | Out-Host; " +
                "echo \"monitor-sleep:\"; powercfg /query; " +
                "echo \"idle-time:\"; [PInvoke.Win32.UserInput]::IdleTime | Out-Host;",
            intervalMs: IntervalMs,
            onResponse: HandleResponse,
            setup: UserInputSetup
        );
    }

    public void StopWindowsEventScanner()
    {
        _standbyTimer?.Dispose();
        _standbyTimer = null;
        Controllers.PowerShellRunner.StopWorker(Label);
    }

    private void EmitEvent(string eventName)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - _lastResumeEvent > 20000)
        {
            _lastResumeEvent = now;
            switch (eventName)
            {
                case "resume": Resume?.Invoke(this, EventArgs.Empty); break;
                case "modern_resume": ModernResume?.Invoke(this, EventArgs.Empty); break;
                case "monitor_resume": MonitorResume?.Invoke(this, EventArgs.Empty); break;
            }
        }
    }

    private void HandleResponse(List<string> lines)
    {
        var buckets = new Dictionary<string, List<string>>();
        string? bucket = null;

        foreach (var line in lines)
        {
            switch (line.Trim())
            {
                case "monitor-sleep:": bucket = "monitor-sleep"; break;
                case "resume-event:": bucket = "resume-event"; break;
                case "idle-time:": bucket = "idle-time"; break;
                default:
                    if (bucket != null)
                    {
                        if (!buckets.ContainsKey(bucket)) buckets[bucket] = new();
                        buckets[bucket].Add(line);
                    }
                    break;
            }
        }

        if (buckets.TryGetValue("resume-event", out var resumeData) && resumeData.Count > 0)
        {
            string scan = string.Join(",", resumeData);
            if (_lastStandbyScan == null)
                _lastStandbyScan = scan;
            else if (scan != _lastStandbyScan)
            {
                EmitEvent("modern_resume");
                _lastStandbyScan = scan;
            }
        }

        if (buckets.TryGetValue("idle-time", out var idleData) &&
            buckets.TryGetValue("monitor-sleep", out var monitorData) &&
            idleData.Count >= 4 && monitorData.Count > 0)
        {
            string monitorStr = string.Join(",", monitorData);
            var match = System.Text.RegularExpressions.Regex.Match(
                monitorStr, @"VIDEOIDLE[\s\S]*?Index:,*([0-9|a-z]*)[\s\S]*?Index:,*([0-9|a-z]*)"
            );
            if (match.Success && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int timeout) && timeout > 0)
            {
                var idleMatch = System.Text.RegularExpressions.Regex.Match(idleData[3], @"(\d+)");
                if (idleMatch.Success && int.TryParse(idleMatch.Groups[1].Value, out int idleSec) && idleSec < 5)
                    EmitEvent("monitor_resume");
            }
        }
    }

    private static readonly string UserInputSetup = @"
Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PInvoke.Win32 {
    public static class UserInput {
        [DllImport(""user32.dll"", SetLastError=false)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO {
            public uint cbSize;
            public int dwTime;
        }

        public static DateTime LastInput {
            get {
                DateTime bootTime = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount);
                DateTime lastInput = bootTime.AddMilliseconds(LastInputTicks);
                return lastInput;
            }
        }

        public static TimeSpan IdleTime {
            get { return DateTime.UtcNow.Subtract(LastInput); }
        }

        public static int LastInputTicks {
            get {
                LASTINPUTINFO lii = new LASTINPUTINFO();
                lii.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                GetLastInputInfo(ref lii);
                return lii.dwTime;
            }
        }
    }
}
'@
";
}
