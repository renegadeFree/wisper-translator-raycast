using NAudio.Wave;

namespace WisperTranslator.Core.Audio;

/// <summary>
/// Media tutti i canali in uno. NAudio offre <c>StereoToMonoSampleProvider</c>, che però
/// non regge i formati 5.1/7.1 tipici di HDMI e delle schede audio.
/// </summary>
public sealed class DownmixToMonoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _scratch = [];

    public DownmixToMonoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_channels == 1)
        {
            return _source.Read(buffer, offset, count);
        }

        var needed = count * _channels;
        if (_scratch.Length < needed)
        {
            _scratch = new float[needed];
        }

        var read = _source.Read(_scratch, 0, needed);
        var frames = read / _channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            var baseIndex = frame * _channels;
            for (var channel = 0; channel < _channels; channel++)
            {
                sum += _scratch[baseIndex + channel];
            }

            buffer[offset + frame] = sum / _channels;
        }

        return frames;
    }
}
