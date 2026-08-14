using System.IO;

namespace AgentNotifier.Audio;

/// <summary>极简 RIFF/WAV PCM 编解码（16/8bit，单/双声道，双声道下混）</summary>
public static class WavCodec
{
    public const int SampleRate = 44100;

    public static bool IsWav(byte[] data) =>
        data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
        && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';

    public static float[] Decode(byte[] wav)
    {
        if (!IsWav(wav)) throw new InvalidDataException("不是有效的 WAV 文件");
        int pos = 12;
        short channels = 1;
        short bits = 16;
        while (pos + 8 <= wav.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var size = BitConverter.ToInt32(wav, pos + 4);
            var body = pos + 8;
            if (id == "fmt ")
            {
                var tag = BitConverter.ToInt16(wav, body);
                if (tag != 1) throw new InvalidDataException("仅支持 PCM 编码");
                channels = BitConverter.ToInt16(wav, body + 2);
                bits = BitConverter.ToInt16(wav, body + 14);
            }
            else if (id == "data")
            {
                var count = Math.Min(size, wav.Length - body);
                var samples = new float[count / (bits / 8)];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = bits == 16
                        ? BitConverter.ToInt16(wav, body + i * 2) / 32768f
                        : (wav[body + i] - 128) / 128f;
                if (channels == 2)
                {
                    var mono = new float[samples.Length / 2];
                    for (int i = 0; i < mono.Length; i++) mono[i] = (samples[i * 2] + samples[i * 2 + 1]) * 0.5f;
                    return mono;
                }
                return samples;
            }
            pos = body + size + (size % 2);
        }
        throw new InvalidDataException("缺少 data 块");
    }

    public static byte[] Encode16(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var s = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767f);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16);
        w.Write((short)1); w.Write((short)1); w.Write(SampleRate);
        w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data.Length);
        w.Write(data);
        return ms.ToArray();
    }
}
