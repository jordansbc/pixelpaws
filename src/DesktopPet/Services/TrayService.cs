using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DesktopPet.Services;

/// <summary>System-tray icon with a Settings / Pause / Quit menu.</summary>
public sealed class TrayService : IDisposable
{
    /// <summary>Where the "Support PixelPaws" menu item sends people. Same link as the repo's Sponsor button.</summary>
    private const string DonateUrl = "https://www.paypal.me/jordanschoner";

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly ToolStripMenuItem _aiItem;
    private readonly ToolStripMenuItem _talkItem;
    private readonly Action _onUpdate;

    public TrayService(Action onSettings, Action<bool> onPauseToggled, Action onQuit, Action onUpdate,
                       Action<bool> onAiToggled, Action onTalk, bool aiEnabled)
    {
        _onUpdate = onUpdate;
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => onSettings();

        _pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.Click += (_, _) => onPauseToggled(_pauseItem.Checked);

        _aiItem = new ToolStripMenuItem("AI companion") { CheckOnClick = true, Checked = aiEnabled };
        _talkItem = new ToolStripMenuItem("Talk to cat…") { Enabled = aiEnabled };
        _aiItem.Click += (_, _) => { onAiToggled(_aiItem.Checked); _talkItem.Enabled = _aiItem.Checked; };
        _talkItem.Click += (_, _) => onTalk();

        var donateItem = new ToolStripMenuItem("Support PixelPaws ☕");
        donateItem.Click += (_, _) => OpenUrl(DonateUrl);

        _updateItem = new ToolStripMenuItem("Update available — install now") { Visible = false };
        _updateItem.Click += (_, _) => onUpdate();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => onQuit();

        menu.Items.Add(settingsItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_aiItem);
        menu.Items.Add(_talkItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(donateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_updateItem);
        menu.Items.Add(quitItem);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "PixelPaws",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => onSettings();
    }

    /// <summary>Open a URL in the user's default browser. Best-effort — never throws into the UI.</summary>
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* if no browser is available, silently do nothing */ }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }

    public void SetPaused(bool paused) => _pauseItem.Checked = paused;

    /// <summary>Reflect the AI-companion on/off state in the tray (kept in sync with the Settings window).</summary>
    public void SetAiEnabled(bool enabled)
    {
        _aiItem.Checked   = enabled;
        _talkItem.Enabled = enabled;
    }

    /// <summary>Reveal the "Update now" menu item and pop a balloon inviting the update.</summary>
    public void ShowUpdateAvailable()
    {
        _updateItem.Visible = true;
        _icon.BalloonTipTitle = "PixelPaws update available";
        _icon.BalloonTipText  = "A newer version is ready. Click here or the tray menu to install.";
        _icon.BalloonTipIcon  = ToolTipIcon.Info;
        _icon.BalloonTipClicked -= OnBalloonClicked;
        _icon.BalloonTipClicked += OnBalloonClicked;
        _icon.ShowBalloonTip(8000);
    }

    private void OnBalloonClicked(object? sender, EventArgs e) => _onUpdate();

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
