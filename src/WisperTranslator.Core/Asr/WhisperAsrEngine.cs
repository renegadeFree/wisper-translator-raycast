using System.Diagnostics;
using System.Text;
using Whisper.net;

namespace WisperTranslator.Core.Asr;

/// <summary>Motore ASR basato su whisper.cpp tramite Whisper.net.</summary>
public sealed class WhisperAsrEngine : IAsrEngine
{
    private readonly WhisperFactory _factory;
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private bool _disposed;
    private int _disposeStarted;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate != 16000)
        {
            throw new ArgumentException("Il motore richiede audio a 16 kHz.", nameof(sampleRate));
        }

        // Una sola decodifica per motore: due context whisper.cpp in parallelo sullo stesso
        // modello non portano vantaggi e rendono imprevedibile la liberazione della memoria.
        await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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
            // Su audio lungo il modello può entrare in ciclo: meglio una frase tagliata che una
            // pagina di ripetizioni (che verrebbe anche tradotta e mostrata).
            var cleanText = AsrTextGuard.TrimRepetitions(text.ToString());
            return new AsrResult(cleanText, language, audioDuration, watch.Elapsed, segments);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    /// <summary>
    /// Libera il modello nativo dopo la decodifica in corso, senza bloccare la UI: liberare il factory mentre
    /// whisper.cpp sta lavorando provocava un access violation in ggml-cpu-whisper.dll.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _disposed = true;
        // La memoria nativa resta viva finché l'ultima decodifica è uscita davvero.
        // La pulizia differita evita sia un access violation sia un'attesa sulla UI.
        if (!_decodeGate.Wait(0))
        {
            _ = DisposeFactoryAsync();
            return;
        }

        try
        {
            _factory.Dispose();
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private async Task DisposeFactoryAsync()
    {
        await _decodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _factory.Dispose();
        }
        finally
        {
            _decodeGate.Release();
        }
    }
}
