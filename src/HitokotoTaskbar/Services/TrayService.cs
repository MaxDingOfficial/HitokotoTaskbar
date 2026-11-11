using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace HitokotoTaskbar.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;
    public event Action? MenuRequested;
    public event Action? SettingsRequested;
    public TrayService()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        _ownedIcon = new Icon(stream);
        _icon = new Forms.NotifyIcon { Icon = _ownedIcon, Text = "Hitokoto Taskbar" };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right) MenuRequested?.Invoke();
            else if (e.Button == Forms.MouseButtons.Left) SettingsRequested?.Invoke();
        };
    }
    public void SetVisible(bool visible) => _icon.Visible = visible;
    public void SetText(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;
    public void Notify(string text)
    {
        if (!_icon.Visible) return;
        _icon.ShowBalloonTip(2500, "Hitokoto Taskbar", text, Forms.ToolTipIcon.Info);
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _ownedIcon.Dispose(); }
}
