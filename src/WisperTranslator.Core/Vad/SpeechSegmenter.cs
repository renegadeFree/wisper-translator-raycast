using WisperTranslator.Core.Audio;

namespace WisperTranslator.Core.Vad;

public sealed record SpeechSegment(float[] Samples, TimeSpan Start, TimeSpan Duration, float Peak)
{
    public double Seconds => Duration.TotalSeconds;
}

/// <summary>
/// Trasforma uno stream continuo a 16 kHz in enunciati: applica il VAD, la macchina
/// a stati di endpoint e tiene un pre-roll per non tagliare l'attacco delle parole.
/// </summary>
public sealed class SpeechSegmenter : IDisposable
{
    private readonly IVadModel _vad;
    private readonly EndpointDetector _detector;
    private readonly float[] _frame;
    private readonly Queue<float[]> _preRoll = new();
    private readonly int _preRollFrames;
    private readonly int _padFrames;
    private readonly List<float> _utterance = [];
    private int _frameFill;
    private int _silenceTailFrames;
    private long _framesSeen;
    private long _utteranceStartFrame;

    public SpeechSegmenter(IVadModel vad, EndpointOptions? options = null)
    {
        _vad = vad;
        _detector = new EndpointDetector(options ?? new EndpointOptions { FrameSamples = vad.FrameSamples });
        _frame = new float[_detector.Options.FrameSamples];

        if (_detector.Options.FrameSamples != vad.FrameSamples)
        {
            throw new ArgumentException(
                $"Il detector usa frame da {_detector.Options.FrameSamples} campioni ma il VAD ne richiede {vad.FrameSamples}.");
        }

        _padFrames = (int)Math.Ceiling(_detector.Options.PadSeconds / _detector.Options.FrameSeconds);
        // Il pre-roll deve coprire tutto il parlato accumulato prima dell'apertura dell'enunciato.
        _preRollFrames = Math.Max(
            1,
            (int)Math.Ceiling(
                (_detector.Options.MinSpeechSeconds + _detector.Options.PadSeconds) / _detector.Options.FrameSeconds));
    }

    public EndpointOptions Options => _detector.Options;

    public bool InSpeech => _detector.InSpeech;

    public int CurrentUtteranceSamples => _utterance.Count;

    /// <summary>Inizio dell'enunciato in corso, in tempo audio.</summary>
    public TimeSpan CurrentUtteranceStart =>
        TimeSpan.FromSeconds(Math.Max(0, _utteranceStartFrame) * _detector.Options.FrameSeconds);

    /// <summary>Copia l'audio dell'enunciato in corso: serve alle decodifiche parziali.</summary>
    public bool TryCopyCurrentUtterance(out float[] samples)
    {
        if (!_detector.InSpeech || _utterance.Count == 0)
        {
            samples = [];
            return false;
        }

        samples = [.. _utterance];
        return true;
    }

    /// <summary>Statistiche cumulative utili al monitoraggio.</summary>
    public int SegmentsEmitted { get; private set; }

    public long SamplesProcessed => _framesSeen * _frame.Length + _frameFill;

    /// <summary>Restituisce gli enunciati completati durante questo blocco (di norma 0 o 1).</summary>
    public IReadOnlyList<SpeechSegment> Feed(ReadOnlySpan<float> samples)
    {
        List<SpeechSegment>? segments = null;
        var offset = 0;
        while (offset < samples.Length)
        {
            var copy = Math.Min(_frame.Length - _frameFill, samples.Length - offset);
            samples.Slice(offset, copy).CopyTo(_frame.AsSpan(_frameFill));
            _frameFill += copy;
            offset += copy;

            if (_frameFill < _frame.Length)
            {
                continue;
            }

            var segment = ProcessFrame();
            _frameFill = 0;
            if (segment is not null)
            {
                (segments ??= []).Add(segment);
            }
        }

        return segments ?? (IReadOnlyList<SpeechSegment>)[];
    }

    private SpeechSegment? ProcessFrame()
    {
        var probability = _vad.SpeechProbability(_frame);
        var signal = _detector.Process(probability);
        _framesSeen++;

        if (signal == EndpointSignal.SpeechStart)
        {
            _utterance.Clear();
            foreach (var preRollFrame in _preRoll)
            {
                _utterance.AddRange(preRollFrame);
            }

            _silenceTailFrames = 0;
            // Il tempo deve descrivere anche il pre-roll incluso nell'audio, altrimenti
            // sottotitoli e diarizzazione risultano spostati rispetto ai campioni.
            _utteranceStartFrame = _framesSeen - _preRoll.Count - 1;
        }

        if (_detector.InSpeech)
        {
            _utterance.AddRange(_frame);
            _silenceTailFrames = probability < _detector.Options.SilenceThreshold ? _silenceTailFrames + 1 : 0;
        }
        else
        {
            var frame = _preRoll.Count == _preRollFrames ? _preRoll.Dequeue() : new float[_frame.Length];
            _frame.CopyTo(frame, 0);
            _preRoll.Enqueue(frame);
        }

        if (signal != EndpointSignal.SpeechEnd && _utterance.Count < _detector.Options.SampleRate * 20)
        {
            return null;
        }

        var samples = BuildSegmentSamples();
        var start = TimeSpan.FromSeconds(
            Math.Max(0, _utteranceStartFrame) * _detector.Options.FrameSeconds);
        var duration = TimeSpan.FromSeconds(samples.Length / (double)_detector.Options.SampleRate);

        _utterance.Clear();
        _preRoll.Clear();
        _silenceTailFrames = 0;

        if (duration.TotalSeconds < _detector.Options.MinSpeechSeconds)
        {
            return null;
        }

        SegmentsEmitted++;
        return new SpeechSegment(samples, start, duration, Peak(samples));
    }

    private float[] BuildSegmentSamples()
    {
        var dropFrames = Math.Max(0, _silenceTailFrames - _padFrames);
        var dropSamples = Math.Min(_utterance.Count, dropFrames * _frame.Length);
        var length = _utterance.Count - dropSamples;
        if (length <= 0)
        {
            return [];
        }

        var samples = new float[length];
        _utterance.CopyTo(0, samples, 0, length);
        return samples;
    }

    private static float Peak(float[] samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        return peak;
    }

    public void Dispose() => _vad.Dispose();
}
