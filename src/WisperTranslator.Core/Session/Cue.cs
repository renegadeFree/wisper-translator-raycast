namespace WisperTranslator.Core.Session;

/// <summary>
/// Etichette dei parlanti. Il diarizzatore restituisce id 1-based (massimo 8); il microfono
/// è per definizione "Tu"; 0 significa "nessuno identificato" (una sola etichetta generica).
/// </summary>
public static class Speakers
{
    public const int Unknown = 0;
    public const int You = -1;

    /// <summary>Etichetta pronta da mostrare, vuota quando non c'è nulla da dire.</summary>
    public static string Label(int speaker, string fallback = "") => speaker switch
    {
        You => "Tu",
        Unknown => fallback,
        _ => $"Speaker {speaker}",
    };

    /// <summary>Colore stabile per parlante: lo stesso id ha sempre lo stesso pallino.</summary>
    public static string Color(int speaker) => speaker switch
    {
        You => "#3B82F6",
        Unknown => "#8A8A8E",
        _ => Palette[(speaker - 1) % Palette.Length],
    };

    private static readonly string[] Palette =
    [
        "#F59E0B",
        "#10B981",
        "#8B5CF6",
        "#EF4444",
        "#06B6D4",
        "#EC4899",
        "#84CC16",
        "#6366F1",
    ];
}

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
    string Provisional = "",
    int Speaker = Speakers.Unknown)
{
    public bool HasTranslation => Translation.Length > 0;

    /// <summary>Nome del parlante da mostrare (vuoto se il parlante non è noto).</summary>
    public string SpeakerLabel => Speakers.Label(Speaker);

    public bool HasSpeaker => SpeakerLabel.Length > 0;

    public string SpeakerColor => Speakers.Color(Speaker);

    /// <summary>Coda dell'ipotesi non ancora confermata, da mostrare attenuata.</summary>
    public string ProvisionalTail => Provisional.StartsWith(Original, StringComparison.Ordinal)
        ? Provisional[Original.Length..].TrimStart()
        : Provisional;

    public bool HasProvisional => !IsFinal && ProvisionalTail.Length > 0;

    /// <summary>Testo originale da mostrare: la parte stabile, o l'ipotesi se non c'è ancora nulla.</summary>
    public string DisplayOriginal => Original.Length > 0 ? Original : Provisional;
}
