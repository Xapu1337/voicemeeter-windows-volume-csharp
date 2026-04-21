using System.Diagnostics;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// Runs PowerShell commands (one-shot and persistent worker model).
/// </summary>
public static class PowerShellRunner
{
    private static readonly Dictionary<string, Process> _hosts = new();
    private static readonly Dictionary<string, System.Threading.Timer> _workers = new();

    /// <summary>
    /// Runs a one-shot PowerShell command and optionally calls back with output.
    /// </summary>
    public static void Run(string command, Action<string>? callback = null, bool logOutput = false)
    {
        // Encode the command as Base64 UTF-16 to avoid quote-escaping issues
        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var proc = new Process { StartInfo = psi };
        proc.Start();

        string output = proc.StandardOutput.ReadToEnd();
        string error = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (logOutput && !string.IsNullOrEmpty(output))
            System.Console.WriteLine(output);

        callback?.Invoke(output);
    }

    /// <summary>
    /// Creates a long-running PowerShell host with stdin/stdout pipes.
    /// Parses control markers {{label:start}} / {{label:end}} to batch output lines.
    /// </summary>
    public static Process CreateHost(string label, Action<List<string>>? onResponse)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = "-Mta -NoProfile",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();
        proc.StandardInput.AutoFlush = true;

        _hosts[label] = proc;
        System.Console.WriteLine($"Started PowerShell worker \"{label}\" PID: {proc.Id}");

        // Read output on a background thread
        Task.Run(() =>
        {
            var streamed = new List<string>();
            bool capturing = false;
            // Format: {{label:start}} / {{label:end}}  (matches the echo markers below)
            var startPattern = "{{" + label + ":start}}";
            var endPattern = "{{" + label + ":end}}";

            while (!proc.StandardOutput.EndOfStream)
            {
                string? raw = proc.StandardOutput.ReadLine();
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line == startPattern)
                {
                    capturing = true;
                    streamed.Clear();
                }
                else if (line == endPattern)
                {
                    capturing = false;
                    onResponse?.Invoke(new List<string>(streamed));
                    streamed.Clear();
                }
                else if (capturing)
                {
                    streamed.Add(line);
                }
            }
        });

        return proc;
    }

    /// <summary>
    /// Starts a repeating PowerShell worker. Optionally runs a one-time setup block first.
    /// </summary>
    public static void StartWorker(string label, string command, int intervalMs,
        Action<List<string>>? onResponse, string? setup = null)
    {
        if (_hosts.ContainsKey(label) || _workers.ContainsKey(label)) return;

        var proc = CreateHost(label, onResponse);

        // run setup code once
        if (!string.IsNullOrEmpty(setup))
        {
            proc.StandardInput.WriteLine(setup);
            proc.StandardInput.Flush();
        }

        string formattedCmd = FormatCommand(command);
        // Markers use format {{label:start}} / {{label:end}} — must match startPattern/endPattern above
        string startMarker = "echo \"{{" + label + ":start}}\"; ";
        string endMarker = "; echo \"{{" + label + ":end}}\"";
        string fullCmd = startMarker + formattedCmd + endMarker;

        // When setup code is present (e.g. Add-Type compilation), delay the first poll
        // long enough for PowerShell to finish compiling before we send real commands.
        int initialDelay = string.IsNullOrEmpty(setup) ? intervalMs : 5000;

        var timer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (!proc.HasExited)
                    proc.StandardInput.WriteLine(fullCmd);
            }
            catch { /* process may have exited */ }
        }, null, initialDelay, intervalMs);

        _workers[label] = timer;
    }

    /// <summary>
    /// Sends a one-off command to an existing named PowerShell host.
    /// </summary>
    public static void SendToWorker(string label, string command)
    {
        if (_hosts.TryGetValue(label, out var proc) && !proc.HasExited)
            proc.StandardInput.WriteLine(command);
    }

    /// <summary>
    /// Stops and removes a named worker and its host process.
    /// </summary>
    public static void StopWorker(string label)
    {
        if (_workers.TryGetValue(label, out var timer))
        {
            timer.Dispose();
            _workers.Remove(label);
        }
        if (_hosts.TryGetValue(label, out var proc))
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            _hosts.Remove(label);
            System.Console.WriteLine($"Killed PowerShell worker \"{label}\"");
        }
    }

    public static void StopAllWorkers()
    {
        foreach (var label in _hosts.Keys.ToList())
            StopWorker(label);
    }

    private static string FormatCommand(string cmd)
        => cmd.ReplaceLineEndings(" ").Trim();
}
