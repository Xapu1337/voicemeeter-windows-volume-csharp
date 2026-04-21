namespace VoicemeeterWindowsVolume.Models;

public class VolumeChange
{
    public int Old { get; set; }
    public int New { get; set; }
}

public class MuteChange
{
    public bool Old { get; set; }
    public bool New { get; set; }
}

public class DeviceChange
{
    public int Old { get; set; }
    public int New { get; set; }
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
}
