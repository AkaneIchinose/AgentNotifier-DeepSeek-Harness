namespace AgentNotifier.Core;

public sealed class LogEntry
{
    public DateTime Time { get; set; }
    public string Tool { get; set; } = "";
    public string Kind { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Summary { get; set; } = "";
}

/// <summary>轻量事件日志：内存最近 200 条 + 追加写入 logs/events.log</summary>
public sealed class LogStore
{
    private readonly string _dir;
    private readonly object _lock = new();
    private readonly List<LogEntry> _recent = new();

    public event Action? Updated;

    public LogStore(string? baseDir = null)
    {
        _dir = Path.Combine(baseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentNotifier"), "logs");
    }

    public string LogsDir => _dir;

    public void Append(AgentEvent e)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Tool = e.Tool,
            Kind = e.Kind == EventKind.NeedsUser ? "需要介入" : "任务完成",
            SessionId = e.SessionId,
            Summary = e.Summary
        };
        lock (_lock)
        {
            _recent.Insert(0, entry);
            if (_recent.Count > 200) _recent.RemoveRange(200, _recent.Count - 200);
            try
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(Path.Combine(_dir, "events.log"),
                    $"{entry.Time:yyyy-MM-dd HH:mm:ss}	{entry.Tool}	{entry.Kind}	{entry.SessionId}	{entry.Summary}{Environment.NewLine}");
            }
            catch { }
        }
        Updated?.Invoke();
    }

    public IReadOnlyList<LogEntry> Recent(int n)
    {
        lock (_lock) return _recent.Take(n).ToList();
    }
}
