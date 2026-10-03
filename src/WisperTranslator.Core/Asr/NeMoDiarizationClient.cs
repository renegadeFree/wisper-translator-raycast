using System.Net.Http.Json;
using System.Text.Json;

namespace WisperTranslator.Core.Asr;

public sealed record DiarizationSegment(double Start, double End, int Speaker);

/// <summary>
/// Diarizzazione separata dalla trascrizione: il testo finale arriva subito e l'etichetta
/// del parlante si risolve dopo, senza tenere in ostaggio il sottotitolo.
/// </summary>
public sealed class NeMoDiarizationClient : IDisposable
{
    private readonly int _port;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(2) };

    public NeMoDiarizationClient(int port) => _port = port;

    public async Task<IReadOnlyList<DiarizationSegment>> DiarizeAsync(
        float[] samples,
        int sampleRate = 16000,
        CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0)
        {
            return [];
        }

        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(NeMoSpeechEngine.ToWav(samples, sampleRate));
        audio.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(audio, "file", "chunk.wav");
        content.Add(new StringContent("offline"), "mode");

        using var response = await _client
            .PostAsync(
                $"http://127.0.0.1:{_port}/v1/audio/diarizations",
                content,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<DiarizationResponse>(cancellationToken)
            .ConfigureAwait(false);

        return payload?.Segments is { Count: > 0 }
            ? [.. payload.Segments.Select(segment =>
                new DiarizationSegment(segment.Start, segment.End, segment.Speaker))]
            : [];
    }

    private sealed record DiarizationResponse(List<DiarizationItem>? Segments);

    private sealed record DiarizationItem(double Start, double End, int Speaker);

    /// <summary>Attribuisce a ogni parola il segmento di diarizzazione che contiene il suo centro.</summary>
    public static IReadOnlyList<AsrSegment> ApplySpeakers(
        IReadOnlyList<AsrSegment> words,
        IReadOnlyList<DiarizationSegment> segments)
    {
        if (words.Count == 0 || segments.Count == 0)
        {
            return words;
        }

        var result = new List<AsrSegment>(words.Count);
        foreach (var word in words)
        {
            var center = (word.Start + (word.Duration / 2)).TotalSeconds;
            var speaker = segments
                .Where(segment => center >= segment.Start - 0.05 && center <= segment.End + 0.05)
                .Select(segment => segment.Speaker)
                .DefaultIfEmpty(0)
                .First();
            result.Add(word with { Speaker = speaker });
        }

        return result;
    }

    public void Dispose() => _client.Dispose();
}
