using System.Text.Json;
using System.Text.Json.Nodes;
using AgentNotifier.Core;

namespace AgentNotifier.Tools;

public enum ToolKind { ClaudeCode, Dsh }

public sealed record ToolState(ToolKind Kind, bool Hooked, string ConfigPath, string? Error);

/// <summary>接入向导：写入 hooks（备份 → 合并 → 回滚）、预览、一键恢复脚本生成</summary>
public sealed class WizardService
{
    private readonly ConfigStore _cfg;
    public string AppDataDir => _cfg.BaseDir;
    public string HelperPath => Path.Combine(AppDataDir, "notify.ps1");
    public string TokenPath => Path.Combine(AppDataDir, "token.txt");
    public string PortPath => Path.Combine(AppDataDir, "port.txt");
    public string BackupDir => Path.Combine(AppDataDir, "backups");

    public WizardService(ConfigStore cfg) { _cfg = cfg; }

    public static string ClaudeConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    // ---------- 状态 ----------
    public ToolState GetState(ToolKind kind)
    {
        try
        {
            switch (kind)
            {
                case ToolKind.ClaudeCode:
                    var cp = ClaudeConfigPath;
                    return new ToolState(kind, HasOurHooks(cp, "hooks", "Notification"), cp,
                        File.Exists(cp) ? null : "配置文件不存在（claude 首次运行后自动生成）");
                default:
                    return new ToolState(kind, false, "", "DSH 经 WebSocket 事件流监听，无需配置");
            }
        }
        catch (Exception ex) { return new ToolState(kind, false, "", ex.Message); }
    }

    private static bool HasOurHooks(string configPath, string rootKey, string eventKey)
    {
        if (!File.Exists(configPath)) return false;
        var root = JsonNode.Parse(File.ReadAllText(configPath));
        var hooks = root?[rootKey]?.AsObject();
        if (hooks == null) return false;
        if (hooks[eventKey] is not JsonArray arr) return false;
        foreach (var entry in arr)
        {
            if (entry?["hooks"] is not JsonArray hs) continue;
            foreach (var h in hs)
            {
                var cmd = h?["command"]?.GetValue<string>() ?? "";
                if (cmd.Contains("notify.ps1", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    // ---------- 接入 ----------
    public (bool ok, string msg) Install(ToolKind kind)
    {
        try
        {
            EnsureHelperFiles();
            return kind switch
            {
                ToolKind.ClaudeCode => PatchConfig(ClaudeConfigPath, "hooks",
                    new[] {
                        ("Notification", "needs_user", "*"),
                        ("PreToolUse", "needs_user", "*"),
                        ("Stop", "done", "*")
                    }, "claude"),
                _ => (false, "DSH 经 WebSocket 事件流监听，无需配置")
            };
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private (bool, string) PatchConfig(string configPath, string rootKey, (string Event, string Kind, string Matcher)[] mappings, string tool)
    {
        var existed = File.Exists(configPath);
        var original = existed ? File.ReadAllText(configPath) : "{}";
        var root = JsonNode.Parse(original)?.AsObject() ?? new JsonObject();

        if (existed)
        {
            Directory.CreateDirectory(BackupDir);
            var bak = Path.Combine(BackupDir, Path.GetFileName(configPath) + ".bak");
            if (!File.Exists(bak)) File.WriteAllText(bak, original);   // 只保留第一份原始备份
        }

        var hooks = root[rootKey]?.AsObject() ?? new JsonObject();
        foreach (var (evt, kind2, matcher) in mappings)
        {
            var arr = hooks[evt] as JsonArray ?? new JsonArray();
            var found = false;
            foreach (var entry in arr)
                if (entry?["hooks"] is JsonArray hs)
                    foreach (var h in hs)
                        if ((h?["command"]?.GetValue<string>() ?? "").Contains("notify.ps1", StringComparison.OrdinalIgnoreCase))
                            found = true;
            if (!found)
            {
                arr.Add(new JsonObject
                {
                    ["matcher"] = matcher,
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "command", ["command"] = HelperCommand(kind2, tool) }
                    }
                });
            }
            hooks[evt] = arr;
        }
        root[rootKey] = hooks;

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return (true, "已写入 " + configPath + "；事件映射：" + string.Join("、", mappings.Select(m => m.Event + " → " + m.Kind)));
    }

    private string HelperCommand(string kind, string tool) =>
        $"powershell -NoProfile -ExecutionPolicy Bypass -File '{HelperPath}' -Kind {kind} -Tool {tool}";

    // ---------- 预览 ----------
    public string Preview(ToolKind kind)
    {
        try
        {
            if (kind != ToolKind.ClaudeCode) return "DSH 经 WebSocket 事件流监听，无需配置预览";
            var path = ClaudeConfigPath;
            var rootKey = "hooks";
            var mappings = new[] { ("Notification", "needs_user", "*"), ("PreToolUse", "needs_user", "*"), ("Stop", "done", "*") };
            var tool = "claude";
            var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject() : new JsonObject();
            var hooks = root[rootKey]?.AsObject() ?? new JsonObject();
            foreach (var (evt, kind2, matcher) in mappings)
            {
                var arr = hooks[evt] as JsonArray ?? new JsonArray();
                var found = arr.Any(e => e?["hooks"] is JsonArray hs &&
                    hs.Any(h => (h?["command"]?.GetValue<string>() ?? "").Contains("notify.ps1", StringComparison.OrdinalIgnoreCase)));
                if (!found)
                    arr.Add(new JsonObject
                    {
                        ["matcher"] = matcher,
                        ["hooks"] = new JsonArray { new JsonObject { ["type"] = "command", ["command"] = HelperCommand(kind2, tool) } }
                    });
                hooks[evt] = arr;
            }
            root[rootKey] = hooks;
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { return "预览失败：" + ex.Message; }
    }

    // ---------- 回滚 ----------
    public (bool ok, string msg) Rollback(ToolKind kind)
    {
        try
        {
            if (kind != ToolKind.ClaudeCode) return (true, "DSH 无需回滚（零配置）");
            var path = ClaudeConfigPath;
            if (!File.Exists(path)) return (true, "配置文件不存在，无需回滚");
            var bak = Path.Combine(BackupDir, Path.GetFileName(path) + ".bak");
            if (File.Exists(bak))
            {
                File.WriteAllText(path, File.ReadAllText(bak));
                return (true, "已从备份还原：" + bak);
            }
            return (true, "没有找到备份；可运行 uninstall.ps1 一键清理（会删除写入的 hooks 并还原）");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ---------- 辅助文件 ----------
    public string ToastHelperPath => Path.Combine(AppDataDir, "toast.ps1");

    public void EnsureHelperFiles()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            WriteUtf8Bom(HelperPath, PsScriptTemplates.NotifyHelper);
            WriteUtf8Bom(ToastHelperPath, PsScriptTemplates.ToastHelper);
            File.WriteAllText(TokenPath, _cfg.Config.Server.Token);
            File.WriteAllText(PortPath, _cfg.Config.Server.Port.ToString());
        }
        catch { /* 数据目录不可写时降级：hook 上报与系统 Toast 不可用，不阻塞应用 */ }
    }

    /// <summary>以 UTF-8 BOM 写脚本文件（Windows PowerShell 5.1 解析中文必需）</summary>
    private static void WriteUtf8Bom(string path, string content)
    {
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(true));
    }

    /// <summary>把 uninstall.ps1 写入指定目录（如软件所在目录 / 项目文件夹）</summary>
    public string WriteUninstallScript(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var p = Path.Combine(targetDir, "uninstall.ps1");
        WriteUtf8Bom(p, PsScriptTemplates.Uninstall);
        return p;
    }
}
