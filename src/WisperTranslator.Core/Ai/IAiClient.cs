namespace WisperTranslator.Core.Ai;

/// <summary>Client verso un servizio IA (locale o cloud).</summary>
public interface IAiClient : IDisposable
{
    string Name { get; }

    Task<string> CompleteAsync(
        IReadOnlyList<AiChatMessage> messages,
        CancellationToken cancellationToken = default);
}
