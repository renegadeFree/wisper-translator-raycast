namespace WisperTranslator.Core.Audio;

/// <summary>Somma le sorgenti abilitate applicando il gain e limitando a [-1, 1].</summary>
public sealed class AudioMixer : IPcmSource, IDisposable
{
    private readonly List<IAudioSource> _sources = [];
    private float[] _scratch = [];

    public IReadOnlyList<IAudioSource> Sources => _sources;

    public void Add(IAudioSource source) => _sources.Add(source);

    public bool Remove(IAudioSource source) => _sources.Remove(source);

    public int Read(Span<float> destination)
    {
        destination.Clear();
        if (_scratch.Length < destination.Length)
        {
            _scratch = new float[destination.Length];
        }

        var produced = 0;
        foreach (var source in _sources)
        {
            if (!source.Enabled || source.Gain == 0f)
            {
                continue;
            }

            var read = source.Read(_scratch.AsSpan(0, destination.Length));
            var gain = source.Gain;
            for (var i = 0; i < read; i++)
            {
                destination[i] += _scratch[i] * gain;
            }

            produced = Math.Max(produced, read);
        }

        for (var i = 0; i < produced; i++)
        {
            destination[i] = Math.Clamp(destination[i], -1f, 1f);
        }

        return produced;
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            (source as IDisposable)?.Dispose();
        }

        _sources.Clear();
    }
}
