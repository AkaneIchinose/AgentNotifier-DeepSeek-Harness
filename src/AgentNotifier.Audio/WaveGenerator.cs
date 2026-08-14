namespace AgentNotifier.Audio;

/// <summary>程序化合成音效（无版权风险），正弦 + 快起音/缓释包络</summary>
public static class WaveGenerator
{
    public static byte[] Tone(double[] notesHz, double secPerNote, double gain = 0.55)
    {
        var rate = WavCodec.SampleRate;
        var total = (int)(notesHz.Length * secPerNote * rate);
        var samples = new float[total];
        int pos = 0;
        foreach (var freq in notesHz)
        {
            var n = (int)(secPerNote * rate);
            var attack = Math.Max(1, (int)(rate * 0.008));
            var release = Math.Max(1, (int)(rate * 0.09));
            for (int i = 0; i < n && pos < total; i++)
            {
                var env = Math.Min(1.0, (double)i / attack) * Math.Min(1.0, (double)(n - i) / release);
                samples[pos++] = (float)(Math.Sin(2 * Math.PI * freq * i / rate) * env * gain);
            }
        }
        return WavCodec.Encode16(samples);
    }

    public static byte[] SoftChime() => Tone(new[] { 880.0, 1174.66, 1567.98 }, 0.18, 0.5);
    public static byte[] ClearAlert() => Tone(new[] { 660.0, 880.0, 1320.0 }, 0.14, 0.6);
    public static byte[] BellMelody() => Tone(new[] { 523.25, 659.25, 783.99, 1046.5 }, 0.22, 0.55);
    public static byte[] TechBlip() => Tone(new[] { 440.0, 440.0, 880.0 }, 0.12, 0.45);
    public static byte[] Retro() => Tone(new[] { 587.33, 587.33, 587.33, 783.99 }, 0.16, 0.5);
}
