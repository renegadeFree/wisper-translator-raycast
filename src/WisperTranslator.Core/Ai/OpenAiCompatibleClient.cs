using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WisperTranslator.Core.Settings;

namespace WisperTranslator.Core.Ai;

/// <summary>
/// Client per qualsiasi endpoint compatibile OpenAI: Ollama, LM Studio, vLLM, OpenAI, DeepSeek,
/// OpenRouter, Groq, Mistral. È il formato più diffuso, quindi copre la maggior parte dei casi.
/// </summary>
public sealed class OpenAiCompatibleClient : IAiClient
{
    private readonly HttpClient _client;
    private readonly AiSettings _settings;

    public OpenAiCompatibleClient(AiSettings settings)
    {
        _settings = settings;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, settings.TimeoutSeconds)) };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());
        }
    }

    public string Name => _settings.DisplayName;

    public async Task<string> CompleteAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest(
            _settings.Model,
            [.. messages.Select(message => new ChatMessage(message.Role, message.Content))],
            _settings.Temperature,
            _settings.MaxTokens,
            _settings is { DisableReasoning: true, Provider: AiProviderKind.Ollama } ? "none" : null);

        using var response = await _client
            .PostAsJsonAsync(_settings.Endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{Name} ha risposto {(int)response.StatusCode}: {Shorten(body)}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<ChatResponse>(cancellationToken)
            .ConfigureAwait(false);

        var text = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            var reasoning = payload?.Choices?.FirstOrDefault()?.Message?.Reasoning;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(reasoning)
                    ? $"{Name} ha restituito una risposta vuota (modello \"{_settings.Model}\")."
                    : $"{Name}: il modello ha speso i {_settings.MaxTokens} token nel ragionamento senza "
                      + "produrre testo. Aumenta i token massimi o attiva «Salta il ragionamento».");
        }

        return text.Trim();
    }

    private static string Shorten(string text) =>
        text.Length <= 300 ? text : text[..300] + "…";

    public void Dispose() => _client.Dispose();

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("reasoning")] string? Reasoning = null);

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("reasoning_effort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ReasoningEffort);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
