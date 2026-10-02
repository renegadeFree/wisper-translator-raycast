using NAudio.Wave;

namespace WisperTranslator.Cli;

/// <summary>Riproduce una clip o il segnale sintetico sul dispositivo predefinito.</summary>
internal sealed class Playback : IDisposable
{
    private readonly WaveOutEvent _device;
    private readonly IDisposable? _source;

    private Playback(WaveOutEvent device, IDisposable? source)
    {
        _device = device;
        _source = source;
    }

    public static Playback Start(string? clipPath, bool synthetic)
    {
        if (!string.IsNullOrWhiteSpace(clipPath))
        {
            var reader = new AudioFileReader(clipPath);
            var device = new WaveOutEvent();
            device.Init(reader);
            device.Play();
            return new Playback(device, reader);
        }

        if (!synthetic)
        {
            throw new InvalidOperationException("Indica --play-clip <file.wav> oppure --play.");
        }

        var syntheticDevice = new WaveOutEvent();
        syntheticDevice.Init(new SyntheticVoiceProvider().ToWaveProvider());
        syntheticDevice.Play();
        return new Playback(syntheticDevice, null);
    }

    public void Dispose()
    {
        _device.Stop();
        _device.Dispose();
        _source?.Dispose();
    }
}
