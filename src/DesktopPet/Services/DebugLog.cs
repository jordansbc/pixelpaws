using System.IO;

namespace DesktopPet.Services;

/// <summary>Lightweight file logger for diagnosing behaviour. Set Enabled=false to disable.</summary>
public static class DebugLog
{
    public static bool Enabled = false;
    private static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixelpaws_debug.log");
    private static readonly object Gate = new();

    public static void Write(string msg)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public static void Clear()
    {
        try { File.Delete(Path); } catch { }
    }
}
