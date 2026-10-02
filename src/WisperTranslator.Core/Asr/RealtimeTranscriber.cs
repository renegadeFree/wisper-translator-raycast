using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Vad;

namespace WisperTranslator.Core.Asr;

public sealed class RealtimeOptions
{
    /// <summary>Ogni quanto tentare una decodifica parziale dell'enunciato in corso.</summary>
    public TimeSpan PartialInterval { get; init; } = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Audio minimo perché valga la pena decodificare un parziale. Sotto il secondo Whisper
    /// tende a inventare ("buon appetito!" su un frammento): meglio aspettare.
    /// </summary>
    public double MinPartialSeconds { get; init; } = 1.0;

    public int BlockSamples { get; init; } = 512;

    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(5);
}

/// <summary>
/// Testo pronto per la UI. <c>Text</c> è sempre il testo completo dell'enunciato in corso,
/// non una differenza: <c>IsFinal</c> dice se è la versione definitiva (da sostituire in blocco).
/// </summary>
public sealed record TranscriptUpdate(
    int UtteranceId,
    string Text,
    bool IsFinal,
    TimeSpan Start,
    TimeSpan Duration,
    TimeSpan Latency);

/// <summary>
/// Lega sorgente audio, VAD e motore ASR in un flusso continuo: decodifiche parziali durante
/// il parlato (con LocalAgreement) e decodifica finale alla chiusura dell'enunciato.
/// </summary>
public sealed class RealtimeTranscriber : IDisposable
{
    private const int SampleRate = 16000;

    private readonly IPcmSource _source;
    private readonly SpeechSegmenter _segmenter;
    private readonly IAsrEngine _engine;
    private readonly IAsrEngine _finalEngine;
    private readonly RealtimeOptions _options;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();
    private readonly Queue<(SpeechSegment Segment, DateTime ClosedAt, int UtteranceId)> _finals = new();
    private float[]? _pendingPartial;
    private int _pendingPartialUtterance;
    private TimeSpan _pendingPartialStart;
    private DateTime _pendingPartialQueuedAt;
    private string? _lastPartialText;
    private int _utteranceId;
    private bool _disposed;

    public RealtimeTranscriber(
        IPcmSource source,
        SpeechSegmenter segmenter,
        IAsrEngine engine,
        RealtimeOptions? options = null,
        IAsrEngine? finalEngine = null)
    {
        _source = source;
        _segmenter = segmenter;
        _engine = engine;
        _finalEngine = finalEngine ?? engine;
        _options = options ?? new RealtimeOptions();
    }

    /// <summary>Testo committato (parziale stabile o frase finale).</summary>
    public event Action<TranscriptUpdate>? Update;

    /// <summary>Ipotesi non ancora confermata: utile per un'anteprima leggera in UI.</summary>
    public event Action<string>? Hypothesis;

    public event Action<SpeechSegment>? SegmentClosed;

    /// <summary>Lingua forzata per il motore ASR (null = rilevamento automatico).</summary>
    public string? Language { get; set; }

    public int FinalSegments { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var worker = Task.Run(() => WorkerAsync(cancellationToken), cancellationToken);
        var block = new float[_options.BlockSamples];
        var lastPartialAt = DateTime.MinValue;
        var minPartialSamples = (int)(_options.MinPartialSeconds * SampleRate);
        var wasInSpeech = false;
        var currentUtterance = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = _source.Read(block);
                if (read <= 0)
                {
                    await Task.Delay(_options.IdleDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var closed = _segmenter.Feed(block.AsSpan(0, read));
                var now = DateTime.UtcNow;

                if (_segmenter.InSpeech && !wasInSpeech)
                {
                    currentUtterance = ++_utteranceId;
                }

                if (closed.Count > 0)
                {
                    lock (_gate)
                    {
                        foreach (var segment in closed)
                        {
                            _finals.Enqueue((segment, now, currentUtterance));
                        }

                        _pendingPartial = null;
                        _lastPartialText = null;
                    }

                    wasInSpeech = false;
                    _signal.Release();
                    continue;
                }

                wasInSpeech = _segmenter.InSpeech;

                if (_segmenter.InSpeech
                    && now - lastPartialAt >= _options.PartialInterval
                    && _segmenter.TryCopyCurrentUtterance(out var samples)
                    && samples.Length >= minPartialSamples)
                {
                    lastPartialAt = now;
                    lock (_gate)
                    {
                        _pendingPartial = samples;
                        _pendingPartialUtterance = currentUtterance;
                        _pendingPartialStart = _segmenter.CurrentUtteranceStart;
                        _pendingPartialQueuedAt = now;
                    }

                    _signal.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // chiusura richiesta: il worker drena la coda e termina
        }

        await worker.ConfigureAwait(false);
    }

    private async Task WorkerAsync(CancellationToken cancellationToken)
    {
        var policy = new LocalAgreementPolicy();

        while (!cancellationToken.IsCancellationRequested || HasPendingWork())
        {
            try
            {
                await _signal.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // si prosegue finché la coda non è vuota
            }

            SpeechSegment? final = null;
            var finalClosedAt = default(DateTime);
            var finalUtterance = 0;
            float[]? partial = null;
            var partialUtterance = 0;
            var partialStart = TimeSpan.Zero;
            var partialQueuedAt = default(DateTime);

            lock (_gate)
            {
                if (_finals.Count > 0)
                {
                    (final, finalClosedAt, finalUtterance) = _finals.Dequeue();
                }
                else if (_pendingPartial is not null)
                {
                    partial = _pendingPartial;
                    partialUtterance = _pendingPartialUtterance;
                    partialStart = _pendingPartialStart;
                    partialQueuedAt = _pendingPartialQueuedAt;
                    _pendingPartial = null;
                }
            }

            if (final is not null)
            {
                var result = await _finalEngine
                    .TranscribeAsync(final.Samples, SampleRate, Language, true, cancellationToken)
                    .ConfigureAwait(false);

                // La decodifica finale è l'autorità sull'enunciato: sostituisce i parziali,
                // così le invenzioni su frammenti brevi non restano a schermo.
                var text = result.Text.Trim();
                policy.Reset();
                FinalSegments++;
                Update?.Invoke(new TranscriptUpdate(
                    finalUtterance,
                    text,
                    true,
                    final.Start,
                    final.Duration,
                    DateTime.UtcNow - finalClosedAt));
                SegmentClosed?.Invoke(final);
                continue;
            }

            if (partial is not null)
            {
                var result = await _engine
                    .TranscribeAsync(partial, SampleRate, Language, false, cancellationToken)
                    .ConfigureAwait(false);
                var agreement = policy.Commit(result.Text);
                if (agreement.NewlyCommitted.Length > 0 && agreement.CommittedText != _lastPartialText)
                {
                    _lastPartialText = agreement.CommittedText;
                    Update?.Invoke(new TranscriptUpdate(
                        partialUtterance,
                        agreement.CommittedText,
                        false,
                        partialStart,
                        TimeSpan.FromSeconds(partial.Length / (double)SampleRate),
                        DateTime.UtcNow - partialQueuedAt));
                }

                if (agreement.PendingText.Length > 0)
                {
                    Hypothesis?.Invoke(agreement.PendingText);
                }

                continue;
            }

            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private bool HasPendingWork()
    {
        lock (_gate)
        {
            return _finals.Count > 0 || _pendingPartial is not null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _signal.Dispose();
    }
}
