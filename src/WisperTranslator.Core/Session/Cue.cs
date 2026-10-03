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
    DateTime CreatedAt,
    string Provisional = "")
{
    public bool HasTranslation => Translation.Length > 0;

    /// <summary>Coda dell'ipotesi non ancora confermata, da mostrare attenuata.</summary>
    public string ProvisionalTail => Provisional.StartsWith(Original, StringComparison.Ordinal)
        ? Provisional[Original.Length..].TrimStart()
        : Provisional;

    public bool HasProvisional => !IsFinal && ProvisionalTail.Length > 0;

    /// <summary>Testo originale da mostrare: la parte stabile, o l'ipotesi se non c'è ancora nulla.</summary>
    public string DisplayOriginal => Original.Length > 0 ? Original : Provisional;
}
