namespace WisperTranslator.Core.Translation;

public sealed record TranslationResult(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    TimeSpan Elapsed,
    bool FromCache);

/// <summary>Motore di traduzione: testo → testo.</summary>
public interface ITranslationEngine : IDisposable
{
    string Name { get; }

    Task<TranslationResult> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken = default);
}
