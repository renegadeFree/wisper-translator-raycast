using WisperTranslator.Core.Session;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Le due corsie di traduzione di una singola battuta.
///
/// La corsia rapida traduce il parziale che cresce e non viene mai annullata: se nel frattempo
/// arriva testo nuovo, la traduzione in corso finisce, viene pubblicata e poi parte subito la
/// successiva. Prima ogni parziale uccideva la traduzione precedente, quindi finché si parlava
/// non usciva niente: era questo il ritardo, non la CPU.
///
/// La corsia di qualità lavora in parallelo, a metà enunciato e alla fine, con un modello più
/// grande, e sostituisce la riga quando ha finito. Non blocca mai la corsia rapida.
/// </summary>
public sealed class CueTranslationLane : IDisposable
{
    /// <summary>Quiete fra due traduzioni rapide: una raffica di delta non fa partire un treno di richieste.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(120);

    /// <summary>Parole nuove necessarie per innescare un passaggio di qualità a metà frase.</summary>
    public const int DefaultQualityWordStep = 8;

    /// <summary>Distanza minima fra due passaggi di qualità: non si accavallano mai.</summary>
    public static readonly TimeSpan DefaultQualityInterval = TimeSpan.FromMilliseconds(1200);

    private readonly int _cueId;
    private readonly TranslationService _fast;
    private readonly TranslationService? _quality;
    private readonly TranslationPipelineMode _mode;
    private readonly SemaphoreSlim _fastGate;
    private readonly SemaphoreSlim _qualityGate;
    private readonly Func<(string From, string To)> _languages;
    private readonly Action<int, string, bool> _publish;
    private readonly Action<string>? _report;
    private readonly TimeSpan _debounce;
    private readonly int _qualityWordStep;
    private readonly TimeSpan _qualityInterval;
    private readonly CancellationTokenSource _stop;
    private readonly SemaphoreSlim _fastSignal = new(0, 1);
    private readonly SemaphoreSlim _qualitySignal = new(0, 1);
    private readonly object _gate = new();
    private readonly Task _fastWorker;
    private readonly Task _qualityWorker;

    private string _pendingText = string.Empty;
    private int _generation;
    private int _finalGeneration;
    private int _fastPublished;
    private int _qualityPublished;
    private int _qualityWords;
    private long _lastQualityTicks;
    private bool _started;
    private bool _disposed;

    public CueTranslationLane(
        int cueId,
        TranslationService fast,
        TranslationService? quality,
        SemaphoreSlim fastGate,
        SemaphoreSlim qualityGate,
        Func<(string From, string To)> languages,
        Action<int, string, bool> publish,
        Action<string>? report = null,
        CancellationToken sessionToken = default,
        TimeSpan? debounce = null,
        int qualityWordStep = DefaultQualityWordStep,
        TimeSpan? qualityInterval = null,
        TranslationPipelineMode mode = TranslationPipelineMode.DualPass)
    {
        ArgumentNullException.ThrowIfNull(fast);
        ArgumentNullException.ThrowIfNull(fastGate);
        ArgumentNullException.ThrowIfNull(qualityGate);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(publish);

        _cueId = cueId;
        _fast = fast;
        _quality = quality;
        _mode = mode;
        _fastGate = fastGate;
        _qualityGate = qualityGate;
        _languages = languages;
        _publish = publish;
        _report = report;
        _debounce = debounce ?? DefaultDebounce;
        _qualityWordStep = Math.Max(1, qualityWordStep);
        _qualityInterval = qualityInterval ?? DefaultQualityInterval;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        _fastWorker = mode == TranslationPipelineMode.QualityOnly ? Task.CompletedTask : Task.Run(FastLoopAsync);
        _qualityWorker = quality is null || mode == TranslationPipelineMode.FastOnly ? Task.CompletedTask : Task.Run(QualityLoopAsync);
    }

    /// <summary>La battuta ha finito entrambi i passaggi: la corsia può essere archiviata.</summary>
    public event Action<CueTranslationLane>? Finished;

    public int CueId => _cueId;

    public bool HasQualityEngine => _quality is not null;

    public Task Completion => Task.WhenAll(_fastWorker, _qualityWorker);

    /// <summary>Nuovo testo per questa battuta: parziale che cresce oppure frase definitiva.</summary>
    public void Update(string text, bool isFinal)
    {
        if (text.Length < 2)
        {
            return;
        }

        bool wantsQuality;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingText = text;
            _generation++;
            if (!_started)
            {
                // Da qui parte il cronometro dei passaggi di qualità: la prima rifinitura arriva
                // quando la frase è cresciuta, non appena si comincia a parlare.
                _started = true;
                _lastQualityTicks = Environment.TickCount64;
                _qualityWords = 0;
            }

            if (isFinal)
            {
                _finalGeneration = _generation;
            }

            wantsQuality = isFinal || (_mode == TranslationPipelineMode.DualPass && QualityDueLocked(text, Environment.TickCount64));
            if (wantsQuality)
            {
                _qualityWords = CountWords(text);
                _lastQualityTicks = Environment.TickCount64;
            }
        }

        if (_mode != TranslationPipelineMode.QualityOnly)
        {
            Signal(_fastSignal);
        }

        if (wantsQuality && _mode != TranslationPipelineMode.FastOnly)
        {
            Signal(_qualitySignal);
        }
    }

    /// <summary>Arresto pulito: le traduzioni in corso si chiudono, la corsia si ferma.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // già fermata
        }

        Signal(_fastSignal);
        Signal(_qualitySignal);
    }

    private async Task FastLoopAsync()
    {
        var token = _stop.Token;
        var first = true;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _fastSignal.WaitAsync(token).ConfigureAwait(false);

                string text;
                lock (_gate)
                {
                    if (_disposed) return;
                    text = _pendingText;
                }

                if (text.Length < 2) continue;

                // Il primo pezzo di frase non aspetta: deve comparire subito. Dopo, se il testo
                // cambia ancora durante l'attesa, si riparte dal più recente invece di tradurre
                // una versione già superata.
                if (!first)
                {
                    await Task.Delay(_debounce, token).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (_disposed) return;
                        if (_pendingText != text) continue;
                    }
                }

                first = false;
                var (from, to) = _languages();
                await _fastGate.WaitAsync(token).ConfigureAwait(false);
                TranslationResult result;
                try
                {
                    result = await _fast.TranslateAsync(text, from, to, token).ConfigureAwait(false);
                }
                finally
                {
                    _fastGate.Release();
                }

                if (result.Text.Length > 0)
                {
                    Publish(text, result.Text, quality: false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // sessione fermata
        }
        catch (Exception exception)
        {
            _report?.Invoke($"Traduzione non riuscita: {exception.Message}");
        }
    }

    private async Task QualityLoopAsync()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _qualitySignal.WaitAsync(token).ConfigureAwait(false);

                string text;
                lock (_gate)
                {
                    if (_disposed) return;
                    text = _pendingText;
                }

                if (text.Length < 2 || _quality is null) continue;

                var (from, to) = _languages();
                await _qualityGate.WaitAsync(token).ConfigureAwait(false);
                TranslationResult result;
                try
                {
                    result = await _quality.TranslateAsync(text, from, to, token).ConfigureAwait(false);
                }
                finally
                {
                    _qualityGate.Release();
                }

                if (result.Text.Length > 0)
                {
                    Publish(text, result.Text, quality: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // sessione fermata
        }
        catch (Exception exception)
        {
            _report?.Invoke($"Rifinitura di qualità non riuscita: {exception.Message}");
        }
    }

    /// <summary>
    /// Pubblica solo se il testo non è più vecchio di quello già mostrato: la frase definitiva
    /// non può essere sovrascritta da un passaggio di qualità partito prima.
    /// </summary>
    private void Publish(string source, string translation, bool quality)
    {
        var generation = 0;
        var finished = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            generation = _generation;
            if (quality)
            {
                // La rifinitura serve solo se nel frattempo il parlato non è corso molto più
                // avanti: altrimenti mostrerebbe una frase più vecchia di quella già a schermo.
                if (CountWords(_pendingText) - CountWords(source) > 2) return;
                _qualityPublished = generation;
            }
            else
            {
                // Se la rifinitura ha già coperto questo testo, la corsia rapida non lo riscrive
                // con la versione approssimativa: vince sempre la traduzione migliore.
                if (_qualityPublished >= generation) return;
                _fastPublished = generation;
            }

            finished = _finalGeneration > 0
                       && (_mode == TranslationPipelineMode.QualityOnly || _fastPublished >= _finalGeneration)
                       && (_quality is null || _mode == TranslationPipelineMode.FastOnly || _qualityPublished >= _finalGeneration);
        }

        _publish(_cueId, translation, quality);

        if (finished)
        {
            lock (_gate)
            {
                _disposed = true;
            }

            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // già fermata
            }

            Finished?.Invoke(this);
        }
    }

    /// <summary>Metà frase: abbastanza parole nuove e abbastanza distanza dall'ultimo passaggio.</summary>
    private bool QualityDueLocked(string text, long nowTicks)
    {
        if (_quality is null)
        {
            return false;
        }

        return CountWords(text) - _qualityWords >= _qualityWordStep
               && nowTicks - _lastQualityTicks >= _qualityInterval.TotalMilliseconds;
    }

    /// <summary>Il conteggio serve solo a decidere quando rifinire: spazi e punteggiatura bastano.</summary>
    internal static int CountWords(string text)
    {
        var words = 0;
        var inside = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsPunctuation(character))
            {
                inside = false;
            }
            else if (!inside)
            {
                inside = true;
                words++;
            }
        }

        return words;
    }

    private static void Signal(SemaphoreSlim signal)
    {
        try
        {
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // corsia già archiviata
        }
        catch (SemaphoreFullException)
        {
            // un segnale è già in coda: basta quello
        }
    }
}
