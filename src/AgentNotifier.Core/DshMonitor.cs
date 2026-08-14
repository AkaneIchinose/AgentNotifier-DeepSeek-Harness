using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AgentNotifier.Core;

/// <summary>某会话识别到的信息：模型 ID、目录显示名（如 DeepSeek-V4-Flash）、会话标题</summary>
public sealed record SessionInfo(string ModelId, string ModelName, string Title);

/// <summary>
/// DSH（DeepSeek Web Harness）监听器：连接官方 WebSocket 事件流
/// （/api/events.mux 与 /api/events.host），感知"需要介入"（提问/审批请求）
/// 与"任务完成"（agent 停止运行）。零配置改动，断开自动重连（3 秒）。
/// 同时周期性调用本机 RPC（session.list / session.models）自动识别每个会话所用模型。
/// </summary>
public sealed class DshMonitor : IDisposable
{
    private readonly EventBus _bus;
    private readonly string _baseUrl;
    private readonly string _origin;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, bool> _runningStates = new();
    private readonly object _infoLock = new();
    private readonly Dictionary<string, SessionInfo> _sessionInfo = new();
    private readonly Dictionary<string, string> _knownModels = new(); // modelId -> 显示名
    private HttpClient? _http;

    public bool Connected { get; private set; }
    public event Action<bool>? ConnectionChanged;
    /// <summary>会话信息（模型/标题）刷新完成（含新模型被发现）</summary>
    public event Action? SessionInfoChanged;

    public DshMonitor(EventBus bus, string baseUrl = "http://127.0.0.1:3080")
    {
        _bus = bus;
        _baseUrl = baseUrl;
        _origin = baseUrl;
    }

    /// <summary>某会话识别到的模型 ID（未知返回空）</summary>
    public string ModelOf(string sessionId)
    {
        lock (_infoLock)
            return _sessionInfo.TryGetValue(sessionId, out var s) ? s.ModelId : "";
    }

    /// <summary>某会话的模型目录显示名（未知返回空）</summary>
    public string ModelNameOf(string sessionId)
    {
        lock (_infoLock)
            return _sessionInfo.TryGetValue(sessionId, out var s) ? s.ModelName : "";
    }

    /// <summary>某会话的标题（未知返回空）</summary>
    public string TitleOf(string sessionId)
    {
        lock (_infoLock)
            return _sessionInfo.TryGetValue(sessionId, out var s) ? s.Title : "";
    }

    /// <summary>已识别到的模型快照（modelId → 显示名），供设置页展示"软件端识别到的模型"</summary>
    public IReadOnlyDictionary<string, string> KnownModels()
    {
        lock (_infoLock) return new Dictionary<string, string>(_knownModels);
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        SetConnected(false);
    }

    public void Dispose() => Stop();

    private void SetConnected(bool value)
    {
        if (Connected != value)
        {
            Connected = value;
            ConnectionChanged?.Invoke(value);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        _http ??= CreateHttpClient();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var mux = await ConnectAsync("/api/events.mux", ct);
                using var host = await ConnectAsync("/api/events.host", ct);
                SetConnected(true);
                var muxTask = ReadLoopAsync("mux", mux, ct);
                var hostTask = ReadLoopAsync("host", host, ct);
                var pollTask = PollLoopAsync(ct);
                await Task.WhenAny(muxTask, hostTask);
            }
            catch { }
            SetConnected(false);
            try { await Task.Delay(3000, ct); } catch { }
        }
    }

    private HttpClient CreateHttpClient()
    {
        var h = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(6) };
        h.DefaultRequestHeaders.TryAddWithoutValidation("Origin", _origin);
        return h;
    }

    /// <summary>周期性自动识别：session.list → 每个会话 session.models（模型 + 显示名 + 标题）</summary>
    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RefreshSessionInfoAsync(ct); }
            catch { }
            try { await Task.Delay(20000, ct); } catch { return; }
        }
    }

    /// <summary>立即刷新一次会话信息（事件到达但模型未知时调用）</summary>
    public void RefreshNow()
    {
        _ = Task.Run(async () =>
        {
            try { await RefreshSessionInfoAsync(CancellationToken.None); }
            catch { }
        });
    }

    private async Task RefreshSessionInfoAsync(CancellationToken ct)
    {
        if (_http == null) return;
        var listJson = await RpcAsync("session.list", new { }, ct);
        if (listJson == null) return;
        try
        {
            using var doc = JsonDocument.Parse(listJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var res) || !res.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return;
            if (!res.TryGetProperty("value", out var value)) return;
            if (!value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;
            ProcessSessions(items, ct);
        }
        catch { }
    }

    private void ProcessSessions(JsonElement items, CancellationToken ct)
    {
        bool changed = false;
        lock (_infoLock)
        {
            foreach (var item in items.EnumerateArray())
            {
                var sessionId = item.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;
                if (string.IsNullOrWhiteSpace(sessionId)) continue;
                var title = ExtractTitle(item);
                var known = _sessionInfo.TryGetValue(sessionId!, out var prev);
                if (known && prev is { } pv && pv.Title == title && pv.ModelId != "") continue; // 无变化

                var modelId = "";
                var modelName = "";
                try
                {
                    var mJson = RpcAsync("session.models", new { sessionId = sessionId! }, ct).GetAwaiter().GetResult();
                    if (mJson != null)
                    {
                        using var mdoc = JsonDocument.Parse(mJson);
                        var mr = mdoc.RootElement;
                        if (mr.TryGetProperty("result", out var mv) && mv.TryGetProperty("ok", out var mok) && mok.GetBoolean() &&
                            mv.TryGetProperty("value", out var mval) && mval.TryGetProperty("current", out var cur))
                        {
                            modelId = cur.TryGetProperty("model", out var mm) ? mm.GetString() ?? "" : "";
                            modelName = cur.TryGetProperty("provider", out var mp) ? mp.GetString() ?? "" : "";
                            // 从目录 groups 里找显示名
                            if (mval.TryGetProperty("groups", out var groups))
                            {
                                foreach (var grp in groups.EnumerateArray())
                                {
                                    if (!grp.TryGetProperty("models", out var mods)) continue;
                                    foreach (var mod in mods.EnumerateArray())
                                    {
                                        if (mod.TryGetProperty("id", out var mid) && mid.GetString() == modelId &&
                                            mod.TryGetProperty("name", out var mname))
                                        {
                                            modelName = mname.GetString() ?? modelName;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                if (modelId != "")
                {
                    _sessionInfo[sessionId!] = new SessionInfo(modelId, modelName, title);
                    if (!_knownModels.ContainsKey(modelId)) _knownModels[modelId] = modelName;
                    changed = true;
                }
                else if (known && prev is { ModelId: not "" } pv2)
                {
                    _sessionInfo[sessionId!] = pv2 with { Title = title };
                    changed = true;
                }
            }
        }
        if (changed) SessionInfoChanged?.Invoke();
    }

    private static string ExtractTitle(JsonElement item)
    {
        if (!item.TryGetProperty("projections", out var proj) || !proj.TryGetProperty("values", out var vals) ||
            !vals.TryGetProperty("title", out var t)) return "";
        string? s = null;
        if (t.ValueKind == JsonValueKind.String) s = t.GetString();
        else if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("value", out var tv)) s = tv.GetString();
        return string.IsNullOrWhiteSpace(s) ? "" : (s!.Length > 30 ? s[..30] : s!);
    }

    /// <summary>调用本机 RPC（POST /api/{method}，client-request 信封），返回原始响应 JSON</summary>
    private async Task<string?> RpcAsync(string method, object payload, CancellationToken ct)
    {
        if (_http == null) return null;
        var body = JsonSerializer.Serialize(new
        {
            type = "client-request",
            rpcId = "an-" + Guid.NewGuid().ToString("N")[..16],
            method,
            payload
        });
        try
        {
            using var resp = await _http.PostAsync("/api/" + method,
                new StringContent(body, Encoding.UTF8, "application/json"), ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }
    }

    private async Task<ClientWebSocket> ConnectAsync(string path, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        try
        {
            ws.Options.SetRequestHeader("Origin", _origin);
            await ws.ConnectAsync(new Uri(ToWsUrl(path)), ct);
            return ws;
        }
        catch { ws.Dispose(); throw; }
    }

    private string ToWsUrl(string path) =>
        _baseUrl.Replace("http://", "ws://").Replace("https://", "wss://").TrimEnd('/') + path;

    private async Task ReadLoopAsync(string stream, ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            ms.SetLength(0);
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (res.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, res.Count);
            } while (!res.EndOfMessage && ws.State == WebSocketState.Open);

            var text = Encoding.UTF8.GetString(ms.ToArray());
            try { HandleFrame(stream, text); }
            catch { }
        }
    }

    private void HandleFrame(string stream, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) return;
        var type = payload.TryGetProperty("type", out var t) ? t.GetString() : null;
        var sessionId = payload.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? "" : "";
        switch (type)
        {
            case "session/event" when stream == "mux":
                break;
            case "question/requested":
                if (ModelOf(sessionId) == "") RefreshNow();
                _bus.Publish(new AgentEvent("dsh", EventKind.NeedsUser, sessionId,
                    ExtractQuestionSummary(payload), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ShortAgentId(sessionId), MsgType.Choice, ModelOf(sessionId),
                    ModelNameOf(sessionId), TitleOf(sessionId)));
                break;
            case "approval/requested":
                var tool = payload.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "工具" : "工具";
                var reason = payload.TryGetProperty("reason", out var rz) ? rz.GetString() : null;
                var summary = "权限请求：" + tool +
                    (string.IsNullOrWhiteSpace(reason) ? "" : " · " + reason);
                if (summary.Length > 200) summary = summary[..200];
                if (ModelOf(sessionId) == "") RefreshNow();
                _bus.Publish(new AgentEvent("dsh", EventKind.NeedsUser, sessionId, summary,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ShortAgentId(sessionId), MsgType.Permission, ModelOf(sessionId),
                    ModelNameOf(sessionId), TitleOf(sessionId)));
                break;
            case "host/session-status" when stream == "host":
                var running = payload.TryGetProperty("running", out var rn) && rn.GetBoolean();
                HandleRunningChange(sessionId, running);
                break;
            case "host/session-removed" when stream == "host":
                lock (_infoLock) _sessionInfo.Remove(sessionId);
                break;
        }
    }

    /// <summary>DSH 会话 ID 较长，取前 10 位作为弹窗中的 Agent 短标识</summary>
    private static string ShortAgentId(string sessionId) =>
        string.IsNullOrWhiteSpace(sessionId) ? "" : (sessionId.Length > 10 ? sessionId[..10] : sessionId);

    /// <summary>running 下降沿 = 该会话本轮工作结束</summary>
    private void HandleRunningChange(string sessionId, bool running)
    {
        lock (_runningStates)
        {
            if (_runningStates.TryGetValue(sessionId, out var last) && last && !running)
            {
                _runningStates[sessionId] = false;
                _bus.Publish(new AgentEvent("dsh", EventKind.Done, sessionId,
                    "本轮工作已完成（结果已提交）", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    ShortAgentId(sessionId), MsgType.Result, ModelOf(sessionId),
                    ModelNameOf(sessionId), TitleOf(sessionId)));
                return;
            }
            _runningStates[sessionId] = running;
        }
    }

    private static string ExtractQuestionSummary(JsonElement payload)
    {
        if (payload.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array && qs.GetArrayLength() > 0)
        {
            if (qs[0].TryGetProperty("question", out var txt))
            {
                var s = txt.GetString() ?? "";
                return s.Length > 200 ? s[..200] : s;
            }
        }
        return "DSH 正在等待你的回答";
    }
}
