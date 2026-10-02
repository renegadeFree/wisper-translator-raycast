using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WisperTranslator.Core.Audio;

/// <summary>Lettura di un file audio come 16 kHz mono float32, qualunque sia il formato di partenza.</summary>
public static class WavFile
{
    public static float[] ReadMono16k(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File audio non trovato: {path}", path);
        }

        using var reader = new AudioFileReader(path);
        ISampleProvider provider = reader;
        if (provider.WaveFormat.Channels != 1)
        {
            provider = new DownmixToMonoSampleProvider(provider);
        }

        if (provider.WaveFormat.SampleRate != VoiceCapture.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, VoiceCapture.SampleRate);
        }

        var samples = new List<float>(provider.WaveFormat.SampleRate * 10);
        var buffer = new float[8192];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return [.. samples];
    }

    public static void WriteMono16k(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(VoiceCapture.SampleRate, 1));
        writer.WriteSamples(samples, 0, samples.Length);
    }
}
