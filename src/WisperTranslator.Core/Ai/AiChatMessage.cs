namespace WisperTranslator.Core.Ai;

public sealed record AiChatMessage(string Role, string Content)
{
    public static AiChatMessage System(string content) => new("system", content);

    public static AiChatMessage User(string content) => new("user", content);
}
