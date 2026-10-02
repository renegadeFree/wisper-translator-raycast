namespace WisperTranslator.Core.Settings;

/// <summary>Tipo di servizio IA usato per riassunti e mappe concettuali.</summary>
public enum AiProviderKind
{
    /// <summary>Ollama in locale (http://localhost:11434/v1). Nessuna chiave, nessun dato in rete.</summary>
    Ollama,

    /// <summary>Qualsiasi endpoint compatibile OpenAI (LM Studio, vLLM, OpenRouter, DeepSeek, Groq…).</summary>
    OpenAiCompatible,

    OpenAi,
    DeepSeek,
    OpenRouter,
    Groq,
    Mistral,
    Gemini,
    Anthropic,
}

public sealed class AiSettings
{
    public AiProviderKind Provider { get; set; } = AiProviderKind.Ollama;

    public string Endpoint { get; set; } = "http://localhost:11434/v1/chat/completions";

    public string Model { get; set; } = "qwen2.5:7b-instruct";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Lingua dei riassunti generati.</summary>
    public string OutputLanguage { get; set; } = "it";

    public double Temperature { get; set; } = 0.2;

    public int MaxTokens { get; set; } = 2048;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// I modelli locali "thinking" (Qwen, Gemma, DeepSeek-R1…) bruciano il budget di token nel
    /// ragionamento e restituiscono testo vuoto. Con Ollama si disattiva con
    /// <c>reasoning_effort=none</c>: misurato su questa macchina, da 67 s a 2,4 s sulla stessa richiesta.
    /// </summary>
    public bool DisableReasoning { get; set; } = true;

    /// <summary>Applica i valori predefiniti del provider scelto (endpoint e modello).</summary>
    public static (string Endpoint, string Model) DefaultsFor(AiProviderKind provider) => provider switch
    {
        AiProviderKind.Ollama => ("http://localhost:11434/v1/chat/completions", "qwen2.5:7b-instruct"),
        AiProviderKind.OpenAi => ("https://api.openai.com/v1/chat/completions", "gpt-4o-mini"),
        AiProviderKind.DeepSeek => ("https://api.deepseek.com/v1/chat/completions", "deepseek-chat"),
        AiProviderKind.OpenRouter => ("https://openrouter.ai/api/v1/chat/completions", "openai/gpt-4o-mini"),
        AiProviderKind.Groq => ("https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile"),
        AiProviderKind.Mistral => ("https://api.mistral.ai/v1/chat/completions", "mistral-small-latest"),
        AiProviderKind.Gemini => ("https://generativelanguage.googleapis.com/v1beta/models", "gemini-2.0-flash"),
        AiProviderKind.Anthropic => ("https://api.anthropic.com/v1/messages", "claude-3-5-sonnet-latest"),
        _ => ("http://localhost:1234/v1/chat/completions", "local-model"),
    };

    public string DisplayName => Provider switch
    {
        AiProviderKind.Ollama => "Ollama (locale)",
        AiProviderKind.OpenAiCompatible => "Compatibile OpenAI (LM Studio, vLLM…)",
        AiProviderKind.OpenAi => "OpenAI",
        AiProviderKind.DeepSeek => "DeepSeek",
        AiProviderKind.OpenRouter => "OpenRouter",
        AiProviderKind.Groq => "Groq",
        AiProviderKind.Mistral => "Mistral",
        AiProviderKind.Gemini => "Google Gemini",
        AiProviderKind.Anthropic => "Anthropic Claude",
        _ => Provider.ToString(),
    };
}
