using System.Text;
using WisperTranslator.Core.Settings;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Core.Ai;

public enum AiTask
{
    ShortSummary,
    DetailedSummary,
    KeyPoints,
    ConceptMap,
}

/// <summary>
/// Compone i prompt e chiama il provider scelto per produrre riassunti, punti chiave e mappe
/// concettuali a partire da una sessione trascritta.
/// </summary>
public sealed class AiAssistant : IDisposable
{
    private readonly IAiClient _client;
    private readonly AiSettings _settings;

    public AiAssistant(AiSettings settings)
    {
        _settings = settings;
        _client = AiClientFactory.Create(settings);
    }

    public string ProviderName => _client.Name;

    public string Model => _settings.Model;

    /// <summary>Verifica rapida della configurazione: chiede una risposta minima al provider.</summary>
    public async Task<string> TestAsync(CancellationToken cancellationToken = default)
    {
        var answer = await _client
            .CompleteAsync([AiChatMessage.User("Rispondi solo con: OK")], cancellationToken)
            .ConfigureAwait(false);
        return answer.Length <= 40 ? answer : answer[..40];
    }

    public async Task<string> RunAsync(
        AiTask task,
        string transcript,
        CancellationToken cancellationToken = default)
    {
        var messages = BuildMessages(task, transcript);
        return await _client.CompleteAsync(messages, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConceptMap> BuildConceptMapAsync(string transcript, CancellationToken cancellationToken = default)
    {
        var raw = await RunAsync(AiTask.ConceptMap, transcript, cancellationToken).ConfigureAwait(false);
        return ConceptMap.Parse(raw);
    }

    /// <summary>Testo per una sezione di un template PDF (o per un template importato).</summary>
    public async Task<string> RunTemplateAsync(
        string instructions,
        string transcript,
        int maxWords,
        CancellationToken cancellationToken = default)
    {
        var language = _settings.OutputLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) ? "inglese" : "italiano";
        var messages = new List<AiChatMessage>
        {
            AiChatMessage.System(
                $"Sei un assistente che lavora su trascrizioni audio e produce documenti. Rispondi in {language}. "
                + (string.IsNullOrWhiteSpace(instructions) ? string.Empty : instructions + " ")
                + $"Usa al massimo {Math.Max(40, maxWords)} parole. "
                + "Attieniti a ciò che è presente nella trascrizione: non inventare nomi, numeri o fatti. "
                + "Niente preamboli, niente ripetizioni della consegna."),
            AiChatMessage.User($"Trascrizione:\n{Trim(transcript, 24_000)}"),
        };

        return await _client.CompleteAsync(messages, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Mappa concettuale guidata dal template: le istruzioni del template definiscono struttura,
    /// gruppi e limiti; se il modello risponde male si ritenta una volta con una correzione.
    /// </summary>
    public async Task<ConceptMap> BuildConceptMapAsync(
        MapTemplate template,
        string transcript,
        CancellationToken cancellationToken = default)
    {
        var language = _settings.OutputLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) ? "inglese" : "italiano";
        var system =
            "Costruisci una mappa concettuale della trascrizione. Rispondi SOLO con JSON valido, senza testo "
            + "attorno e senza commenti, in questo formato: "
            + "{\"title\":\"titolo breve\",\"nodes\":[{\"id\":\"n1\",\"label\":\"concetto\",\"group\":\"tema\"}],"
            + "\"edges\":[{\"from\":\"n1\",\"to\":\"n2\",\"label\":\"relazione\"}]}. "
            + $"Le etichette devono essere in {language}, di al massimo {template.Limits.MaxLabelWords} parole. "
            + $"Usa al massimo {template.Limits.MaxNodes} nodi e {template.Limits.MaxDepth} livelli di profondità. "
            + "Ogni nodo deve avere un id unico, ogni arco deve riferirsi a id esistenti e ogni nodo dovrebbe avere un "
            + "\"group\" con il nome del tema. "
            + template.Instructions;

        var messages = new List<AiChatMessage>
        {
            AiChatMessage.System(system),
            AiChatMessage.User($"Trascrizione:\n{Trim(transcript, 24_000)}"),
        };

        var map = ConceptMap.Parse(await _client.CompleteAsync(messages, cancellationToken).ConfigureAwait(false), template.Name);
        if (!map.IsEmpty)
        {
            return map;
        }

        messages.Add(AiChatMessage.User("La risposta precedente non conteneva JSON valido. Rispondi di nuovo SOLO con il JSON."));
        return ConceptMap.Parse(await _client.CompleteAsync(messages, cancellationToken).ConfigureAwait(false), template.Name);
    }

    private IReadOnlyList<AiChatMessage> BuildMessages(AiTask task, string transcript)
    {
        var language = _settings.OutputLanguage.Equals("en", StringComparison.OrdinalIgnoreCase) ? "inglese" : "italiano";
        var content = Trim(transcript, 24_000);

        return task switch
        {
            AiTask.ShortSummary =>
            [
                AiChatMessage.System(
                    $"Sei un assistente che riassume trascrizioni di audio. Rispondi in {language}, "
                    + "con un paragrafo di 4-6 righe e poi 3-5 punti chiave. Niente preamboli."),
                AiChatMessage.User($"Trascrizione:\n{content}"),
            ],
            AiTask.DetailedSummary =>
            [
                AiChatMessage.System(
                    $"Sei un assistente che riassume trascrizioni di audio. Rispondi in {language}. "
                    + "Struttura: titolo, contesto, argomenti trattati in ordine, decisioni o richieste, "
                    + "punti aperti. Usa titoli di sezione brevi. Niente preamboli."),
                AiChatMessage.User($"Trascrizione:\n{content}"),
            ],
            AiTask.KeyPoints =>
            [
                AiChatMessage.System(
                    $"Estrai i punti chiave dalla trascrizione. Rispondi in {language} con un elenco "
                    + "puntato di 5-12 voci, ognuna di una riga, in ordine di importanza. Niente preamboli."),
                AiChatMessage.User($"Trascrizione:\n{content}"),
            ],
            _ =>
            [
                AiChatMessage.System(
                    "Costruisci una mappa concettuale della trascrizione. Rispondi SOLO con JSON valido, "
                    + "senza testo attorno e senza commenti, in questo formato: "
                    + "{\"title\":\"titolo breve\","
                    + "\"nodes\":[{\"id\":\"n1\",\"label\":\"concetto\",\"group\":\"tema\"}],"
                    + "\"edges\":[{\"from\":\"n1\",\"to\":\"n2\",\"label\":\"relazione\"}]}. "
                    + $"Le etichette devono essere in {language}, brevi (max 6 parole). "
                    + "Usa da 5 a 15 nodi. Ogni nodo deve avere un id unico e ogni arco deve riferirsi a id esistenti."),
                AiChatMessage.User($"Trascrizione:\n{content}"),
            ],
        };
    }

    private static string Trim(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        // Le sessioni lunghe si troncano dal centro per non perdere inizio e fine.
        var half = limit / 2;
        var builder = new StringBuilder(text.Length);
        builder.Append(text[..half]);
        builder.AppendLine("\n[... parte centrale omessa ...]\n");
        builder.Append(text[^half..]);
        return builder.ToString();
    }

    public void Dispose() => _client.Dispose();
}
