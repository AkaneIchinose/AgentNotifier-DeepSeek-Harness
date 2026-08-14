using System.IO;

namespace AgentNotifier.Audio;

/// <summary>内置音效库：合成音效 + Windows 经典提示音 + 自定义导入（custom: 前缀）</summary>
public sealed class BuiltInSounds
{
    private static readonly string[] SynthKeys = { "soft-chime", "clear-alert", "bell-melody", "tech-blip", "retro" };
    private static readonly (string Key, string WinFile)[] WindowsSounds =
    {
        ("windows-notify", "Windows Notify.wav"),
        ("windows-ding", "Windows Ding.wav"),
        ("windows-chord", "Windows Chord.wav"),
        ("windows-exclamation", "Windows Exclamation.wav"),
        ("windows-asterisk", "Windows Asterisk.wav"),
    };

    public string CustomDir { get; }
    private readonly string _winMedia;

    public BuiltInSounds(string? customDir = null)
    {
        CustomDir = customDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentNotifier", "custom");
        _winMedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
    }

    public IReadOnlyList<string> AllKeys()
    {
        var list = new List<string>(SynthKeys);
        foreach (var (k, f) in WindowsSounds)
            if (File.Exists(Path.Combine(_winMedia, f))) list.Add(k);
        try
        {
            if (Directory.Exists(CustomDir))
            {
                foreach (var f in Directory.GetFiles(CustomDir, "*.wav"))
                    list.Add("custom:" + Path.GetFileName(f));
                foreach (var f in Directory.GetFiles(CustomDir, "*.mp3"))
                    list.Add("mp3:" + Path.GetFileName(f));
                foreach (var f in Directory.GetFiles(CustomDir, "*.flac"))
                    list.Add("flac:" + Path.GetFileName(f));
            }
        }
        catch { }
        return list;
    }

    public string DisplayName(string key)
    {
        if (key.StartsWith("mp3:")) return Path.GetFileNameWithoutExtension(key["mp3:".Length..]) + "（自定义 MP3）";
        if (key.StartsWith("flac:")) return Path.GetFileNameWithoutExtension(key["flac:".Length..]) + "（自定义 FLAC）";
        if (key.StartsWith("custom:")) return Path.GetFileNameWithoutExtension(key["custom:".Length..]) + "（自定义）";
        foreach (var (k, f) in WindowsSounds) if (k == key) return f.Replace(".wav", "") + "（Windows 经典）";
        return key switch
        {
            "soft-chime" => "柔和叮咚（合成）",
            "clear-alert" => "清脆提示（合成）",
            "bell-melody" => "悠扬铃声（合成）",
            "tech-blip" => "科技脉冲（合成）",
            "retro" => "复古通知（合成）",
            _ => key
        };
    }

    public bool Exists(string key)
    {
        if (SynthKeys.Contains(key)) return true;
        foreach (var (k, f) in WindowsSounds) if (k == key) return File.Exists(Path.Combine(_winMedia, f));
        if (key.StartsWith("custom:")) return File.Exists(Path.Combine(CustomDir, key["custom:".Length..]));
        return false;
    }

    public byte[]? GetWav(string key)
    {
        if (key.StartsWith("flac:"))
        {
            var p = Path.Combine(CustomDir, key["flac:".Length..]);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
        if (key.StartsWith("mp3:"))
        {
            var p = Path.Combine(CustomDir, key["mp3:".Length..]);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
        if (key.StartsWith("custom:"))
        {
            var p = Path.Combine(CustomDir, key["custom:".Length..]);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
        if (SynthKeys.Contains(key))
        {
            return key switch
            {
                "soft-chime" => WaveGenerator.SoftChime(),
                "clear-alert" => WaveGenerator.ClearAlert(),
                "bell-melody" => WaveGenerator.BellMelody(),
                "tech-blip" => WaveGenerator.TechBlip(),
                _ => WaveGenerator.Retro()
            };
        }
        foreach (var (k, f) in WindowsSounds)
            if (k == key) { var p = Path.Combine(_winMedia, f); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
        return null;
    }

    /// <summary>导入自定义音频：WAV(PCM) 或 MP3；mp3 播放走 MediaPlayer（MediaFoundation）</summary>
    public (bool ok, string key) Import(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext == ".mp3" || ext == ".flac")
            {
                var data = File.ReadAllBytes(sourcePath);
                if (data.Length < 100) return (false, "文件过小或已损坏");
                Directory.CreateDirectory(CustomDir);
                var suffix = ext == ".mp3" ? "mp3:" : "flac:";
                var dest = Path.Combine(CustomDir, Path.GetFileName(sourcePath));
                if (File.Exists(dest)) dest = Path.Combine(CustomDir, Guid.NewGuid().ToString("N") + ext);
                File.WriteAllBytes(dest, data);
                return (true, suffix + Path.GetFileName(dest));
            }
            var wavData = File.ReadAllBytes(sourcePath);
            if (!WavCodec.IsWav(wavData)) return (false, "仅支持 WAV(PCM) 或 MP3 文件；flac/ogg 暂不支持");
            Directory.CreateDirectory(CustomDir);
            var wavDest = Path.Combine(CustomDir, Path.GetFileName(sourcePath));
            if (File.Exists(wavDest)) wavDest = Path.Combine(CustomDir, Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(wavDest, wavData);
            return (true, "custom:" + Path.GetFileName(wavDest));
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public void Delete(string key)
    {
        if (key.StartsWith("mp3:") || key.StartsWith("flac:"))
        {
            var prefix = key.StartsWith("mp3:") ? "mp3:" : "flac:";
            var p = Path.Combine(CustomDir, key[prefix.Length..]);
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
        else if (key.StartsWith("custom:"))
        {
            var p = Path.Combine(CustomDir, key["custom:".Length..]);
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }
}
