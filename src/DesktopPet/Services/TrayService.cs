using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DesktopPet.Services;

/// <summary>System-tray icon with a Settings / Pause / Quit menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly Action _onUpdate;

    public TrayService(Action onSettings, Action<bool> onPauseToggled, Action onQuit, Action onUpdate)
    {
        _onUpdate = onUpdate;
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => onSettings();

        _pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.Click += (_, _) => onPauseToggled(_pauseItem.Checked);

        _updateItem = new ToolStripMenuItem("Update available — install now") { Visible = false };
        _updateItem.Click += (_, _) => onUpdate();

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => onQuit();

        menu.Items.Add(settingsItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
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
