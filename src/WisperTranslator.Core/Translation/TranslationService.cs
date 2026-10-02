using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Facciata usata dall'app: evita di ritradurre lo stesso testo (i parziali crescono a ogni
/// aggiornamento) e tiene statistiche utili alla UI.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private readonly ITranslationEngine _engine;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly int _cacheLimit;

    public TranslationService(ITranslationEngine engine, int cacheLimit = 2000)
    {
        _engine = engine;
        _cacheLimit = cacheLimit;
    }

    public ITranslationEngine Engine => _engine;

    public int CacheCount => _cache.Count;

    public long Hits { get; private set; }

    /// <summary>
    /// I modelli locali a volte incollano le frasi ("ciao.Come stai"): si rimette lo spazio,
    /// che su un sottotitolo si nota subito.
    /// </summary>
    private static string Normalize(string text) =>
        Regex.Replace(text, @"([.!?…])(?=[\p{L}\p{N}])", "$1 ");

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

        var key = $"{from}|{to}|{trimmed}";
        if (_cache.TryGetValue(key, out var cached))
        {
            Hits++;
            return new TranslationResult(cached, from, to, TimeSpan.Zero, true);
        }

        var result = await _engine.TranslateAsync(trimmed, from, to, cancellationToken).ConfigureAwait(false);
        result = result with { Text = Normalize(result.Text) };
        if (result.Text.Length > 0)
        {
            if (_cache.Count >= _cacheLimit)
            {
                _cache.Clear();
            }

            _cache[key] = result.Text;
        }

        return result;
    }

    public void ClearCache() => _cache.Clear();

    public void Dispose() => _engine.Dispose();
}
