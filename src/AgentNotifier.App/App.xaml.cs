using System.IO;
using System.Windows;

namespace AgentNotifier.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    DateTime.Now + " " + args.Exception + Environment.NewLine);
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    DateTime.Now + " [AppDomain] " + args.ExceptionObject + Environment.NewLine);
            }
            catch { }
        };
        _mutex = new Mutex(true, "AgentNotifier.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Agent 提醒器已经在运行（可查看系统托盘）。", "Agent 提醒器");
            Shutdown();
            return;
        }

        _services = new AppServices();
        _services.Start();

        var win = new MainWindow { DataContext = _services.Vm };
        _services.MainWindow = win;
        _services.Vm.SetPage("overview");
        win.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
