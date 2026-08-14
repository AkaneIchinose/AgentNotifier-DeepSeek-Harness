using System.IO;
using System.Windows;
using AgentNotifier.Audio;
using AgentNotifier.Core;
using AgentNotifier.Notify;
using AgentNotifier.Tools;
using Microsoft.Win32;

namespace AgentNotifier.App;

/// <summary>服务装配：配置、事件服务、提醒引擎、音频、通知、日志、托盘、自启</summary>
public sealed class AppServices : IDisposable
{
    public ConfigStore Cfg { get; } = new();
    public EventBus Bus { get; } = new();
    public ReminderEngine Engine { get; }
    public EventServer Server { get; }
    public BuiltInSounds Sounds { get; }
    public AudioPlayer Player { get; }
    public INotifier Notifier { get; }
    public WizardService Wizard { get; }
    public LogStore Logs { get; }
    public TrayHost Tray { get; }
    public DshMonitor Dsh { get; }
    public MainViewModel Vm { get; }
    public MainWindow? MainWindow { get; set; }

    private readonly CancellationTokenSource _cts = new();
    private BalloonNotifier? _balloon;
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public AppServices()
    {
        Cfg.Load();
        Sounds = new BuiltInSounds(Path.Combine(Cfg.BaseDir, "custom"));
        Player = new AudioPlayer(Sounds);
        Logs = new LogStore(Cfg.BaseDir);
        Wizard = new WizardService(Cfg);
        Notifier = NotifierFactory.Create(Path.Combine(Cfg.BaseDir, "toast.ps1"));
        Dsh = new DshMonitor(Bus);
        Engine = new ReminderEngine(Cfg, Bus);
        Server = new EventServer(Cfg, Bus);
        Tray = new TrayHost("Agent 提醒器");
        Vm = new MainViewModel(this);

        Bus.Raised += Logs.Append;
        Engine.RingRequested += OnRing;
        Engine.ToastRequested += OnToast;
        Tray.MuteRequested += () => Vm.ToggleMute();
        Tray.ExitRequested += () =>
        {
            if (MainWindow != null) MainWindow.AllowClose = true;
            System.Windows.Application.Current.Shutdown();
        };
        Tray.ShowRequested += ShowMainWindow;
        PopupNotifier.ClickAction = ShowMainWindow;
        // 软件端自动识别：DSH 轮询发现新模型 → 自动加入模型样式列表（默认样式，可再自定义）
        Dsh.SessionInfoChanged += OnSessionInfoChanged;
    }

    private void OnSessionInfoChanged()
    {
        try
        {
            var known = Dsh.KnownModels();
            if (known.Count == 0) return;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                bool changed = false;
                Cfg.Update(c =>
                {
                    foreach (var kv in known)
                    {
                        if (c.Models.Any(x => x.ModelId == kv.Key)) continue;
                        c.Models.Add(new ModelStyle
                        {
                            ModelId = kv.Key,
                            Name = kv.Value != "" ? kv.Value : kv.Key
                        });
                        changed = true;
                    }
                });
                if (changed) Vm.RefreshModelStyles();
            });
        }
        catch { }
    }

    /// <summary>把主窗口从托盘/后台带回前台（托盘菜单与弹窗点击共用）</summary>
    private void ShowMainWindow()
    {
        if (MainWindow == null) return;
        MainWindow!.Show();
        MainWindow!.ShowInTaskbar = true;
        MainWindow!.WindowState = System.Windows.WindowState.Normal;
        MainWindow.Activate();
    }

    public void Start()
    {
        try { Server.Start(_cts.Token); }
        catch { /* 端口被占用等：不阻塞应用启动，通知仍可用 */ }
        try { Wizard.EnsureHelperFiles(); }
        catch { /* 数据目录不可写：hook 上报与 Toast 降级 */ }
        Dsh.Start();
        ApplyTheme(Cfg.Config.Theme);
        if (Cfg.Config.Theme == "system")
        {
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; }
            catch { }
        }
        Tray.SetMuted(Cfg.Config.Muted);
        ApplyAutostart(Cfg.Config.Autostart);
    }

    private void OnRing(AgentEvent e)
    {
        var a = e.Kind == EventKind.NeedsUser ? Cfg.Config.NeedsUser : Cfg.Config.Done;
        _ = Player.PlayAsync(a.File, a.Volume, a.Repeats, a.IntervalSec);
    }

    private void OnToast(AgentEvent e)
    {
        var tool = e.Tool switch { "claude" => "Claude Code", "dsh" => "DSH", _ => e.Tool };
        var title = e.Kind == EventKind.NeedsUser ? "需要你的介入" : "任务完成";
        var summary = e.Summary == ""
            ? (e.Kind == EventKind.NeedsUser ? "正在等待你的回复或批准" : "本轮工作已完成")
            : e.Summary;

        // 应用内富弹窗：类型徽标（选择/权限/提交结果）+ Agent 标识 + 自定义图片/内容
        if (Cfg.Config.Notification.Mode == "window")
        {
            try
            {
                PopupNotifier.Show(e, tool, title, summary, Cfg.Config, Cfg.BaseDir);
                return;
            }
            catch (Exception ex)
            {
                // 弹窗失败不阻塞事件：记入崩溃日志并回退原生气泡
                try
                {
                    System.IO.File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                        DateTime.Now + " [PopupNotifier] " + ex + Environment.NewLine);
                }
                catch { }
            }
        }

        var body = tool + "：" + summary;
        if (Cfg.Config.Notification.Mode == "balloon")
        {
            _balloon ??= new BalloonNotifier();
            _ = Task.Run(() => _balloon.Show(title, body));
            return;
        }
        _ = Task.Run(() => Notifier.Show(title, body));
    }

    /// <summary>应用主题：system 跟随 Windows，light/dark 手动指定；动态替换颜色字典</summary>
    public void ApplyTheme(string mode)
    {
        try
        {
            var dark = mode switch
            {
                "dark" => true,
                "light" => false,
                _ => IsSystemDark()
            };
            var app = System.Windows.Application.Current;
            if (app == null) return;
            var dicts = app.Resources.MergedDictionaries;
            foreach (var d in dicts.Where(d => d.Source != null && d.Source.OriginalString.Contains("Colors.xaml")).ToList())
                dicts.Remove(d);
            var uri = new Uri(dark
                ? "pack://application:,,,/Themes/DarkColors.xaml"
                : "pack://application:,,,/Themes/LightColors.xaml", UriKind.Absolute);
            dicts.Add(new ResourceDictionary { Source = uri });
        }
        catch { }
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWAREMicrosoftWindowsCurrentVersionThemesPersonalize");
            return key?.GetValue("AppsUseLightTheme") is int i && i == 0;
        }
        catch { return false; }
    }

    public void ApplyAutostart(bool enabled)
    {
        try
        {
            var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? "";
                if (exe != "") key.SetValue("AgentNotifier", "\"" + exe + "\"");
            }
            else key.DeleteValue("AgentNotifier", false);
        }
        catch { }
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General && Cfg.Config.Theme == "system")
        {
            try { System.Windows.Application.Current.Dispatcher.Invoke(() => ApplyTheme("system")); }
            catch { }
        }
    }

    public void Dispose()
    {
        try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; }
        catch { }
        _cts.Cancel();
        Server.Stop();
        Dsh.Stop();
        Player.Stop();
        Tray.Dispose();
    }
}
