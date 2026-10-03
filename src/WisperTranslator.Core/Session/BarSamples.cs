namespace WisperTranslator.Core.Session;

/// <summary>
/// Contenuto d'esempio per gli screenshot del README: frasi realistiche, parlanti diversi e una
/// riga ancora provvisoria, così l'immagine mostra lo stato vero della barra senza catturare lo
/// schermo di nessuno.
/// </summary>
public static class BarSamples
{
    public static IReadOnlyList<Cue> Cues()
    {
        var started = new DateTime(2026, 10, 3, 9, 30, 0, DateTimeKind.Local);
        var at = TimeSpan.Zero;

        Cue Next(int id, int speaker, string original, string translation, bool isFinal, string provisional = "")
        {
            var cue = new Cue(
                id,
                original,
                translation,
                isFinal,
                at,
                TimeSpan.FromSeconds(7),
                started.Add(at),
                provisional,
                speaker);
            at += TimeSpan.FromSeconds(9);
            return cue;
        }

        return
        [
            Next(
                3,
                Speakers.You,
                "Allora, per la riunione di domani preparo io la presentazione e tu ricontrolli i numeri del bilancio.",
                "So, for tomorrow's meeting I'll prepare the presentation and you double-check the budget figures.",
                true),
            Next(
                2,
                2,
                "Perfetto, allora stasera mando il file aggiornato a tutto il team così ognuno lo legge prima.",
                "Perfect, so tonight I'll send the updated file to the whole team so everyone can read it beforehand.",
                true),
            Next(
                1,
                1,
                "Aspetta, il preventivo non è ancora approvato:",
                "Hold on, the quote hasn't been approved yet:",
                false,
                "Aspetta, il preventivo non è ancora approvato: meglio aspettare lunedì prima di condividerlo."),
        ];
    }
}
