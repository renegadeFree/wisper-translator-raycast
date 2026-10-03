using System.Diagnostics;
using System.Text.Json;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Vosk (Apache-2.0): modello minuscolo con riconoscimento continuo. Costa una frazione
/// di Whisper e serve il testo immediato sui PC deboli; la frase definitiva resta a Whisper.
/// </summary>
public sealed class VoskAsrEngine : IAsrEngine
{
    private readonly Vosk.Model _model;

    public VoskAsrEngine(string modelDirectory)
    {
        if (!Directory.Exists(modelDirectory))
        {
            throw new DirectoryNotFoundException($"Modello Vosk non trovato: {modelDirectory}");
        }

        _model = new Vosk.Model(modelDirectory);
        Name = $"Vosk ({Path.GetFileName(modelDirectory)})";
    }

    public string Name { get; }

    public Task<AsrResult> TranscribeAsync(
        float[] samples,
        int sampleRate = 16000,
        string? language = null,
        bool useContext = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // Pochi secondi costano decine di millisecondi: inutile pagare il costo di un
        // giro di annullamento per interrompere una decodifica che finisce subito.
        var watch = Stopwatch.StartNew();
        var pcm = ToPcm16(samples);
        string text;
        using (var recognizer = new Vosk.VoskRecognizer(_model, sampleRate))
        {
            recognizer.AcceptWaveform(pcm, pcm.Length);
            text = ReadText(recognizer.FinalResult());
        }

        watch.Stop();
        var audioDuration = TimeSpan.FromSeconds(samples.Length / (double)sampleRate);
        return Task.FromResult(new AsrResult(
            AsrTextGuard.TrimRepetitions(text),
            language,
            audioDuration,
            watch.Elapsed,
            []));
    }

    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }

    private static string ReadText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("text", out var text)
                ? text.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    public void Dispose() => _model.Dispose();
}
