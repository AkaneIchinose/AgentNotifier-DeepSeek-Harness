using System.Security.Cryptography;
using System.Text.Json;

namespace AgentNotifier.Core;

/// <summary>配置存取：%APPDATA%\AgentNotifier\config.json；首次生成随机令牌</summary>
public sealed class ConfigStore
{
    public string BaseDir { get; }
    public string ConfigPath { get; }
    public AppConfig Config { get; private set; } = new();
    public event Action? Changed;

    public ConfigStore(string? baseDir = null)
    {
        BaseDir = baseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentNotifier");
        ConfigPath = Path.Combine(BaseDir, "config.json");
    }

    public void Load()
    {
        var existed = File.Exists(ConfigPath);
        try
        {
            Directory.CreateDirectory(BaseDir);
            if (existed)
                Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
        }
        catch { Config = new AppConfig(); }

        if (string.IsNullOrWhiteSpace(Config.Server.Token)) Config.Server.Token = CreateToken();
        if (Config.Server.Port <= 0 || Config.Server.Port > 65535) Config.Server.Port = 28150;
        if (Config.NeedsUser.File == "") Config.NeedsUser.File = "soft-chime";
        if (Config.Done.File == "") Config.Done.File = "clear-alert";
        if (Config.Models.Count == 0)
        {
            // 预置常用模型样式（可在通知页增删改，删空后下次启动自动恢复）；
            // flash/pro 使用内嵌默认横幅图（builtin:fish，随 exe 打包，无路径信息）。
            Config.Models.Add(new ModelStyle { ModelId = "deepseek-v4-flash", Name = "DeepSeek-V4-Flash", Color = "#4FC3F7", ImagePath = "builtin:fish" });
            Config.Models.Add(new ModelStyle { ModelId = "deepseek-v4-pro", Name = "DeepSeek-V4-Pro", Color = "#A78BFA", ImagePath = "builtin:fish" });
            Config.Models.Add(new ModelStyle { Name = "r1", Color = "#A78BFA" });
            Config.Models.Add(new ModelStyle { Name = "claude", Color = "#F59E0B" });
            Config.Models.Add(new ModelStyle { Name = "deepseek", Color = "#2DD4BF" });
        }
        Save();
    }

    public static string CreateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
        Changed?.Invoke();
    }

    public void Update(Action<AppConfig> mutate)
    {
        mutate(Config);
        Save();
    }
}
