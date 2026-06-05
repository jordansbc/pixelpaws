using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DesktopPet.Services;

/// <summary>System-tray icon with a Settings / Pause / Quit menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pauseItem;

    public TrayService(Action onSettings, Action<bool> onPauseToggled, Action onQuit)
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => onSettings();

        _pauseItem = new ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.Click += (_, _) => onPauseToggled(_pauseItem.Checked);

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => onQuit();

        menu.Items.Add(settingsItem);
        menu.Items.Add(_pauseItem);
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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
