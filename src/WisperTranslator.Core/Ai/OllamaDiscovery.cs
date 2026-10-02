using System.Text.Json;

namespace WisperTranslator.Core.Ai;

/// <summary>Elenca i modelli installati in un'istanza Ollama locale.</summary>
public static class OllamaDiscovery
{
    public static async Task<IReadOnlyList<string>> ListModelsAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = endpoint;
        var index = baseUrl.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
        if (index > 0)
        {
            baseUrl = baseUrl[..index];
        }

        baseUrl = baseUrl.TrimEnd('/');
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var json = await client.GetStringAsync($"{baseUrl}/api/tags", cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("models", out var models))
        {
            return [];
        }

        return
        [
            .. models.EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!),
        ];
    }
}
