using System.Diagnostics;

namespace AgentNotifier.Notify;

/// <summary>
/// Windows 系统 Toast（WinRT，经 PowerShell 5.1 调用，无需 NuGet 依赖）。
/// 标题/正文经环境变量传递；串行队列保证按事件顺序显示（toast.ps1 冷启动慢，
/// 并发会乱序）；执行失败或超时自动回退原生气球，保证通知可见。
/// </summary>
public sealed class ToastNotifier : INotifier
{
    private readonly string _scriptPath;
    private readonly BalloonNotifier _fallback = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string Name => "系统 Toast（WinRT，气球兜底）";

    public ToastNotifier(string scriptPath)
    {
        _scriptPath = scriptPath;
    }

    public void Show(string title, string body)
    {
        _ = ShowAsync(title, body);
    }

    private async Task ShowAsync(string title, string body)
    {
        await _gate.WaitAsync();
        try
        {
            var psi = new ProcessStartInfo("powershell",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + _scriptPath + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["AN_TITLE"] = title ?? "";
            psi.Environment["AN_BODY"] = body ?? "";
            var proc = Process.Start(psi);
            // 首次冷启动可能较慢，给足 8 秒；失败/超时 → 气球兜底
            if (proc == null || !proc.WaitForExit(8000) || proc.ExitCode != 0)
                _fallback.Show(title ?? "", body ?? "");
        }
        catch { _fallback.Show(title ?? "", body ?? ""); }
        finally { _gate.Release(); }
    }
}
