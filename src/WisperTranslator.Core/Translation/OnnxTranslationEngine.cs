using System.Diagnostics;
using WisperTranslator.Core.Models;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Secondo stadio di traduzione: modelli Marian (opus-mt) in ONNX, caricati al primo uso e
/// tenuti in memoria. Serve a rifinire la frase quando il motore rapido ha già mostrato il
/// senso di quello che si sta dicendo, quindi può prendersi i millisecondi che gli servono.
/// </summary>
public sealed class OnnxTranslationEngine : ITranslationEngine
{
    private readonly string _root;
    private readonly int _threads;
    private readonly Dictionary<string, Lazy<MarianOnnxModel>> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    public OnnxTranslationEngine(string? directory = null, int? threads = null)
    {
        _root = directory ?? Path.Combine(AppPaths.ModelsDirectory, "mt", ModelCatalog.OnnxDirectory);
        _threads = threads ?? Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
    }

    public string Name => "onnx-marian";

    /// <summary>Cartella in cui vivono i modelli ONNX delle due direzioni.</summary>
    public string Root => _root;

    /// <summary>True quando la coppia richiesta è installata: la UI lo usa per l'avviso.</summary>
    public static bool IsPairInstalled(string from, string to) =>
        EntryFor(from, to) is { } entry && ModelStore.IsInstalled(entry);

    public Task<TranslationResult> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new TranslationResult(string.Empty, from, to, TimeSpan.Zero, false));
        }

        var model = ModelFor(from, to);
        var watch = Stopwatch.StartNew();
        var translated = model.Translate(text, cancellationToken);
        watch.Stop();
        return Task.FromResult(new TranslationResult(translated, from, to, watch.Elapsed, false));
    }

    /// <summary>Il modello si carica alla prima traduzione: l'avvio della sessione non si allunga.</summary>
    private MarianOnnxModel ModelFor(string from, string to)
    {
        var entry = EntryFor(from, to)
                    ?? throw new NotSupportedException($"Rifinitura di qualità non disponibile per {from} → {to}.");
        var directory = ModelStore.PathFor(entry);
        Lazy<MarianOnnxModel> lazy;
        lock (_gate)
        {
            if (!_models.TryGetValue(entry.Id, out var existing))
            {
                existing = new Lazy<MarianOnnxModel>(() => MarianOnnxModel.Load(directory, _threads));
                _models[entry.Id] = existing;
            }

            lazy = existing;
        }

        return lazy.Value;
    }

    private static ModelCatalogEntry? EntryFor(string from, string to) => (from, to) switch
    {
        ("it", "en") => ModelCatalog.OpusMtItalianEnglish,
        ("en", "it") => ModelCatalog.OpusMtEnglishItalian,
        _ => null,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<Lazy<MarianOnnxModel>> models;
        lock (_gate)
        {
            models = [.. _models.Values];
            _models.Clear();
        }

        foreach (var model in models.Where(model => model.IsValueCreated))
        {
            model.Value.Dispose();
        }
    }
}
