namespace AgentNotifier.Notify;

/// <summary>系统托盘宿主：菜单（显示主窗口 / 一键静音 / 退出）</summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _muteItem;

    public event Action? ShowRequested;
    public event Action? MuteRequested;
    public event Action? ExitRequested;

    public TrayHost(string text)
    {
        _icon = new NotifyIcon { Visible = true, Text = text, Icon = TrayIconFactory.Create() };
        var menu = new ContextMenuStrip();
        var show = new ToolStripMenuItem("显示主窗口");
        show.Click += (_, _) => ShowRequested?.Invoke();
        _muteItem = new ToolStripMenuItem("一键静音");
        _muteItem.Click += (_, _) => MuteRequested?.Invoke();
        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(show);
        menu.Items.Add(_muteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void SetMuted(bool muted)
    {
        _muteItem.Text = muted ? "取消静音" : "一键静音";
    }

    public void Dispose()
    {
        try { _icon.Visible = false; _icon.Dispose(); } catch { }
    }
}
