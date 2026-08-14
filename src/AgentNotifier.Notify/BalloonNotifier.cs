namespace AgentNotifier.Notify;

/// <summary>Windows 原生气球通知（托盘气泡），显示后自动隐藏图标</summary>
public sealed class BalloonNotifier : INotifier
{
    private readonly NotifyIcon _icon;
    public string Name => "原生通知（气泡）";

    public BalloonNotifier()
    {
        _icon = new NotifyIcon { Visible = false, Text = "AgentNotifier", Icon = TrayIconFactory.Create() };
    }

    public void Show(string title, string body)
    {
        try
        {
            _icon.Visible = true;
            _icon.ShowBalloonTip(6000, title, body, ToolTipIcon.Info);
            var t = new Thread(() =>
            {
                try { Thread.Sleep(8000); _icon.Visible = false; }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
        catch { }
    }
}
