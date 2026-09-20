using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DesktopPet.Services;

/// <summary>
/// Keeps PixelPaws up to date from GitHub Releases.
///
/// The old updater assumed every install was a git checkout with the .NET SDK available, and
/// updated by running `git pull` + a rebuild. That works for developers and nobody else. This
/// version asks the Releases API for the latest tag, downloads the published single-file exe,
/// and swaps it in via a tiny batch script — so a normal user needs neither git nor the SDK.
///
/// A git checkout is still detected and preferred when present (a developer wants `git pull`,
/// not their build replaced by a release binary).
/// </summary>
public sealed class UpdateService
{
    private const string Owner = "jordansbc";
    private const string Repo  = "pixelpaws";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private readonly HttpClient _http;

    /// <summary>Repo root if this install is a git checkout, else null.</summary>
    public string? RepoRoot { get; }

    /// <summary>True when this install updates via git rather than by swapping a downloaded exe.</summary>
    public bool IsSourceCheckout => RepoRoot != null;

    /// <summary>Tag of the newest release seen by the last check, if any.</summary>
    public string? LatestTag { get; private set; }

    /// <summary>Browser URL for the newest release, for "see what's new".</summary>
    public string? LatestUrl { get; private set; }

    private string? _assetUrl;

    public UpdateService(HttpClient http)
    {
        _http    = http;
        RepoRoot = FindRepoRoot();
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
                File.Exists(Path.Combine(dir.FullName, "update.bat")))
                return dir.FullName;
        return null;
    }

    /// <summary>
    /// True if a newer build is available. Source checkouts compare against origin/main;
    /// everyone else compares the running assembly version to the latest release tag.
    /// </summary>
    public async Task<bool> IsUpdateAvailableAsync(CancellationToken ct = default)
    {
        return IsSourceCheckout
            ? await IsGitBehindAsync()
            : await IsNewerReleaseAvailableAsync(ct);
    }

    /// <summary>A sentence describing the result of a check, for the Settings screen.</summary>
    public async Task<string> DescribeCheckAsync(CancellationToken ct = default)
    {
        try
        {
            bool available = await IsUpdateAvailableAsync(ct);
            if (!available)
                return IsSourceCheckout
                    ? $"Up to date ({AppVersion.Display}, source checkout)."
                    : $"Up to date ({AppVersion.Display}).";

            return IsSourceCheckout
                ? "New commits on origin/main — use the tray menu to update."
                : $"{LatestTag} is available (you have {AppVersion.Display}).";
        }
        catch (Exception ex)
        {
            return $"Couldn't check: {ex.Message}";
        }
    }

    // ── release-based updating (the normal case) ────────────────────────────

    private async Task<bool> IsNewerReleaseAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            LatestTag = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            LatestUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;
            _assetUrl = FindExeAsset(root);

            return _assetUrl != null && AppVersion.IsNewerThanCurrent(LatestTag);
        }
        catch { return false; }   // offline, rate-limited, no releases yet — stay quiet
    }

    /// <summary>Pick the published .exe from a release's assets.</summary>
    private static string? FindExeAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets)) return null;
        foreach (var a in assets.EnumerateArray())
        {
            string? name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name == null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (a.TryGetProperty("browser_download_url", out var url)) return url.GetString();
        }
        return null;
    }

    /// <summary>
    /// Download the new exe beside the current one and hand off to a small batch script that
    /// waits for this process to exit, swaps the files, and relaunches. A running exe can't
    /// overwrite itself, which is why the swap has to happen from outside.
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(CancellationToken ct = default)
    {
        if (_assetUrl == null) return false;

        string currentExe = Environment.ProcessPath
                            ?? Path.Combine(AppContext.BaseDirectory, "PixelPaws.exe");
        string dir        = Path.GetDirectoryName(currentExe)!;
        string stagedExe  = Path.Combine(dir, "PixelPaws.new.exe");
        string script     = Path.Combine(Path.GetTempPath(), "pixelpaws_update.cmd");

        try
        {
            using (var resp = await _http.GetAsync(_assetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode) return false;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(stagedExe);
                await src.CopyToAsync(dst, ct);
            }

            // Sanity check: a truncated or HTML-error download must never replace a working app.
            if (new FileInfo(stagedExe).Length < 1_000_000)
            {
                TryDelete(stagedExe);
                return false;
            }

            await File.WriteAllTextAsync(script, BuildSwapScript(currentExe, stagedExe), ct);

            Process.Start(new ProcessStartInfo
            {
                FileName        = script,
                UseShellExecute = true,
                CreateNoWindow  = true,
                WindowStyle     = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch
        {
            TryDelete(stagedExe);
            return false;
        }
    }

    /// <summary>
    /// Wait for the app to exit, swap in the new exe, relaunch, then delete ourselves.
    /// Keeps a `.old` copy so a failed swap is recoverable by hand.
    /// </summary>
    private static string BuildSwapScript(string currentExe, string stagedExe) =>
        $"""
        @echo off
        setlocal
        rem Give the running PixelPaws a moment to close.
        for /l %%i in (1,1,30) do (
            tasklist /fi "imagename eq PixelPaws.exe" | find /i "PixelPaws.exe" >nul || goto :swap
            timeout /t 1 /nobreak >nul
        )
        :swap
        if exist "{currentExe}.old" del /q "{currentExe}.old"
        move /y "{currentExe}" "{currentExe}.old" >nul 2>&1
        move /y "{stagedExe}" "{currentExe}" >nul 2>&1
        if errorlevel 1 (
            rem Swap failed - put the original back rather than leaving nothing behind.
            move /y "{currentExe}.old" "{currentExe}" >nul 2>&1
        )
        start "" "{currentExe}"
        del /q "{currentExe}.old" >nul 2>&1
        del /q "%~f0"
        """;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ── git-checkout updating (developers) ──────────────────────────────────

    /// <summary>
    /// True only if origin/main has commits the local checkout doesn't.
    ///
    /// Comparing the two SHAs for inequality is not enough: a checkout with unpushed local
    /// commits, or one sitting on a feature branch, differs from origin/main while being ahead
    /// of it rather than behind. That would nag about an update on every launch and offer to
    /// `git pull` over work in progress. Ancestry is the real question — if origin/main is
    /// already an ancestor of HEAD there is nothing to fetch.
    /// </summary>
    private async Task<bool> IsGitBehindAsync()
    {
        if (RepoRoot == null) return false;
        try
        {
            var (remoteOut, remoteOk) = await Git("ls-remote origin main");
            if (!remoteOk || string.IsNullOrWhiteSpace(remoteOut)) return false;

            string remoteSha = remoteOut.Split('\t', ' ')[0].Trim();
            if (remoteSha.Length < 7) return false;

            // Exit 0 => the remote commit is already in our history (up to date, or ahead).
            // Any other exit, including "bad object" when we have never fetched that commit,
            // means there is genuinely something new upstream.
            var (_, isAncestor) = await Git($"merge-base --is-ancestor {remoteSha} HEAD");
            return !isAncestor;
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

    /// <summary>Run git and return (stdout, exited cleanly). Both pipes are drained concurrently:
    /// reading only stdout can deadlock, because git blocks once the stderr pipe buffer fills
    /// (`ls-remote` chatters there about progress) and then never reaches the exit we await.</summary>
    private async Task<(string Output, bool Success)> Git(string args)
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
        if (p == null) return ("", false);

        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr);
        await p.WaitForExitAsync();

        return (await stdout, p.ExitCode == 0);
    }
}
