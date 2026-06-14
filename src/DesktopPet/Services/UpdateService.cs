using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DesktopPet.Services;

/// <summary>
/// Detects when the local git checkout is behind origin/main and runs the existing
/// one-click updater (update.bat: pull → rebuild → relaunch). PixelPaws ships as a
/// cloned repo whose build output lives inside the repo, so we locate the repo root by
/// walking up from the executable until we find update.bat next to a .git folder.
/// Everything is best-effort: no git, no network, or not a checkout → silently no-op.
/// </summary>
public sealed class UpdateService
{
    public string? RepoRoot { get; }

    public UpdateService() => RepoRoot = FindRepoRoot();

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
                File.Exists(Path.Combine(dir.FullName, "update.bat")))
                return dir.FullName;
        return null;
    }

    /// <summary>True if origin/main has commits the local checkout doesn't.</summary>
    public async Task<bool> IsUpdateAvailableAsync()
    {
        if (RepoRoot == null) return false;
        try
        {
            string local = await Git("rev-parse HEAD");
            string remote = await Git("ls-remote origin main");
            if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(remote))
                return false;
            string remoteSha = remote.Split('\t', ' ')[0].Trim();
            return remoteSha.Length >= 7 && !local.Trim().StartsWith(remoteSha[..7]);
        }
        catch { return false; }
    }

    /// <summary>Launch update.bat in its own console; it will close and relaunch the app.</summary>
    public void RunUpdater()
    {
        if (RepoRoot == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(RepoRoot, "update.bat"),
                WorkingDirectory = RepoRoot,
                UseShellExecute = true,
            });
        }
        catch { /* best-effort */ }
    }

    private async Task<string> Git(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = RepoRoot!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p == null) return "";
        string outp = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return p.ExitCode == 0 ? outp : "";
    }
}
