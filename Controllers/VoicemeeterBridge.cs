using System.Runtime.InteropServices;

namespace VoicemeeterWindowsVolume.Controllers;

/// <summary>
/// P/Invoke bridge to VoicemeeterRemote.dll.
/// Wraps the official Voicemeeter Remote API.
/// You must have Voicemeeter installed for the DLL to be present.
/// </summary>
public class VoicemeeterBridge : IDisposable
{
    private const string Dll = "VoicemeeterRemote64.dll";

    // Resolve the DLL from the Voicemeeter install directory at runtime
    static VoicemeeterBridge()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(VoicemeeterBridge).Assembly,
            (name, _, _) =>
            {
                if (name != Dll) return IntPtr.Zero;

                // Try well-known install paths first
                string[] candidates =
                {
                    @"C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll",
                    @"C:\Program Files\VB\Voicemeeter\VoicemeeterRemote64.dll",
                };

                // Also try to locate via registry uninstall key
                try
                {
                    string? installDir = ReadVoicemeeterInstallDir();
                    if (installDir != null)
                        candidates = [Path.Combine(installDir, Dll), .. candidates];
                }
                catch { /* ignore registry errors */ }

                foreach (string path in candidates)
                    if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr handle))
                        return handle;

                System.Console.WriteLine(
                    $"WARNING: {Dll} not found. Voicemeeter may not be installed.");
                return IntPtr.Zero;
            });
    }

    private static string? ReadVoicemeeterInstallDir()
    {
        string[] keys =
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}",
        };
        foreach (string key in keys)
        {
            using var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            if (reg?.GetValue("InstallLocation") is string dir && Directory.Exists(dir))
                return dir;
        }
        return null;
    }

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_Login")]
    private static extern int VBVMR_Login();

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_Logout")]
    private static extern int VBVMR_Logout();

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_IsParametersDirty")]
    private static extern int VBVMR_IsParametersDirty();

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetParameterFloat")]
    private static extern int VBVMR_GetParameterFloat(
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        out float value);

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_SetParameterFloat")]
    private static extern int VBVMR_SetParameterFloat(
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        float value);

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetParameterStringA")]
    private static extern int VBVMR_GetParameterStringA(
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        System.Text.StringBuilder result);

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_SetParameters")]
    private static extern int VBVMR_SetParameters(
        [MarshalAs(UnmanagedType.LPStr)] string paramScript);

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_GetVoicemeeterType")]
    private static extern int VBVMR_GetVoicemeeterType(out int type);

    [DllImport("VoicemeeterRemote64.dll", EntryPoint = "VBVMR_MacroButton_SetStatus")]
    private static extern int VBVMR_MacroButton_SetStatus(int nuLogicalButton, float fValue, int bitMode);

    private System.Threading.Timer? _pollTimer;
    private bool _disposed;

    public event Action? OnParametersChange;
    public event Action? OnDisconnected;
    public string VmType { get; private set; } = "voicemeeter";

    public void Connect()
    {
        // Login can succeed (return 0/1) while the VM engine is still initializing —
        // GetVoicemeeterType will return -1 until shared memory is mapped.
        // Calling any parameter API before that point → AV (0xc0000005).
        // Retry here so no unmanaged exception ever propagates to managed code.
        const int maxAttempts = 30;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            int loginResult = VBVMR_Login();
            if (loginResult < 0)
            {
                // VM process exists but COM/IPC server not accepting clients yet
                System.Console.WriteLine($"VBVMR_Login not ready (attempt {attempt}/{maxAttempts}, code {loginResult})");
                VBVMR_Logout();
                System.Threading.Thread.Sleep(1000);
                continue;
            }

            int typeResult = VBVMR_GetVoicemeeterType(out int vmType);
            if (typeResult < 0 || vmType <= 0)
            {
                // Login succeeded but shared memory not mapped yet
                System.Console.WriteLine($"VBVMR_GetVoicemeeterType not ready (attempt {attempt}/{maxAttempts}, code {typeResult}, type {vmType})");
                VBVMR_Logout();
                System.Threading.Thread.Sleep(1000);
                continue;
            }

            VmType = vmType switch
            {
                2 => "voicemeeterBanana",
                3 => "voicemeeterPotato",
                _ => "voicemeeter",
            };

            System.Console.WriteLine($"Connected to Voicemeeter type: {VmType} (attempt {attempt})");

            // Poll for parameter changes every 50ms; fire OnDisconnected if VM is gone.
            // Interlocked.Exchange ensures only the first concurrent callback fires OnDisconnected
            // (System.Threading.Timer callbacks can overlap when they take longer than the period).
            _pollTimer = new System.Threading.Timer(_ =>
            {
                int dirty = VBVMR_IsParametersDirty();
                if (dirty < 0)
                {
                    var t = System.Threading.Interlocked.Exchange(ref _pollTimer, null);
                    if (t != null)
                    {
                        t.Dispose();
                        OnDisconnected?.Invoke();
                    }
                }
                else if (dirty > 0)
                {
                    OnParametersChange?.Invoke();
                }
            }, null, 0, 50);

            return; // connected successfully
        }

        throw new InvalidOperationException($"Voicemeeter not ready after {maxAttempts} attempts.");
    }

    public void Disconnect()
    {
        // Wait for any in-progress timer callback to finish before calling VBVMR_Logout.
        // If we call Logout while IsParametersDirty is executing on another thread the DLL
        // corrupts its internal state and the next Login will AV.
        if (_pollTimer != null)
        {
            using var done = new System.Threading.ManualResetEventSlim(false);
            if (_pollTimer.Dispose(done.WaitHandle))
                done.Wait(500); // max 500ms; 50ms period so one tick is well within that
            _pollTimer = null;
        }
        VBVMR_Logout();
    }

    /// <summary>
    /// Gets a float parameter. e.g. GetParameter("Strip", 0, "Gain")
    /// </summary>
    public float GetParameter(string type, int index, string paramName)
    {
        string fullParam = $"{type}[{index}].{paramName}";
        VBVMR_GetParameterFloat(fullParam, out float value);
        return value;
    }

    /// <summary>
    /// Gets a string parameter. e.g. GetParameterString("Strip", 0, "Label")
    /// </summary>
    public string GetParameterString(string type, int index, string paramName)
    {
        string fullParam = $"{type}[{index}].{paramName}";
        var sb = new System.Text.StringBuilder(512);
        VBVMR_GetParameterStringA(fullParam, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Sets a float parameter. e.g. SetParameter("Strip", 0, "Gain", -6.0f)
    /// </summary>
    public void SetParameter(string type, int index, string paramName, float value)
    {
        string fullParam = $"{type}[{index}].{paramName}";
        VBVMR_SetParameterFloat(fullParam, value);
    }

    /// <summary>
    /// Sends a command to Voicemeeter. e.g. SendCommand("Restart", 1), SendCommand("Show", 1)
    /// </summary>
    public void SendCommand(string command, float value)
    {
        string script = $"Command.{command}={value};";
        VBVMR_SetParameters(script);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Disconnect();
        }
    }
}
