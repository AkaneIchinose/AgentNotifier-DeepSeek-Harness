using System.IO;
using System.Media;

namespace AgentNotifier.Audio;

/// <summary>播放引擎：音量（采样增益）→ 响铃次数 → 间隔，支持取消</summary>
public sealed class AudioPlayer
{
    private readonly BuiltInSounds _sounds;
    private readonly object _lock = new();
    private SoundPlayer? _current;
    private CancellationTokenSource? _cts;
    private Mp3Player? _mp3;

    public AudioPlayer(BuiltInSounds? sounds = null) { _sounds = sounds ?? new BuiltInSounds(); }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _current?.Stop();
            _current = null;
            try { _mp3?.Stop(); } catch { }
        }
    }

    public async Task PlayAsync(string soundKey, int volume, int repeats, double intervalSec)
    {
        Stop();
        var cts = new CancellationTokenSource();
        byte[]? wav;
        lock (_lock)
        {
            _cts = cts;
            if (soundKey.StartsWith("mp3:", StringComparison.OrdinalIgnoreCase)
                || soundKey.StartsWith("flac:", StringComparison.OrdinalIgnoreCase))
            {
                _mp3 ??= new Mp3Player();
                var prefix = soundKey.StartsWith("mp3:") ? "mp3:" : "flac:";
                var p = Path.Combine(_sounds.CustomDir, soundKey[prefix.Length..]);
                _ = _mp3.PlayAsync(p, volume, repeats, intervalSec);
                return;
            }
            wav = _sounds.GetWav(soundKey);
        }
        if (wav == null) return;

        float[] samples;
        try { samples = WavCodec.Decode(wav); }
        catch { return; }

        var gain = Math.Clamp(volume, 0, 100) / 100f;
        if (gain < 1f) for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
        var outWav = WavCodec.Encode16(samples);

        var count = Math.Clamp(repeats, 1, 10);
        var intervalMs = (int)(Math.Clamp(intervalSec, 0.2, 10) * 1000);
        try
        {
            for (int r = 0; r < count; r++)
            {
                cts.Token.ThrowIfCancellationRequested();
                var sp = new SoundPlayer(new MemoryStream(outWav));
                lock (_lock) _current = sp;
                sp.PlaySync();
                if (r < count - 1) await Task.Delay(intervalMs, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            lock (_lock) { if (ReferenceEquals(_current?.Tag, null)) _current = null; }
        }
    }
}
