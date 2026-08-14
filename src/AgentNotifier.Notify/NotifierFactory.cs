namespace AgentNotifier.Notify;

public static class NotifierFactory
{
    /// <summary>创建通知器：系统 Toast（WinRT 经 PowerShell）；脚本缺失时回退原生气球</summary>
    public static INotifier Create(string? toastScriptPath = null)
    {
        if (toastScriptPath != null && File.Exists(toastScriptPath))
            return new ToastNotifier(toastScriptPath);
        return new BalloonNotifier();
    }
}
