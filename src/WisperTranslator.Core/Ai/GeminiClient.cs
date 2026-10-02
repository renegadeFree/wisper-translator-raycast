using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WisperTranslator.Core.Settings;

namespace WisperTranslator.Core.Ai;

/// <summary>Client per Google Gemini (`generateContent`).</summary>
public sealed class GeminiClient : IAiClient
{
    private readonly HttpClient _client;
    private readonly AiSettings _settings;

    public GeminiClient(AiSettings settings)
    {
        _settings = settings;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, settings.TimeoutSeconds)) };
    }

    public string Name => _settings.DisplayName;

    public async Task<string> CompleteAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var system = string.Join("\n", messages.Where(m => m.Role == "system").Select(m => m.Content));
        var contents = messages
            .Where(message => message.Role != "system")
            .Select(message => new Content(message.Role == "assistant" ? "model" : "user", [new Part(message.Content)]))
            .ToArray();

        var request = new GenerateRequest(
            system.Length > 0 ? new SystemInstruction([new Part(system)]) : null,
            contents,
            new GenerationConfig(_settings.Temperature, _settings.MaxTokens));

        var url = $"{_settings.Endpoint.TrimEnd('/')}/{_settings.Model}:generateContent?key={_settings.ApiKey.Trim()}";
        using var response = await _client.PostAsJsonAsync(url, request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"{Name} ha risposto {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        }

        var payload = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken).ConfigureAwait(false);
        var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"{Name} ha restituito una risposta vuota.");
        }

        return text.Trim();
    }

    public void Dispose() => _client.Dispose();

    private sealed record Part([property: JsonPropertyName("text")] string Text);

    private sealed record Content(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] Part[] Parts);

    private sealed record SystemInstruction(
        [property: JsonPropertyName("parts")] Part[] Parts);

    private sealed record GenerationConfig(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxTokens);

    private sealed record GenerateRequest(
        [property: JsonPropertyName("systemInstruction")] SystemInstruction? SystemInstruction,
        [property: JsonPropertyName("contents")] Content[] Contents,
        [property: JsonPropertyName("generationConfig")] GenerationConfig GenerationConfig);

    private sealed record GenerateResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<Candidate>? Candidates);

    private sealed record Candidate(
        [property: JsonPropertyName("content")] Content? Content);
}
