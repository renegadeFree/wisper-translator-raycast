using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Vad;

namespace WisperTranslator.Core.Asr;

public sealed class RealtimeOptions
{
    /// <summary>Ogni quanto tentare una decodifica parziale dell'enunciato in corso.</summary>
    public TimeSpan PartialInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Con enunciati lunghi si allunga l'intervallo fra i parziali: la decodifica costa sempre di
    /// più e accodare lavoro non riduce la latenza, la aumenta soltanto.
    /// </summary>
    public TimeSpan MaxPartialInterval { get; init; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>Oltre questa durata di audio il parziale non viene più decodificato in anticipo.</summary>
    public double PartialDecodeLimitSeconds { get; init; } = 8.0;

    /// <summary>
    /// Audio minimo perché valga la pena decodificare un parziale. Sotto il secondo Whisper
    /// tende a inventare ("buon appetito!" su un frammento): meglio aspettare.
    /// </summary>
    public double MinPartialSeconds { get; init; } = 0.8;

    public int BlockSamples { get; init; } = 512;

    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(5);

    /// <summary>
    /// Quanto attendere la decodifica in corso allo stop. Oltre il limite si prosegue: la
    /// decodifica resta in volo sul motore (che non viene liberato) e il risultato viene scartato.
    /// </summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromMilliseconds(1200);
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
    TimeSpan Latency,
    string Provisional = "");

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
    private readonly SemaphoreSlim _finalSignal = new(0);
    private readonly object _gate = new();
    private readonly Queue<(SpeechSegment Segment, DateTime ClosedAt, int UtteranceId)> _finals = new();
    private float[]? _pendingPartial;
    private int _pendingPartialUtterance;
    private TimeSpan _pendingPartialStart;
    private DateTime _pendingPartialQueuedAt;
    private string? _lastPartialText;
    private string _lastProvisionalText = string.Empty;
    private int _policyUtterance;
    private int _utteranceId;
    private volatile bool _partialBusy;
    private TimeSpan _partialInterval;
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
        _partialInterval = _options.PartialInterval;
    }

    /// <summary>Testo committato (parziale stabile o frase finale).</summary>
    public event Action<TranscriptUpdate>? Update;

    /// <summary>Ipotesi non ancora confermata: utile per un'anteprima leggera in UI.</summary>
    public event Action<string>? Hypothesis;

    public event Action<SpeechSegment>? SegmentClosed;

    /// <summary>Lingua forzata per il motore ASR (null = rilevamento automatico).</summary>
    public string? Language { get; set; }

    public int FinalSegments { get; private set; }

    /// <summary>Ultimo errore di decodifica: la UI può mostrarlo invece di restare muta.</summary>
    public string? LastError { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Due corsie indipendenti: i parziali non aspettano mai la rifinitura della frase
        // precedente, che era la causa della sensazione di testo "in ritardo".
        var partialWorker = Task.Run(() => PartialWorkerAsync(cancellationToken), CancellationToken.None);
        var finalWorker = Task.Run(() => FinalWorkerAsync(cancellationToken), CancellationToken.None);
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
                        _lastProvisionalText = string.Empty;
                    }

                    wasInSpeech = false;
                    _finalSignal.Release();
                    continue;
                }

                wasInSpeech = _segmenter.InSpeech;

                // Con il worker occupato un nuovo parziale non farebbe che accodare lavoro vecchio.
                if (_segmenter.InSpeech
                    && !_partialBusy
                    && now - lastPartialAt >= _partialInterval
                    && _segmenter.TryCopyCurrentUtterance(out var samples)
                    && samples.Length >= minPartialSamples)
                {
                    lastPartialAt = now;
                    // Più l'enunciato è lungo, più costa decodificarlo: si dirada invece di accodare.
                    _partialInterval = TimeSpan.FromMilliseconds(Math.Min(
                        _options.MaxPartialInterval.TotalMilliseconds,
                        _options.PartialInterval.TotalMilliseconds * (1.0 + samples.Length / (3.0 * SampleRate))));
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
        finally
        {
            // Niente lavoro residuo e nessuna attesa illimitata: allo stop l'app deve tornare
            // subito disponibile anche se una decodifica nativa è ancora in volo.
            lock (_gate)
            {
                _finals.Clear();
                _pendingPartial = null;
            }

            try
            {
                await Task
                    .WhenAll(partialWorker, finalWorker)
                    .WaitAsync(_options.StopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                LastError ??= "Decodifica interrotta in corso: sessione fermata comunque.";
            }
        }
    }

    /// <summary>Corsia dei parziali: produce il testo provvisorio mentre si parla.</summary>
    private async Task PartialWorkerAsync(CancellationToken cancellationToken)
    {
        var policy = new LocalAgreementPolicy();

        while (!cancellationToken.IsCancellationRequested)
        {
            await WaitSignalAsync(_signal, cancellationToken).ConfigureAwait(false);

            float[]? partial;
            int partialUtterance;
            TimeSpan partialStart;
            DateTime partialQueuedAt;
            lock (_gate)
            {
                partial = _pendingPartial;
                partialUtterance = _pendingPartialUtterance;
                partialStart = _pendingPartialStart;
                partialQueuedAt = _pendingPartialQueuedAt;
                _pendingPartial = null;
            }

            if (partial is null)
            {
                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _partialBusy = true;
            try
            {
                if (partialUtterance != _policyUtterance)
                {
                    // Enunciato nuovo: la policy non deve trascinarsi il testo precedente.
                    _policyUtterance = partialUtterance;
                    policy.Reset();
                    _lastPartialText = null;
                    _lastProvisionalText = string.Empty;
                }

                var result = await _engine
                    .TranscribeAsync(partial, SampleRate, Language, false, cancellationToken)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var agreement = policy.Commit(result.Text);
                // Il testo provvisorio è l'ipotesi intera (stabile + coda ancora incerta):
                // è quello che fa comparire le parole mentre vengono pronunciate.
                var provisional = string.Join(
                    ' ',
                    new[] { agreement.CommittedText, agreement.PendingText }.Where(part => part.Length > 0));
                var committedChanged = agreement.NewlyCommitted.Length > 0
                                       && agreement.CommittedText != _lastPartialText;
                var provisionalChanged = provisional.Length > 0 && provisional != _lastProvisionalText;

                if (committedChanged || provisionalChanged)
                {
                    _lastPartialText = agreement.CommittedText;
                    _lastProvisionalText = provisional;
                    Update?.Invoke(new TranscriptUpdate(
                        partialUtterance,
                        agreement.CommittedText,
                        false,
                        partialStart,
                        TimeSpan.FromSeconds(partial.Length / (double)SampleRate),
                        DateTime.UtcNow - partialQueuedAt,
                        provisional));
                }

                if (agreement.PendingText.Length > 0)
                {
                    Hypothesis?.Invoke(agreement.PendingText);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LastError = exception.Message;
            }
            finally
            {
                _partialBusy = false;
            }
        }
    }

    /// <summary>Corsia della frase definitiva: sostituisce l'ipotesi senza fermare i parziali.</summary>
    private async Task FinalWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await WaitSignalAsync(_finalSignal, cancellationToken).ConfigureAwait(false);

            SpeechSegment? final = null;
            var closedAt = default(DateTime);
            var utterance = 0;
            lock (_gate)
            {
                if (_finals.Count > 0)
                {
                    (final, closedAt, utterance) = _finals.Dequeue();
                }
            }

            if (final is null)
            {
                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            try
            {
                var result = await _finalEngine
                    .TranscribeAsync(final.Samples, SampleRate, Language, true, cancellationToken)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // La decodifica finale è l'autorità sull'enunciato: sostituisce i parziali,
                // così le invenzioni su frammenti brevi non restano a schermo.
                FinalSegments++;
                Update?.Invoke(new TranscriptUpdate(
                    utterance,
                    result.Text.Trim(),
                    true,
                    final.Start,
                    final.Duration,
                    DateTime.UtcNow - closedAt));
                SegmentClosed?.Invoke(final);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LastError = exception.Message;
            }
        }
    }

    private static async Task WaitSignalAsync(SemaphoreSlim signal, CancellationToken cancellationToken)
    {
        try
        {
            await signal.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // il ciclo esce al giro successivo
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
        _finalSignal.Dispose();
    }
}
