using System.IO;

namespace DesktopPet.Services;

/// <summary>Lightweight file logger for diagnosing behaviour. Set Enabled=false to disable.</summary>
public static class DebugLog
{
    /// <summary>
    /// Off unless PIXELPAWS_DEBUG=1 is set, so users never accumulate a log they didn't ask for
    /// — but anyone reporting a bug can turn it on without a special build.
    /// </summary>
    public static bool Enabled =
        Environment.GetEnvironmentVariable("PIXELPAWS_DEBUG") is "1" or "true";
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
