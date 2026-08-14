namespace AgentNotifier.Core;

/// <summary>提醒引擎：去抖 → 勿扰 → 静音 → 通知/响铃决策</summary>
public sealed class ReminderEngine
{
    private readonly ConfigStore _cfg;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _lastSeen = new();

    public event Action<AgentEvent>? RingRequested;
    public event Action<AgentEvent>? ToastRequested;

    public ReminderEngine(ConfigStore cfg, EventBus bus)
    {
        _cfg = cfg;
        bus.Raised += OnEvent;
    }

    private void OnEvent(AgentEvent e)
    {
        var cfg = _cfg.Config;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = $"{e.Tool}|{e.SessionId}|{e.Kind}";
        var debounceMs = Math.Max(cfg.DebounceSec, 0) * 1000L;

        bool ring;
        bool toast = true;
        lock (_lock)
        {
            if (_lastSeen.TryGetValue(key, out var last) && now - last < debounceMs)
                return; // 去抖：同一会话同类事件在窗口内只提醒一次
            _lastSeen[key] = now;
            ring = true;
        }

        var dnd = ReminderEngine.FindActiveDnd(cfg.Dnd);
        if (dnd != null)
        {
            if (dnd.Mode == "silent") { ring = false; toast = false; }
            else if (dnd.Mode == "toast_only") ring = false;
        }
        if (cfg.Muted) ring = false;
        if (!cfg.ToastEnabled) toast = false;

        if (ring) RingRequested?.Invoke(e);
        if (toast) ToastRequested?.Invoke(e);
    }

    public static DndRule? FindActiveDnd(List<DndRule> rules)
    {
        var now = DateTime.Now.TimeOfDay;
        foreach (var r in rules)
        {
            var start = TryParse(r.Start, out var s) ? s : new TimeSpan(22, 0, 0);
            var end = TryParse(r.End, out var en) ? en : new TimeSpan(8, 0, 0);
            bool active = start <= end ? now >= start && now < end : now >= start || now < end;
            if (active) return r;
        }
        return null;
    }

    private static bool TryParse(string s, out TimeSpan ts)
    {
        var p = s.Split(':');
        if (p.Length == 2 && int.TryParse(p[0], out var h) && int.TryParse(p[1], out var m) && h is >= 0 and < 24 && m is >= 0 and < 60)
        {
            ts = new TimeSpan(h, m, 0);
            return true;
        }
        ts = default;
        return false;
    }
}
