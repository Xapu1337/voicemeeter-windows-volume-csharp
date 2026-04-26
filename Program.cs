using VoicemeeterWindowsVolume.Controllers;
using VoicemeeterWindowsVolume.Models;
using VoicemeeterWindowsVolume.Views;

namespace VoicemeeterWindowsVolume;

/// <summary>
/// Application entry point. Wires up MVC components and starts the message loop.
/// </summary>
internal static class Program
{
    private static readonly string SettingsPath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "vmwv-crash.log");

    private static readonly string AppLogPath =
        Path.Combine(AppContext.BaseDirectory, "vmwv.log");

    [STAThread]
    static void Main()
    {
        // Redirect Console.WriteLine to vmwv.log (timestamped, auto-flush)
        var logWriter = new TimestampedFileWriter(AppLogPath);
        Console.SetOut(logWriter);

        // Catch all unhandled exceptions and log them before exiting
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal(e.ExceptionObject?.ToString() ?? "Unknown error");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal($"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        try
        {
            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                LogFatal($"UI thread exception: {e.Exception}");
                MessageBox.Show(e.Exception.Message, AppStrings.FriendlyName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            System.Console.WriteLine(
                $"Voicemeeter Windows Volume started, Process ID: {Environment.ProcessId}");

            // Detect system theme for icon color
            string iconColor = GetSystemColor();

            // Initialize View (must be on STA thread before Application.Run)
            TrayViewController.Instance.Initialize(iconColor);

            // Load settings, apply to UI, then start audio sync
            SettingsController.Instance.LoadSettings(
                settingsPath: SettingsPath,
                defaults: new AppSettings(),
                callback: () =>
                {
                    TrayViewController.Instance.ApplySavedToggles();
                    System.Console.WriteLine("Starting audio synchronization");
                    AudioSyncController.Instance.StartAudioSync();
                }
            );

            // ApplicationContext keeps the WinForms message pump alive without a main form.
            // Application.Run() with no args exits immediately — we need the context.
            var ctx = new TrayApplicationContext();
            Application.Run(ctx);
        }
        catch (Exception ex)
        {
            LogFatal(ex.ToString());
            MessageBox.Show(ex.Message, AppStrings.FriendlyName + " - Fatal Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void LogFatal(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* can't log — ignore */ }
        System.Console.WriteLine(message);
    }

    private static string GetSystemColor()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 0 ? "dark" : "light";
        }
        catch { /* fall through */ }
        return "default";
    }
}

/// <summary>
/// Custom ApplicationContext that keeps the WinForms message pump running
/// for a tray-only app (no main view). Handles graceful shutdown.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    public TrayApplicationContext()
    {
        Application.ApplicationExit += (_, _) => Cleanup();
    }

    private static void Cleanup()
    {
        try
        {
            AudioSyncController.Instance.Disconnect();
            PowerShellRunner.StopAllWorkers();
            TrayViewController.Instance.Dispose();
            System.Console.WriteLine("clean exit");
        }
        catch { /* best-effort cleanup */ }
    }
}

/// <summary>
/// TextWriter that prepends a timestamp to every line and writes to a file with auto-flush.
/// Replaces Console.Out so all Console.WriteLine calls are captured to vmwv.log.
/// TODO: disable logging maybe? or log level control idk
/// </summary>
internal sealed class TimestampedFileWriter : TextWriter
{
    private readonly StreamWriter _writer;

    public TimestampedFileWriter(string path)
    {
        _writer = new StreamWriter(path, append: false, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void WriteLine(string? value)
        => _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {value}");

    public override void Write(string? value)
        => _writer.Write(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _writer.Dispose();
        base.Dispose(disposing);
    }
}
