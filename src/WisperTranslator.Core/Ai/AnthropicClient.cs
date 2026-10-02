using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WisperTranslator.Core.Settings;

namespace WisperTranslator.Core.Ai;

/// <summary>Client per l'API Messages di Anthropic (formato diverso da OpenAI).</summary>
public sealed class AnthropicClient : IAiClient
{
    private readonly HttpClient _client;
    private readonly AiSettings _settings;

    public AnthropicClient(AiSettings settings)
    {
        _settings = settings;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, settings.TimeoutSeconds)) };
        _client.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey.Trim());
        _client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public string Name => _settings.DisplayName;

    public async Task<string> CompleteAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var system = string.Join("\n", messages.Where(m => m.Role == "system").Select(m => m.Content));
        var conversation = messages
            .Where(message => message.Role != "system")
            .Select(message => new ChatMessage(message.Role == "assistant" ? "assistant" : "user", message.Content))
            .ToArray();

        var request = new MessageRequest(_settings.Model, _settings.MaxTokens, system, conversation, _settings.Temperature);
        using var response = await _client
            .PostAsJsonAsync(_settings.Endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"{Name} ha risposto {(int)response.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        }

        var payload = await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken).ConfigureAwait(false);
        var text = payload?.Content?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"{Name} ha restituito una risposta vuota.");
        }

        return text.Trim();
    }

    public void Dispose() => _client.Dispose();

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MessageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record MessageResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("text")] string? Text);
}
