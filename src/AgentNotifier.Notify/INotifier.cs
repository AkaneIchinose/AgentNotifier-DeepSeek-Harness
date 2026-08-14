namespace AgentNotifier.Notify;

/// <summary>通知接口：M1 实现为 Windows 原生气球通知；M2 接入系统 Toast（WinRT）</summary>
public interface INotifier
{
    string Name { get; }
    void Show(string title, string body);
}
