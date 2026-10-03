using System.Text.RegularExpressions;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Facciata usata dall'app: evita di ritradurre lo stesso testo (i parziali crescono a ogni
/// aggiornamento) e tiene statistiche utili alla UI.
/// </summary>
public sealed partial class TranslationService : IDisposable
{
    private readonly ITranslationEngine _engine;
    private readonly Dictionary<(string From, string To, string Text), string> _cache = new();
    private readonly Queue<(string From, string To, string Text)> _cacheOrder = new();
    private readonly object _cacheGate = new();
    private long _hits;
    private readonly int _cacheLimit;

    public TranslationService(ITranslationEngine engine, int cacheLimit = 2000)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentOutOfRangeException.ThrowIfNegative(cacheLimit);
        _engine = engine;
        _cacheLimit = cacheLimit;
    }

    public ITranslationEngine Engine => _engine;

    public int CacheCount { get { lock (_cacheGate) { return _cache.Count; } } }

    public long Hits => Interlocked.Read(ref _hits);

    /// <summary>
    /// I modelli locali a volte incollano le frasi ("ciao.Come stai"): si rimette lo spazio,
    /// che su un sottotitolo si nota subito.
    /// </summary>
    private static string Normalize(string text) =>
        Punctuation().Replace(text, "$1 ");

    [GeneratedRegex(@"([.!?…])(?=[\p{L}\p{N}])")]
    private static partial Regex Punctuation();

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return new TranslationResult(string.Empty, from, to, TimeSpan.Zero, false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var key = (from, to, trimmed);
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                Interlocked.Increment(ref _hits);
                return new TranslationResult(cached, from, to, TimeSpan.Zero, true);
            }
        }

        var result = await _engine.TranslateAsync(trimmed, from, to, cancellationToken).ConfigureAwait(false);
        result = result with { Text = Normalize(result.Text) };
        if (result.Text.Length > 0 && _cacheLimit > 0)
        {
            lock (_cacheGate)
            {
                if (!_cache.ContainsKey(key))
                {
                    while (_cache.Count >= _cacheLimit)
                    {
                        _cache.Remove(_cacheOrder.Dequeue());
                    }

                    _cacheOrder.Enqueue(key);
                }

                _cache[key] = result.Text;
            }
        }

        return result;
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _cacheOrder.Clear();
        }
    }

    public void Dispose() => _engine.Dispose();
}
