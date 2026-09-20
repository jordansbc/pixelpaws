using System.Reflection;

namespace DesktopPet.Services;

/// <summary>
/// The running build's version, read from the assembly. Release tags are `v1.2.3`; the assembly
/// carries `1.2.3`, so comparisons strip a leading `v` from either side.
/// </summary>
public static class AppVersion
{
    /// <summary>Numeric version of the running build, e.g. 1.2.3.</summary>
    public static Version Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Human-readable version for the Settings screen.</summary>
    public static string Display => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

    /// <summary>Parse a release tag like "v1.4.0" into a Version. Null if it isn't one.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];

        // Tags may carry a suffix (1.4.0-beta); Version can't parse that, so cut it off.
        int dash = t.IndexOf('-');
        if (dash > 0) t = t[..dash];

        return Version.TryParse(t, out var v) ? v : null;
    }

    /// <summary>True if <paramref name="tag"/> names a build newer than the one running.</summary>
    public static bool IsNewerThanCurrent(string? tag)
    {
        var v = ParseTag(tag);
        if (v is null) return false;

        // Compare only major.minor.build — the revision field is not part of our tags.
        var them = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        var us   = new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0));
        return them > us;
    }
}
