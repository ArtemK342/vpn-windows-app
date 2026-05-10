using System.Drawing;
using System.Runtime.InteropServices;

namespace FsocietyNet.Services;

public sealed class TrayService : IDisposable
{
    // ── P/Invoke ──────────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // ── Fields ────────────────────────────────────────────────────────────
    private readonly WinForms.NotifyIcon      _notify;
    private readonly WinForms.ToolStripMenuItem _connectItem;
    private readonly WinForms.ToolStripMenuItem _disconnectItem;

    private readonly IntPtr _hIconConnected;
    private readonly IntPtr _hIconDisconnected;

    private bool _disposed;

    // ── Constructor ───────────────────────────────────────────────────────
    public TrayService(Action onOpen, Action onConnect, Action onDisconnect, Action onExit)
    {
        _hIconConnected    = CreateCircleIcon(Color.FromArgb(0xC8, 0xF1, 0x35), Color.FromArgb(0x1A, 0x1A, 0x1A));
        _hIconDisconnected = CreateCircleIcon(Color.FromArgb(0x88, 0x88, 0x92), Color.FromArgb(0x1A, 0x1A, 0x1A));

        var menu = new WinForms.ContextMenuStrip();

        var openItem = new WinForms.ToolStripMenuItem("Открыть");
        openItem.Click += (_, _) => onOpen();

        _connectItem = new WinForms.ToolStripMenuItem("Подключить");
        _connectItem.Click += (_, _) => onConnect();

        _disconnectItem = new WinForms.ToolStripMenuItem("Отключить") { Enabled = false };
        _disconnectItem.Click += (_, _) => onDisconnect();

        var exitItem = new WinForms.ToolStripMenuItem("Выйти");
        exitItem.Click += (_, _) => onExit();

        menu.Items.Add(openItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_disconnectItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notify = new WinForms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Text             = "[f]society VPN — Отключён",
            Icon             = Icon.FromHandle(_hIconDisconnected),
            Visible          = true
        };
        _notify.DoubleClick += (_, _) => onOpen();
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetConnected(bool connected, string? serverName)
    {
        if (_disposed) return;
        _notify.Icon    = connected
            ? Icon.FromHandle(_hIconConnected)
            : Icon.FromHandle(_hIconDisconnected);
        _notify.Text    = connected
            ? $"[f]society VPN — {serverName ?? "Подключён"}"
            : "[f]society VPN — Отключён";
        _connectItem.Enabled    = !connected;
        _disconnectItem.Enabled = connected;
    }

    public void ShowNotification(string title, string message,
        WinForms.ToolTipIcon icon = WinForms.ToolTipIcon.Info)
    {
        if (_disposed) return;
        _notify.ShowBalloonTip(4000, title, message, icon);
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notify.Visible = false;
        _notify.Dispose();
        if (_hIconConnected    != IntPtr.Zero) DestroyIcon(_hIconConnected);
        if (_hIconDisconnected != IntPtr.Zero) DestroyIcon(_hIconDisconnected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Creates a 24x24 bitmap with a filled circle and returns its HICON.</summary>
    private static IntPtr CreateCircleIcon(Color circleColor, Color bgColor)
    {
        using var bmp = new Bitmap(24, 24);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(bgColor);
        using var brush = new SolidBrush(circleColor);
        g.FillEllipse(brush, 2, 2, 20, 20);
        return bmp.GetHicon();
    }
}
