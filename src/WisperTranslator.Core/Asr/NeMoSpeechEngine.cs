using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Corsia NeMo: manda la finestra audio al server locale e legge il testo.
/// Il modello Nemotron 3.5 streaming costa una frazione di Whisper e conosce l'italiano.
/// </summary>
public sealed class NeMoSpeechEngine : IAsrEngine
{
    private readonly NeMoSpeechServer _server;
    private readonly bool _ownsServer;

    public NeMoSpeechEngine(NeMoSpeechServer server, string? name = null, bool ownsServer = true)
    {
        _server = server;
        _ownsServer = ownsServer;
        Name = name ?? "NeMo Nemotron 3.5";
    }

    public string Name { get; }

    public async Task<AsrResult> TranscribeAsync(
        float[] samples,
        int sampleRate = 16000,
        string? language = null,
        bool useContext = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var watch = Stopwatch.StartNew();

        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(ToWav(samples, sampleRate));
        audio.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "chunk.wav");
        content.Add(new StringContent(NeMoModels.StreamingId), "model");
        if (!string.IsNullOrWhiteSpace(language))
        {
            content.Add(new StringContent(language), "language");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var response = await client
            .PostAsync($"http://127.0.0.1:{_server.Port}/v1/audio/transcriptions", content, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<TranscriptionResponse>(cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var audioDuration = TimeSpan.FromSeconds(samples.Length / (double)sampleRate);
        return new AsrResult(
            AsrTextGuard.TrimRepetitions(payload?.Text?.Trim() ?? string.Empty),
            language,
            audioDuration,
            watch.Elapsed,
            []);
    }

    /// <summary>WAV PCM16 mono in memoria: è il formato che il server accetta senza conversioni.</summary>
    private static byte[] ToWav(float[] samples, int sampleRate)
    {
        var dataLength = samples.Length * 2;
        var buffer = new byte[44 + dataLength];
        var writer = new BinaryWriter(new MemoryStream(buffer), Encoding.ASCII);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            writer.Write(value);
        }

        return buffer;
    }

    public void Dispose()
    {
        if (_ownsServer)
        {
            _server.Dispose();
        }
    }

    private sealed record TranscriptionResponse(string? Text);
}
