using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Motore cloud opzionale (API compatibile OpenAI, DeepL-like o qualsiasi endpoint chat):
/// qualità superiore sui sottotitoli, richiede internet e una chiave. Disattivato per default.
/// </summary>
public sealed class OpenAiCompatibleEngine : ITranslationEngine
{
    private readonly HttpClient _client;
    private readonly string _endpoint;
    private readonly string _model;

    public OpenAiCompatibleEngine(string apiKey, string endpoint, string model)
    {
        _endpoint = endpoint;
        _model = model;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        Name = $"openai:{model}";
    }

    public string Name { get; }

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

        var request = new ChatRequest(
            _model,
            [
                new ChatMessage("system",
                    $"You are a subtitle translator. Translate from {from} to {to}. "
                    + "Reply with the translation only, no explanations, no quotes."),
                new ChatMessage("user", text),
            ],
            Temperature: 0);

        var watch = Stopwatch.StartNew();
        using var response = await _client
            .PostAsJsonAsync(_endpoint, request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<ChatResponse>(cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var translated = payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
        return new TranslationResult(translated, from, to, watch.Elapsed, false);
    }

    public void Dispose() => _client.Dispose();

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
