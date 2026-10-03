namespace WisperTranslator.Core.Asr;

/// <summary>
/// Mantiene un'identità stabile per i parlanti dell'audio di sistema. La diarizzazione per
/// singola frase azzera le etichette a ogni enunciato; qui si rianalizza una finestra
/// scorrevole e si riusano gli id globali sovrapposti nel tempo.
/// </summary>
public sealed class SpeakerTracker : IDisposable
{
    private const int SampleRate = 16000;
    // Finestra corta e passo largo: tiene l'uso CPU intorno al 20% di un core, non satura
    // la macchina e conserva le identità finché i parlanti si alternano nella conversazione.
    private const int MaxSeconds = 45;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan MinNewAudio = TimeSpan.FromSeconds(1.0);

    private readonly NeMoDiarizationClient _client;
    private readonly object _gate = new();
    private readonly Audio.FloatRingBuffer _buffer = new(MaxSeconds * SampleRate);
    private List<GlobalSegment> _segments = [];
    private long _totalSamples;
    private double _bufferStartSeconds;
    private DateTime _lastRun = DateTime.MinValue;
    private long _samplesAtLastRun;
    private bool _busy;
    private int _nextSpeaker = 1;
    private bool _disposed;

    public SpeakerTracker(NeMoDiarizationClient client) => _client = client;

    /// <summary>
    /// Forza l'elaborazione immediata della finestra se c'è audio sufficiente e non stiamo già calcolando.
    /// Viene invocato alla chiusura di un enunciato per ridurre a zero i tempi di attesa dell'etichetta parlante.
    /// </summary>
    public void TriggerNow()
    {
        var shouldRun = false;
        lock (_gate)
        {
            if (!_disposed && !_busy && _buffer.Count >= (long)(SampleRate * 1.5) && _totalSamples - _samplesAtLastRun >= (long)(0.5 * SampleRate))
            {
                _busy = true;
                shouldRun = true;
            }
        }

        if (shouldRun)
        {
            _ = Task.Run(RunAsync);
        }
    }

    public void Append(ReadOnlySpan<float> samples)
    {
        if (_disposed || samples.Length == 0)
        {
            return;
        }

        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var shouldRun = false;
        lock (_gate)
        {
            _buffer.Write(samples);
            _totalSamples += samples.Length;
            _bufferStartSeconds = (_totalSamples - _buffer.Count) / (double)SampleRate;

            if (!_busy
                && peak >= 0.01f
                && _buffer.Count >= SampleRate * 2
                && DateTime.UtcNow - _lastRun >= Interval
                && _totalSamples - _samplesAtLastRun >= (long)(MinNewAudio.TotalSeconds * SampleRate))
            {
                _busy = true;
                shouldRun = true;
            }
        }

        if (shouldRun)
        {
            _ = Task.Run(RunAsync);
        }
    }

    /// <summary>
    /// Attribuisce il parlante alle parole dell'enunciato. Restituisce false se la finestra
    /// diarizzata non copre ancora la fine dell'enunciato: il chiamante riproverà.
    /// </summary>
    public bool TryApply(
        IReadOnlyList<AsrSegment> words,
        TimeSpan utteranceStart,
        out IReadOnlyList<AsrSegment> tagged)
    {
        lock (_gate)
        {
            var end = utteranceStart + words
                .Select(word => word.Start + word.Duration)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Max();
            var covered = _segments.Count > 0
                && _segments.Max(segment => segment.End) >= end.TotalSeconds - 0.5;
            if (!covered)
            {
                tagged = words;
                return false;
            }

            var result = new List<AsrSegment>(words.Count);
            foreach (var word in words)
            {
                var center = (utteranceStart + word.Start + (word.Duration / 2)).TotalSeconds;
                var speaker = 0;
                var nearest = double.MaxValue;
                foreach (var segment in _segments)
                {
                    if (center >= segment.Start - 0.05 && center <= segment.End + 0.05)
                    {
                        speaker = segment.Speaker;
                        break;
                    }

                    var distance = Distance(center, segment);
                    if (distance < nearest)
                    {
                        nearest = distance;
                        speaker = segment.Speaker;
                    }
                }
                result.Add(word with { Speaker = speaker });
            }

            tagged = result;
            return true;
        }
    }

    private async Task RunAsync()
    {
        float[] audio;
        double start;
        long samples;
        lock (_gate)
        {
            audio = _buffer.Snapshot();
            start = _bufferStartSeconds;
            samples = _totalSamples;
        }

        try
        {
            var segments = await _client.DiarizeAsync(audio, SampleRate).ConfigureAwait(false);
            lock (_gate)
            {
                if (segments.Count > 0)
                {
                    _segments = Map(segments, start);
                }

            }
        }
        catch (Exception)
        {
            // la prossima finestra ritenta: il testo resta comunque disponibile
        }
        finally
        {
            lock (_gate)
            {
                // Anche un errore deve rispettare l'intervallo: niente retry a ogni frame.
                _lastRun = DateTime.UtcNow;
                _samplesAtLastRun = samples;
                _busy = false;
            }
        }
    }

    private static double Distance(double point, GlobalSegment segment) =>
        point < segment.Start ? segment.Start - point
        : point > segment.End ? point - segment.End
        : 0;

    private List<GlobalSegment> Map(IReadOnlyList<DiarizationSegment> raw, double bufferStart)
    {
        var map = new Dictionary<int, int>();
        var assigned = new HashSet<int>();
        foreach (var speaker in raw.Select(segment => segment.Speaker).Distinct())
        {
            var overlaps = new Dictionary<int, double>();
            foreach (var segment in raw.Where(segment => segment.Speaker == speaker))
            {
                var absoluteStart = bufferStart + segment.Start;
                var absoluteEnd = bufferStart + segment.End;
                foreach (var previous in _segments)
                {
                    var overlap = Math.Min(absoluteEnd, previous.End) - Math.Max(absoluteStart, previous.Start);
                    if (overlap <= 0)
                    {
                        continue;
                    }

                    overlaps[previous.Speaker] = overlaps.GetValueOrDefault(previous.Speaker) + overlap;
                }
            }

            var match = overlaps.Where(pair => !assigned.Contains(pair.Key))
                .OrderByDescending(pair => pair.Value).FirstOrDefault();
            map[speaker] = match.Value > 0 ? match.Key : _nextSpeaker++;
            assigned.Add(map[speaker]);
        }

        return
        [
            .. raw.Select(segment => new GlobalSegment(
                bufferStart + segment.Start,
                bufferStart + segment.End,
                map[segment.Speaker])),
        ];
    }

    public void Dispose()
    {
        _disposed = true;
        _client.Dispose();
    }

    private sealed record GlobalSegment(double Start, double End, int Speaker);
}
