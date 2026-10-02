namespace WisperTranslator.Core.Session;

/// <summary>
/// Una battuta a schermo: testo originale e traduzione. <c>IsFinal</c> è falso finché
/// l'enunciato è in corso, così la UI sa cosa può ancora cambiare.
/// </summary>
public sealed record Cue(
    int Id,
    string Original,
    string Translation,
    bool IsFinal,
    TimeSpan AudioStart,
    TimeSpan Duration,
    DateTime CreatedAt)
{
    public bool HasTranslation => Translation.Length > 0;
}
