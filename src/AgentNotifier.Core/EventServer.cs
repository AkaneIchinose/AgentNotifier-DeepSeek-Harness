using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AgentNotifier.Core;

/// <summary>
/// 本机 HTTP 事件服务：TcpListener 手写极简 HTTP/1.1（零系统依赖），
/// 仅监听 127.0.0.1，Bearer 令牌校验。
/// </summary>
public sealed class EventServer
{
    private readonly ConfigStore _cfg;
    private readonly EventBus _bus;
    private TcpListener? _listener;
    public int Port => _cfg.Config.Server.Port;
    public bool Running { get; private set; }

    public EventServer(ConfigStore cfg, EventBus bus) { _cfg = cfg; _bus = bus; }

    public void Start(CancellationToken ct)
    {
        if (Running) return;
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        Running = true;
        _ = Task.Run(() => LoopAsync(ct));
    }

    public void Stop()
    {
        try { _listener?.Stop(); } catch { }
        Running = false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(client));
        }
        Running = false;
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var req = await ReadRequestAsync(stream);
                if (req == null) return;

                if (req.Method == "GET" && req.Path == "/v1/ping")
                {
                    await WriteResponseAsync(stream, 200, "{\"ok\":true,\"version\":\"0.3.0\",\"port\":" + Port + "}");
                    return;
                }
                if (req.Method == "POST" && req.Path == "/v1/event")
                {
                    var auth = req.Headers.GetValueOrDefault("Authorization") ?? "";
                    var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..] : "";
                    if (token != _cfg.Config.Server.Token)
                    {
                        await WriteResponseAsync(stream, 401, "{\"ok\":false,\"error\":\"unauthorized\"}");
                        return;
                    }
                    var json = req.Body.TrimStart('\uFEFF');
                    var dto = JsonSerializer.Deserialize<EventDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dto == null || string.IsNullOrWhiteSpace(dto.Tool) || dto.Kind is not ("needs_user" or "done"))
                    {
                        await WriteResponseAsync(stream, 400, "{\"ok\":false,\"error\":\"bad payload\"}");
                        return;
                    }
                    var kind = dto.Kind == "needs_user" ? EventKind.NeedsUser : EventKind.Done;
                    var summary = dto.Summary ?? "";
                    if (summary.Length > 200) summary = summary[..200];
                    var msg = (dto.Msg ?? "").ToLowerInvariant() switch
                    {
                        "choice" => MsgType.Choice,
                        "permission" => MsgType.Permission,
                        "result" => MsgType.Result,
                        _ => MsgType.Generic
                    };
                    _bus.Publish(new AgentEvent(dto.Tool, kind, dto.SessionId ?? "", summary,
                        dto.Ts > 0 ? dto.Ts : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        dto.Agent ?? "", msg, dto.Model ?? ""));
                    await WriteResponseAsync(stream, 200, "{\"ok\":true}");
                    return;
                }
                await WriteResponseAsync(stream, 404, "{\"ok\":false,\"error\":\"not found\"}");
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "server-errors.log"),
                        DateTime.Now + " " + ex + Environment.NewLine);
                }
                catch { }
                try { await WriteResponseAsync(stream, 500, "{\"ok\":false,\"error\":\"internal\"}"); } catch { }
            }
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(Stream stream)
    {
        var buf = new byte[8192];
        var streamData = new MemoryStream();
        int headerEnd = -1;
        while (true)
        {
            int n = await stream.ReadAsync(buf);
            if (n <= 0) return null;
            streamData.Write(buf, 0, n);
            var text = Encoding.UTF8.GetString(streamData.ToArray());
            headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0 || streamData.Length > 65536) break;
        }
        if (headerEnd < 0) return null;
        var data = streamData.ToArray();
        var headerText = Encoding.UTF8.GetString(data, 0, headerEnd);
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        var first = lines[0].Split(' ');
        if (first.Length < 2) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var idx = line.IndexOf(':');
            if (idx > 0) headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        var body = "";
        if (headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var len) && len > 0)
        {
            var bodyStart = headerEnd + 4;
            var have = data.Length - bodyStart;
            if (have < len)
            {
                var rest = new byte[len - have];
                int read = 0;
                while (read < rest.Length)
                {
                    int n = await stream.ReadAsync(rest.AsMemory(read));
                    if (n <= 0) break;
                    read += n;
                }
                body = Encoding.UTF8.GetString(data, bodyStart, have) + Encoding.UTF8.GetString(rest, 0, read);
            }
            else body = Encoding.UTF8.GetString(data, bodyStart, len);
        }
        return new HttpRequest(first[0], first[1], headers, body);
    }

    private static async Task WriteResponseAsync(Stream stream, int status, string json)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", _ => "Internal Server Error" };
        var body = Encoding.UTF8.GetBytes(json);
        var head = Encoding.UTF8.GetBytes(
            "HTTP/1.1 " + status + " " + reason + "\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            "Content-Length: " + body.Length + "\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Headers, string Body);

    private sealed class EventDto
    {
        public string? Tool { get; set; }
        public string? Kind { get; set; }
        public string? SessionId { get; set; }
        public string? Summary { get; set; }
        public long Ts { get; set; }
        /// <summary>可选：choice / permission / result / generic（缺省按 Kind 推断为通用）</summary>
        public string? Msg { get; set; }
        /// <summary>可选：来源代理标识（如 DSH 会话短 ID 或工具名）</summary>
        public string? Agent { get; set; }
        /// <summary>可选：会话所用模型名（如 deepseek-v4-flash），用于弹窗模型样式匹配</summary>
        public string? Model { get; set; }
    }
}
