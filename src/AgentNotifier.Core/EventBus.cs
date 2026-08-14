namespace AgentNotifier.Core;

public sealed class EventBus
{
    public event Action<AgentEvent>? Raised;
    public void Publish(AgentEvent e) => Raised?.Invoke(e);
}
