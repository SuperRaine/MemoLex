using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MemoLex;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notification;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Drawing.Icon _icon;
    private bool _disposed;
    private bool _hintShown;

    public TrayIcon(Action restore, Action settings, Action exit)
    {
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/MemoLex;component/app.ico")).Stream)
        using (var embeddedIcon = new Drawing.Icon(stream, 32, 32))
            _icon = (Drawing.Icon)embeddedIcon.Clone();
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("打开墨墨搜词", null, (_, _) => restore());
        _menu.Items.Add("设置", null, (_, _) => settings());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => exit());
        _notification = new Forms.NotifyIcon { Icon = _icon, Text = "墨墨搜词 · 点击打开，右键退出", ContextMenuStrip = _menu, Visible = true };
        _notification.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) restore(); };
        _notification.DoubleClick += (_, _) => restore();
        _notification.BalloonTipClicked += (_, _) => restore();
    }
    public void ShowHiddenHint(string shortcut)
    {
        if (_disposed || _hintShown) return;
        _hintShown = true;
        _notification.ShowBalloonTip(2500, "墨墨搜词已收进托盘", shortcut=="未设置"?"点击托盘图标恢复；右键菜单可退出。":$"点击托盘图标或按 {shortcut} 恢复；右键菜单可退出。", Forms.ToolTipIcon.Info);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notification.Visible = false;
        _notification.Dispose();
        _menu.Dispose();
        _icon.Dispose();
    }
}
