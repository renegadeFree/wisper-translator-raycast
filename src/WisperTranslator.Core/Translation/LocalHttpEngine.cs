using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Motore locale: parla con il server HTTP di MTranServer (Apache-2.0), che usa i modelli
/// Bergamot/Mozilla. Su una frase media risponde in ~30 ms senza GPU.
/// </summary>
public sealed class LocalHttpEngine : ITranslationEngine
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _baseUrl;

    public LocalHttpEngine(int port = 8989)
        : this($"http://127.0.0.1:{port}")
    {
    }

    public LocalHttpEngine(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        Name = $"mtran:{_baseUrl}";
    }

    public string Name { get; }

    public string BaseUrl => _baseUrl;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await TranslateAsync("test", "en", "it", cancellationToken).ConfigureAwait(false);
            return result.Text.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(string.Empty, from, to, TimeSpan.Zero, false);
        }

        var watch = Stopwatch.StartNew();
        using var response = await Client
            .PostAsJsonAsync($"{_baseUrl}/translate", new TranslateRequest(from, to, text), cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<TranslateResponse>(cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        return new TranslationResult(payload?.Result ?? string.Empty, from, to, watch.Elapsed, false);
    }

    public void Dispose()
    {
    }

    private sealed record TranslateRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("text")] string Text);

    private sealed record TranslateResponse(
        [property: JsonPropertyName("result")] string? Result);
}
