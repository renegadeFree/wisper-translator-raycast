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
/// <c>Turns</c> è pieno solo quando la frase finale contiene più parlanti.
/// </summary>
public sealed record TranscriptUpdate(
    int UtteranceId,
    string Text,
    bool IsFinal,
    TimeSpan Start,
    TimeSpan Duration,
    TimeSpan Latency,
    string Provisional = "",
    IReadOnlyList<TranscriptTurn>? Turns = null);

/// <summary>Battuta di un singolo parlante dentro una frase finale diarizzata.</summary>
public sealed record TranscriptTurn(int Speaker, string Text, TimeSpan Start, TimeSpan Duration);

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
    private NeMoRealtimeClient? _streaming;
    private readonly int _streamingPort;
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
    private volatile bool _restartStreaming;
    private TimeSpan _partialInterval;
    private string? _language;
    private int _streamingUtterance;
    private TimeSpan _streamingStart;
    private readonly StreamingTextAccumulator _streamingText = new();
    private long _samplesSeen;
    private bool _disposed;
    private Task _workers = Task.CompletedTask;
    private int _lastClosedUtterance;

    internal Task WorkersCompletion => _workers;

    public RealtimeTranscriber(
        IPcmSource source,
        SpeechSegmenter segmenter,
        IAsrEngine engine,
        RealtimeOptions? options = null,
        IAsrEngine? finalEngine = null,
        NeMoRealtimeClient? streaming = null)
    {
        _source = source;
        _segmenter = segmenter;
        _engine = engine;
        _finalEngine = finalEngine ?? engine;
        _options = options ?? new RealtimeOptions();
        _partialInterval = _options.PartialInterval;
        if (streaming is not null)
        {
            _streaming = streaming;
            _streamingPort = streaming.Port;
            _streaming.Update += OnStreamingUpdate;
        }
    }

    /// <summary>Testo committato (parziale stabile o frase finale).</summary>
    public event Action<TranscriptUpdate>? Update;

    /// <summary>Ipotesi non ancora confermata: utile per un'anteprima leggera in UI.</summary>
    public event Action<string>? Hypothesis;

    public event Action<SpeechSegment>? SegmentClosed;

    /// <summary>Audio e parole dell'enunciato appena concluso: servono alla diarizzazione differita.</summary>
    public event Action<int, TimeSpan, float[], AsrResult>? FinalResolved;

    /// <summary>Ogni blocco audio con il suo tempo: lo usa la diarizzazione a finestra scorrevole.</summary>
    // La memoria è valida durante il callback: chi la conserva deve copiarla.
    public event Action<ReadOnlyMemory<float>, TimeSpan>? AudioBlock;

    /// <summary>Lingua forzata per il motore ASR (null = rilevamento automatico).</summary>
    public string? Language
    {
        get => _language;
        set
        {
            if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _language = value;
            _restartStreaming = true;
        }
    }

    public int FinalSegments { get; private set; }

    /// <summary>Ultimo errore di decodifica: la UI può mostrarlo invece di restare muta.</summary>
    public string? LastError { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StartStreamingAsync(cancellationToken).ConfigureAwait(false);

        // Due corsie indipendenti: i parziali non aspettano mai la rifinitura della frase
        // precedente, che era la causa della sensazione di testo "in ritardo".
        var partialWorker = Task.Run(() => PartialWorkerAsync(cancellationToken), CancellationToken.None);
        var finalWorker = Task.Run(() => FinalWorkerAsync(cancellationToken), CancellationToken.None);
        _workers = Task.WhenAll(partialWorker, finalWorker);
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

                if (AudioBlock is not null)
                {
                    AudioBlock(block.AsMemory(0, read), TimeSpan.FromSeconds(_samplesSeen / (double)SampleRate));
                }

                _samplesSeen += read;

                var closed = _segmenter.Feed(block.AsSpan(0, read));
                var now = DateTime.UtcNow;

                if (_segmenter.InSpeech && !wasInSpeech)
                {
                    currentUtterance = ++_utteranceId;
                    Volatile.Write(ref _streamingUtterance, currentUtterance);
                    _streamingStart = _segmenter.CurrentUtteranceStart;
                    _streamingText.Reset();
                }

                if (_streaming is { IsFaulted: false })
                {
                    // Anche il blocco che chiude l'enunciato deve raggiungere il WebSocket.
                    _streaming.Push(block.AsSpan(0, read));
                }
                else if (_streaming is { IsFaulted: true } failed)
                {
                    failed.Update -= OnStreamingUpdate;
                    _streaming = null;
                    _streamingText.Reset();
                    await failed.DisposeAsync().ConfigureAwait(false);
                    _lastPartialText = null;
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
                        _lastClosedUtterance = currentUtterance;
                        _lastPartialText = null;
                        _lastProvisionalText = string.Empty;
                    }

                    wasInSpeech = false;
                    _finalSignal.Release(closed.Count);
                    continue;
                }

                wasInSpeech = _segmenter.InSpeech;

                // Con il worker occupato un nuovo parziale non farebbe che accodare lavoro vecchio.
                if (_streaming is null
                    && _segmenter.InSpeech
                    && !_partialBusy
                    && now - lastPartialAt >= _partialInterval
                    && _segmenter.CurrentUtteranceSamples >= minPartialSamples
                    && _segmenter.CurrentUtteranceSamples <= _options.PartialDecodeLimitSeconds * SampleRate
                    && _segmenter.TryCopyCurrentUtterance(out var samples))
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

                if (_restartStreaming && _streaming is not null)
                {
                    await RestartStreamingAsync(cancellationToken).ConfigureAwait(false);
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
                await _workers
                    .WaitAsync(_options.StopTimeout, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                LastError ??= "Decodifica interrotta in corso: sessione fermata comunque.";
            }
        }
    }

    private async Task StartStreamingAsync(CancellationToken cancellationToken)
    {
        if (_streaming is null)
        {
            return;
        }

        try
        {
            await _streaming.StartAsync(_language, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failed = _streaming;
            failed.Update -= OnStreamingUpdate;
            _streaming = null;
            _streamingText.Reset();
            await failed.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RestartStreamingAsync(CancellationToken cancellationToken)
    {
        _restartStreaming = false;
        if (_streaming is null)
        {
            return;
        }

        var previous = _streaming;
        previous.Update -= OnStreamingUpdate;
        _streaming = null;
        _streamingText.Reset();
        await previous.DisposeAsync().ConfigureAwait(false);

        var next = new NeMoRealtimeClient(_streamingPort);
        next.Update += OnStreamingUpdate;
        _streaming = next;
        await StartStreamingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Il WebSocket manda il testo del blocco corrente. Lo pubblichiamo come parziale:
    /// la frase definitiva batch resta l'autorità e sostituirà questa anteprima.
    /// </summary>
    private void OnStreamingUpdate(string text, bool completed)
    {
        var utterance = Volatile.Read(ref _streamingUtterance);
        if (_disposed || utterance == 0 || utterance <= Volatile.Read(ref _lastClosedUtterance)
            || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var accumulated = _streamingText.Append(text, completed);
        if (string.IsNullOrWhiteSpace(accumulated))
        {
            return;
        }

        Update?.Invoke(new TranscriptUpdate(
            utterance,
            accumulated,
            false,
            _streamingStart,
            TimeSpan.Zero,
            TimeSpan.Zero,
            Provisional: string.Empty));
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

                if ((committedChanged || provisionalChanged)
                    && partialUtterance > Volatile.Read(ref _lastClosedUtterance))
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
                    DateTime.UtcNow - closedAt,
                    Turns: BuildTurns(result)));
                SegmentClosed?.Invoke(final);
                FinalResolved?.Invoke(utterance, final.Start, final.Samples, result);
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
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // il ciclo esce al giro successivo
        }
    }

    /// <summary>
    /// Turni per parlante, se il motore ha fatto diarizzazione. Senza parlanti la lista resta
    /// vuota e la battuta viene trattata come sempre: nessun cambiamento per Whisper e Vosk.
    /// </summary>
    internal static IReadOnlyList<TranscriptTurn> BuildTurns(AsrResult result)
    {
        if (result.Segments.Count == 0 || !result.Segments.Any(segment => segment.Speaker != 0))
        {
            return [];
        }

        var turns = new List<TranscriptTurn>();
        foreach (var segment in result.Segments)
        {
            var text = segment.Text.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (turns.Count > 0 && turns[^1].Speaker == segment.Speaker)
            {
                var previous = turns[^1];
                turns[^1] = previous with
                {
                    Text = $"{previous.Text} {text}",
                    Duration = segment.Start + segment.Duration - previous.Start,
                };
                continue;
            }

            turns.Add(new TranscriptTurn(segment.Speaker, text, segment.Start, segment.Duration));
        }

        return turns;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Un decoder nativo può ignorare l'annullamento: i semafori restano validi
        // fino all'uscita effettiva dei worker, anche dopo il timeout dello stop.
        _ = _workers.ContinueWith(task =>
        {
            _ = task.Exception;
            _signal.Dispose();
            _finalSignal.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        if (_streaming is not null)
        {
            _streaming.Update -= OnStreamingUpdate;
            try
            {
                _streaming.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                // lo stop non deve restare appeso alla socket
            }

            _streaming = null;
        }
    }
}
