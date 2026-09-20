using System.IO;
using System.Reflection;

namespace DesktopPet.Services;

/// <summary>
/// Where the pet's art comes from.
///
/// Assets are compiled into the executable so a release is genuinely one file — a published
/// PixelPaws.exe with no Assets folder beside it still has all seven cats. A matching file on
/// disk always wins, though, which keeps the edit-and-rerun loop working during development and
/// lets anyone drop in a custom pet pack without rebuilding.
/// </summary>
public static class AssetSource
{
    private static readonly Assembly Asm = typeof(AssetSource).Assembly;

    /// <summary>Embedded resources are named by their project path with separators as dots.</summary>
    private static string ResourceName(string relativePath) =>
        "DesktopPet." + relativePath.Replace('\\', '/').Replace('/', '.');

    private static string DiskPath(string relativePath) =>
        Path.Combine(AppContext.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Open an asset, preferring a file on disk over the embedded copy. Null if neither exists.</summary>
    public static Stream? TryOpen(string relativePath)
    {
        try
        {
            string disk = DiskPath(relativePath);
            if (File.Exists(disk)) return File.OpenRead(disk);
        }
        catch { /* unreadable on disk — fall through to the embedded copy */ }

        return Asm.GetManifestResourceStream(ResourceName(relativePath));
    }

    /// <summary>Open an asset or throw — for assets whose absence is a broken build.</summary>
    public static Stream Open(string relativePath) =>
        TryOpen(relativePath) ?? throw new FileNotFoundException($"Missing asset: {relativePath}");

    public static bool Exists(string relativePath)
    {
        using var s = TryOpen(relativePath);
        return s != null;
    }

    /// <summary>Read an asset fully into memory. Callers that need seeking (image decoders) want this.</summary>
    public static byte[] ReadAllBytes(string relativePath)
    {
        using var s = Open(relativePath);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static string ReadAllText(string relativePath)
    {
        using var s = Open(relativePath);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>
    /// Every pet pack that can be loaded, by folder name (e.g. "cat", "cat-orange"): the built-in
    /// embedded packs plus anything the user has dropped into Assets\pets next to the exe.
    /// </summary>
    public static IReadOnlyList<string> ListPets()
    {
        var pets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        const string prefix = "DesktopPet.Assets.pets.";
        const string suffix = ".manifest.json";
        foreach (var name in Asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) continue;
            pets.Add(name[prefix.Length..^suffix.Length]);
        }

        try
        {
            string dir = DiskPath("Assets/pets");
            if (Directory.Exists(dir))
                foreach (var d in Directory.EnumerateDirectories(dir))
                    if (File.Exists(Path.Combine(d, "manifest.json")))
                        pets.Add(Path.GetFileName(d));
        }
        catch { /* best-effort */ }

        return pets.ToList();
    }
}
