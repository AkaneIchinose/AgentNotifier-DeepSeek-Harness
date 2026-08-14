using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace AgentNotifier.App;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>托盘菜单“退出”时置 true，允许窗口真正关闭</summary>
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyBackdrop();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm && PageHost.Content == null)
                vm.SetPage("overview");
        };
    }

    /// <summary>Win11 22H2+ 亚克力/云母背景；失败自动忽略（Win10 显示纯色背景）</summary>
    private void ApplyBackdrop()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int backdrop = 3; // DWMWA_SYSTEMBACKDROP_TYPE=38, Acrylic
            if (DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)) != 0)
            {
                backdrop = 2; // Mica
                DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            }
            int corner = 2; // DWMWA_WINDOW_CORNER_PREFERENCE=33, Rounded
            DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        }
        catch { }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 关闭窗口 = 最小化到托盘（托盘菜单“退出”才是真正退出）
        if (AllowClose) return;
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string tag) return;
        foreach (var item in NavMenu.Items.OfType<MenuItem>())
            item.IsChecked = item == mi;
        if (DataContext is MainViewModel vm) vm.SetPage(tag);
    }
}
