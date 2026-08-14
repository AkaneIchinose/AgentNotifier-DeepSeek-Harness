using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentNotifier.Audio;
using AgentNotifier.Core;

if (args.Length > 0 && args[0] == "--icon")
{
    MakeIcon(args.Length > 1 ? args[1] : "agentnotifier.ico");
    return 0;
}

// 生成应用图标（蓝色圆 + 白色点，多尺寸 ICO）
void MakeIcon(string path)
{
    var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
    var images = new List<(int Size, byte[] Data)>();
    foreach (var s in sizes)
    {
        using var bmp = new System.Drawing.Bitmap(s, s);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(79, 195, 247));
            g.FillEllipse(brush, 0, 0, s - 1, s - 1);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, Math.Max(2f, s / 7f));
            var dot = (int)(s * 0.42f);
            g.DrawEllipse(pen, dot, dot, s - 2 * dot - 1, s - 2 * dot - 1);
        }
        using var ms = new MemoryStream();
        if (s >= 256) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); images.Add((s, ms.ToArray())); }
        else
        {
            // 32bpp BMP（不含文件头）
            var stride = s * 4;
            using var bmp32 = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp32))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(79, 195, 247));
                g.FillEllipse(brush, 0, 0, s - 1, s - 1);
                using var pen = new System.Drawing.Pen(System.Drawing.Color.White, Math.Max(2f, s / 7f));
                var dot = (int)(s * 0.42f);
                g.DrawEllipse(pen, dot, dot, s - 2 * dot - 1, s - 2 * dot - 1);
            }
            var bits = new byte[s * stride];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    var c = bmp32.GetPixel(x, y);
                    var off = y * stride + x * 4;
                    bits[off] = c.B; bits[off + 1] = c.G; bits[off + 2] = c.R; bits[off + 3] = c.A;
                }
            var and = new byte[s * ((s + 31) / 32) * 4];
            images.Add((s, bits.Concat(and).ToArray()));
        }
    }
    using var fs = File.Create(path);
    using var w = new BinaryWriter(fs);
    w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
    var offset = 6 + images.Count * 16;
    foreach (var (size, data) in images)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32);
        w.Write(data.Length); w.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in images) w.Write(data);
    Console.WriteLine("ICON: " + path + " (" + images.Count + " sizes)");
}

var failures = new List<string>();
void Check(bool cond, string name)
{
    Console.WriteLine((cond ? "[PASS] " : "[FAIL] ") + name);
    if (!cond) failures.Add(name);
}

var testDir = Path.Combine(Path.GetTempPath(), "agentnotifier-smoke-" + Guid.NewGuid().ToString("N"));
var cfg = new ConfigStore(testDir);
cfg.Load();
cfg.Config.Server.Port = 28991;
cfg.Config.DebounceSec = 60;
cfg.Save();

var bus = new EventBus();
var engine = new ReminderEngine(cfg, bus);
var rings = new List<AgentEvent>();
var toasts = new List<AgentEvent>();
engine.RingRequested += rings.Add;
engine.ToastRequested += toasts.Add;

var server = new EventServer(cfg, bus);
using var cts = new CancellationTokenSource();
server.Start(cts.Token);
Check(server.Running, "事件服务已启动 (127.0.0.1:" + cfg.Config.Server.Port + ")");

using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + cfg.Config.Server.Port) };

var ping = await client.GetStringAsync("/v1/ping");
Check(ping.Contains("\"ok\":true"), "GET /v1/ping 正常");

var bad = new HttpClient { BaseAddress = client.BaseAddress };
var badResp = await bad.PostAsync("/v1/event", new StringContent("{}", Encoding.UTF8, "application/json"));
Check(badResp.StatusCode == HttpStatusCode.Unauthorized, "无令牌返回 401");

client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Config.Server.Token);

async Task Post(string tool, string kind, string session, string summary)
{
    var body = JsonSerializer.Serialize(new { tool, kind, sessionId = session, summary, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    var resp = await client.PostAsync("/v1/event", new StringContent(body, Encoding.UTF8, "application/json"));
    var respText = await resp.Content.ReadAsStringAsync();
    Console.WriteLine("[INFO] POST " + tool + "/" + kind + " -> " + (int)resp.StatusCode + " body=" + respText);
    Check(resp.StatusCode == HttpStatusCode.OK, "POST " + tool + "/" + kind + " -> 200");
}

await Post("claude", "needs_user", "s1", "claude 等待批准");
await Post("claude", "needs_user", "s1", "claude 等待批准(重复)");
Check(rings.Count == 1, "去抖：同会话同类事件只响 1 次（实际 " + rings.Count + "）");
Check(toasts.Count == 1, "去抖：通知也只 1 次");

await Post("dsh", "done", "s2", "任务完成");
Check(rings.Count == 2 && rings[1].Tool == "dsh" && rings[1].Kind == EventKind.Done, "不同工具/事件分别触发");
Check(toasts.Count == 2, "通知同样触发");

cfg.Update(c => c.Muted = true);
await Post("claude", "done", "s3", "静音测试");
Check(rings.Count == 2, "静音时不响铃（实际 " + rings.Count + "）");
Check(toasts.Count == 3, "静音时仍弹通知");
cfg.Update(c => c.Muted = false);

var now = DateTime.Now;
cfg.Update(c => c.Dnd.Add(new DndRule
{
    Start = now.AddMinutes(-10).ToString("HH:mm"),
    End = now.AddMinutes(10).ToString("HH:mm"),
    Mode = "silent"
}));
await Post("claude", "needs_user", "s4", "勿扰测试");
Check(rings.Count == 2 && toasts.Count == 3, "勿扰(silent)：不响铃不通知");
cfg.Update(c => c.Dnd.Clear());
cfg.Update(c => c.Dnd.Add(new DndRule
{
    Start = now.AddMinutes(-10).ToString("HH:mm"),
    End = now.AddMinutes(10).ToString("HH:mm"),
    Mode = "toast_only"
}));
await Post("claude", "done", "s5", "勿扰测试2");
Check(rings.Count == 2 && toasts.Count == 4, "勿扰(toast_only)：仅通知不响铃");
cfg.Update(c => c.Dnd.Clear());

var sounds = new BuiltInSounds(Path.Combine(testDir, "custom"));
var soundCount = 0;
foreach (var k in sounds.AllKeys())
{
    soundCount++;
    var wav = sounds.GetWav(k);
    Check(wav != null && wav.Length > 100, "音效存在：" + k);
    if (wav != null)
    {
        var samples = WavCodec.Decode(wav);
        Check(samples.Length > 1000, "解码正常：" + k + " (" + samples.Length + " samples)");
    }
}
Check(soundCount >= 5, "内置音效数量 >= 5（实际 " + soundCount + "）");

var player = new AudioPlayer(sounds);
await player.PlayAsync("soft-chime", 50, 1, 0);
Console.WriteLine("[INFO] soft-chime 已实际播放一次（音量 50）");

// ---- mp3 导入与 MediaPlayer 播放链路 ----
var fakeMp3 = Path.Combine(Path.GetTempPath(), "fake-" + Guid.NewGuid().ToString("N") + ".mp3");
File.WriteAllBytes(fakeMp3, new byte[256]);
var imp = sounds.Import(fakeMp3);
Check(imp.ok && imp.key.StartsWith("mp3:"), "MP3 导入成功（" + (imp.ok ? imp.key : imp.key) + "）");
Check(sounds.AllKeys().Any(k => k.StartsWith("mp3:")), "MP3 出现在音效列表");
var winMedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify.wav");
if (File.Exists(winMedia))
{
    try
    {
        using var mp3p = new Mp3Player();
        await mp3p.PlayAsync(winMedia, 40, 1, 0);
        Console.WriteLine("[INFO] MediaPlayer 播放链路正常（Windows Notify.wav）");
        Check(true, "MediaPlayer 播放链路（STA+Dispatcher）");
    }
    catch (Exception ex) { Check(false, "MediaPlayer 播放链路异常: " + ex.Message); }
}
else Console.WriteLine("[SKIP] Windows Notify.wav 不存在，跳过 MediaPlayer 实测");
try { File.Delete(fakeMp3); } catch { }

// ---- notify.ps1 helper 端到端：模拟真实 hook 调用 ----
var e2eDir = Path.Combine(Path.GetTempPath(), "agentnotifier-e2e-" + Guid.NewGuid().ToString("N"));
var e2eCfg = new ConfigStore(Path.Combine(e2eDir, "AgentNotifier"));
e2eCfg.Load();
e2eCfg.Config.Server.Port = 28993;
e2eCfg.Save();
var wizard = new AgentNotifier.Tools.WizardService(e2eCfg);
wizard.EnsureHelperFiles();
Check(File.Exists(wizard.HelperPath), "notify.ps1 已生成");
Check(File.Exists(wizard.TokenPath), "token.txt 已生成");

var bus2 = new EventBus();
var got = new List<AgentEvent>();
bus2.Raised += e => { lock (got) got.Add(e); };
var server2 = new EventServer(e2eCfg, bus2);
using var cts2 = new CancellationTokenSource();
server2.Start(cts2.Token);

var psi = new System.Diagnostics.ProcessStartInfo("powershell",
    "-NoProfile -ExecutionPolicy Bypass -File \"" + wizard.HelperPath + "\" -Kind needs_user -Tool claude")
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    UseShellExecute = false,
    CreateNoWindow = true
};
psi.Environment["APPDATA"] = e2eDir;
// 先验证 powershell 子进程 stdin 管道
try
{
    var stdIn = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
        "-NoProfile -Command \"$t=[Console]::In.ReadToEnd(); Write-Output ('STDIN-GOT:'+$t)\"" )
    { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
    stdIn.StartInfo.Environment["APPDATA"] = e2eDir;
    await stdIn.StandardInput.WriteAsync("hello-stdin");
    stdIn.StandardInput.Close();
    var stdOut = await stdIn.StandardOutput.ReadToEndAsync();
    await stdIn.WaitForExitAsync();
    Check(stdOut.Contains("STDIN-GOT:hello-stdin"), "powershell 子进程 stdin 管道正常（输出: " + stdOut.Trim() + "）");
}
catch (Exception ex) { Check(false, "stdin 管道测试异常: " + ex.Message); }

// 用带诊断的 debug 版 helper 执行
var debugHelper = File.ReadAllText(wizard.HelperPath)
    .Replace("$ErrorActionPreference = 'SilentlyContinue'", "$ErrorActionPreference = 'Continue'")
    .Replace("curl.exe -s -o NUL", "curl.exe -s -o NUL -w \"[CURL-HTTP:%{http_code}]\"")
    .Replace("} catch { }", "} catch { Write-Output ('[HELPER-CATCH] ' + $_.Exception.Message) }")
    .Replace("if (-not $token) { exit 0 }", "if (-not $token) { Write-Output '[HELPER-NO-TOKEN]'; exit 0 }");
var debugPath = Path.Combine(e2eDir, "notify-debug.ps1");
File.WriteAllText(debugPath, debugHelper);
try
{
    var psi2 = new System.Diagnostics.ProcessStartInfo("powershell",
        "-NoProfile -ExecutionPolicy Bypass -File \"" + debugPath + "\" -Kind needs_user -Tool claude")
    { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    psi2.Environment["APPDATA"] = e2eDir;
    var proc2 = System.Diagnostics.Process.Start(psi2)!;
    await proc2.StandardInput.WriteAsync("{\"session_id\":\"e2e-session\",\"hook_event_name\":\"Notification\"}");
    proc2.StandardInput.Close();
    var helperOut = await proc2.StandardOutput.ReadToEndAsync();
    var helperErr = await proc2.StandardError.ReadToEndAsync();
    await proc2.WaitForExitAsync();
    Console.WriteLine("[INFO] helper 输出: " + helperOut.Trim());
    if (helperErr.Trim() != "") Console.WriteLine("[INFO] helper 错误: " + helperErr.Trim());
    Check(proc2.ExitCode == 0, "notify.ps1 退出码 0");
}
catch (Exception ex) { Check(false, "notify.ps1 无法启动: " + ex.Message); }

await Task.Delay(800);
Check(got.Count == 1 && got[0].Tool == "claude" && got[0].Kind == EventKind.NeedsUser && got[0].SessionId == "e2e-session",
    "helper 上报事件被服务接收（tool=" + (got.Count > 0 ? got[0].Tool : "?") + " kind=" + (got.Count > 0 ? got[0].Kind : "?") + "）");
server2.Stop();
try { Directory.Delete(e2eDir, true); } catch { }
try { Directory.Delete(Path.Combine(Path.GetTempPath(), "agentnotifier-e2e-*"), true); } catch { }

// ---- 主题机制测试（STA + WPF 资源字典加载与切换） ----
var themeResults = await Task.Run(() =>
{
    var results = new List<string>();
    var thread = new Thread(() =>
    {
        try
        {
            var app = new System.Windows.Application();
            var light = new System.Windows.ResourceDictionary
            { Source = new Uri("pack://application:,,,/AgentNotifier;component/Themes/LightColors.xaml") };
            var dark = new System.Windows.ResourceDictionary
            { Source = new Uri("pack://application:,,,/AgentNotifier;component/Themes/DarkColors.xaml") };
            var cardLight = ((System.Windows.Media.SolidColorBrush)light["CardBrush"]).Color.ToString();
            var cardDark = ((System.Windows.Media.SolidColorBrush)dark["CardBrush"]).Color.ToString();
            app.Resources.MergedDictionaries.Add(light);
            var c1 = ((System.Windows.Media.SolidColorBrush)app.Resources["CardBrush"]).Color.ToString();
            app.Resources.MergedDictionaries.Remove(light);
            app.Resources.MergedDictionaries.Add(dark);
            var c2 = ((System.Windows.Media.SolidColorBrush)app.Resources["CardBrush"]).Color.ToString();
            results.Add("LIGHT_CARD=" + cardLight);
            results.Add("DARK_CARD=" + cardDark);
            results.Add("SWITCH_OK=" + (c1 != c2));
        }
        catch (Exception ex) { results.Add("THEME-ERR: " + ex.Message); }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(10000);
    return results;
});
foreach (var r in themeResults) Console.WriteLine("[INFO] " + r);
Check(themeResults.Any(r => r.Contains("LIGHT_CARD=#FFFFFFFF"))
    && themeResults.Any(r => r.Contains("DARK_CARD=#FF252B34"))
    && themeResults.Contains("SWITCH_OK=True"),
    "主题机制：浅/深字典加载与动态切换");

// ---- toast.ps1 系统 Toast 链路测试 ----
try
{
    wizard.EnsureHelperFiles();
    Check(File.Exists(wizard.ToastHelperPath), "toast.ps1 已生成");
    var tpsi = new System.Diagnostics.ProcessStartInfo("powershell",
        "-NoProfile -ExecutionPolicy Bypass -File \"" + wizard.ToastHelperPath + "\"")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    tpsi.Environment["AN_TITLE"] = "AgentNotifier 测试";
    tpsi.Environment["AN_BODY"] = "系统 Toast 链路验证";
    var tproc = System.Diagnostics.Process.Start(tpsi)!;
    var tOut = await tproc.StandardOutput.ReadToEndAsync();
    var tErr = await tproc.StandardError.ReadToEndAsync();
    await tproc.WaitForExitAsync();
    if (tOut.Trim() != "") Console.WriteLine("[INFO] toast 输出: " + tOut.Trim());
    if (tErr.Trim() != "") Console.WriteLine("[INFO] toast 错误: " + tErr.Trim());
    Check(tproc.ExitCode == 0, "toast.ps1 执行成功（系统 Toast WinRT 链路）");
}
catch (Exception ex) { Check(false, "toast.ps1 异常: " + ex.Message); }

// ---- DSH 监听器集成测试（本机 DSH 运行则实测连接，否则跳过） ----
var dshBus = new EventBus();
var dshEvents = new List<AgentEvent>();
dshBus.Raised += e => { lock (dshEvents) dshEvents.Add(e); };
var monitor = new DshMonitor(dshBus);
var dshConnected = false;
monitor.ConnectionChanged += c => dshConnected = c;
monitor.Start();
await Task.Delay(4500);
if (dshConnected)
{
    Check(true, "DSH WebSocket 事件流已连接（本机 DSH 运行中）");
    Console.WriteLine("[INFO] DSH mux/host 监听已建立（事件数=" + dshEvents.Count + "）");
}
else Console.WriteLine("[SKIP] 本机 DSH 未运行，跳过 DSH 监听实测");
monitor.Stop();

server.Stop();
try { Directory.Delete(testDir, true); } catch { }

Console.WriteLine(failures.Count == 0 ? "\n=== 全部通过 ===" : "\n=== 失败项: " + string.Join("; ", failures) + " ===");
return failures.Count == 0 ? 0 : 1;
