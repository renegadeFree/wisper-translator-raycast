using System.Diagnostics;
using System.Text;
using Whisper.net;

namespace WisperTranslator.Core.Asr;

/// <summary>Motore ASR basato su whisper.cpp tramite Whisper.net.</summary>
public sealed class WhisperAsrEngine : IAsrEngine
{
    private readonly WhisperFactory _factory;

    public WhisperAsrEngine(string modelPath, string? name = null, int? threads = null)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Modello ASR non trovato: {modelPath}", modelPath);
        }

        _factory = WhisperFactory.FromPath(modelPath);
        Name = name ?? Path.GetFileNameWithoutExtension(modelPath);
        Threads = threads ?? Math.Max(1, Environment.ProcessorCount / 2);
    }

    public string Name { get; }

    public int Threads { get; }

    public async Task<AsrResult> TranscribeAsync(
        float[] samples,
        int sampleRate = 16000,
        string? language = null,
        bool useContext = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate != 16000)
        {
            throw new ArgumentException("Il motore richiede audio a 16 kHz.", nameof(sampleRate));
        }

        var builder = _factory.CreateBuilder().WithThreads(Threads);
        if (!useContext)
        {
            // Sulle decodifiche parziali il contesto fa ripetere il modello su frammenti brevi.
            builder = builder.WithNoContext();
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            builder = builder.WithLanguage(language);
        }

        using var processor = builder.Build();
        var segments = new List<AsrSegment>();
        var text = new StringBuilder();
        var watch = Stopwatch.StartNew();

        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
        {
            text.Append(segment.Text);
            segments.Add(new AsrSegment(segment.Text.Trim(), segment.Start, segment.End - segment.Start));
        }

        watch.Stop();
        var audioDuration = TimeSpan.FromSeconds(samples.Length / (double)sampleRate);
        return new AsrResult(text.ToString().Trim(), language, audioDuration, watch.Elapsed, segments);
    }

    public void Dispose() => _factory.Dispose();
}
