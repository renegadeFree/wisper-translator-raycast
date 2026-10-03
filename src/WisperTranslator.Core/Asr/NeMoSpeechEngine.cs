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
    private readonly bool _diarize;

    public NeMoSpeechEngine(
        NeMoSpeechServer server,
        string? name = null,
        bool ownsServer = true,
        bool diarize = false)
    {
        _server = server;
        _ownsServer = ownsServer;
        _diarize = diarize;
        Name = name ?? (diarize ? "NeMo Nemotron 3.5 + parlanti" : "NeMo Nemotron 3.5");
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

        if (_diarize)
        {
            // Le parole con i tempi servono ad allineare il diarizzatore in un secondo momento.
            // La diarizzazione non si chiede qui: aspettarla rallenterebbe il testo definitivo.
            content.Add(new StringContent("verbose_json"), "response_format");
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
            _diarize ? Words(payload?.Words) : GroupBySpeaker(payload?.Words));
    }

    /// <summary>
    /// Parole singole con i tempi: sono la base per attribuire in seguito il parlante senza
    /// rifare la trascrizione.
    /// </summary>
    internal static IReadOnlyList<AsrSegment> Words(List<TranscriptionWord>? words)
    {
        if (words is null || words.Count == 0)
        {
            return [];
        }

        var segments = new List<AsrSegment>();
        foreach (var word in words)
        {
            var text = word.Word?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            segments.Add(new AsrSegment(
                text,
                TimeSpan.FromSeconds(Math.Max(0, word.Start)),
                TimeSpan.FromSeconds(Math.Max(0, word.End - word.Start)),
                0));
        }

        return segments;
    }

    /// <summary>
    /// Parole consecutive dello stesso parlante diventano un turno: la sessione può così
    /// spezzare un enunciato in cui si sono sentite due voci diverse.
    /// </summary>
    internal static IReadOnlyList<AsrSegment> GroupBySpeaker(List<TranscriptionWord>? words)
    {
        if (words is null || words.Count == 0 || words.All(word => word.Speaker is null or 0))
        {
            return [];
        }

        var segments = new List<AsrSegment>();
        foreach (var word in words)
        {
            var speaker = word.Speaker ?? 0;
            var start = TimeSpan.FromSeconds(Math.Max(0, word.Start));
            var end = TimeSpan.FromSeconds(Math.Max(word.End, word.Start));
            var text = word.Word?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            if (segments.Count > 0 && segments[^1].Speaker == speaker)
            {
                var previous = segments[^1];
                segments[^1] = previous with
                {
                    Text = $"{previous.Text} {text}",
                    Duration = end - previous.Start,
                };
                continue;
            }

            segments.Add(new AsrSegment(text, start, end - start, speaker));
        }

        return segments;
    }

    /// <summary>WAV PCM16 mono in memoria: è il formato che il server accetta senza conversioni.</summary>
    internal static byte[] ToWav(float[] samples, int sampleRate)
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

    internal sealed record TranscriptionResponse(string? Text, List<TranscriptionWord>? Words);

    internal sealed record TranscriptionWord(string? Word, double Start, double End, int? Speaker);
}
