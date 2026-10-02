using WisperTranslator.Core.Settings;

namespace WisperTranslator.Core.Ai;

public static class AiClientFactory
{
    public static IAiClient Create(AiSettings settings) => settings.Provider switch
    {
        AiProviderKind.Anthropic => new AnthropicClient(settings),
        AiProviderKind.Gemini => new GeminiClient(settings),
        _ => new OpenAiCompatibleClient(settings),
    };
}
