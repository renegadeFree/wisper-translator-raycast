namespace WisperTranslator.Core.Asr;

public sealed record AsrSegment(string Text, TimeSpan Start, TimeSpan Duration);

public sealed record AsrResult(
    string Text,
    string? Language,
    TimeSpan AudioDuration,
    TimeSpan Elapsed,
    IReadOnlyList<AsrSegment> Segments)
{
    /// <summary>Real Time Factor: &lt; 1 significa più veloce della durata dell'audio.</summary>
    public double RealTimeFactor => AudioDuration.TotalSeconds <= 0
        ? 0
        : Elapsed.TotalSeconds / AudioDuration.TotalSeconds;
}

/// <summary>Motore di trascrizione: audio 16 kHz mono float32 → testo.</summary>
public interface IAsrEngine : IDisposable
{
    string Name { get; }

    Task<AsrResult> TranscribeAsync(
        float[] samples,
        int sampleRate = 16000,
        string? language = null,
        bool useContext = true,
        CancellationToken cancellationToken = default);
}
