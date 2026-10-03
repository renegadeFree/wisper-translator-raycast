using WisperTranslator.Core.Ai;

namespace WisperTranslator.Core.Rendering;

/// <summary>Mappa di esempio: serve all'anteprima dei template e alle verifiche.</summary>
public static class SampleContent
{
    public static ConceptMap Map() => new(
        "Come funziona la trascrizione in tempo reale",
        [
            new ConceptNode("n1", "Trascrizione in tempo reale"),
            new ConceptNode("n2", "Cattura loopback", "Audio"),
            new ConceptNode("n3", "Microfono", "Audio"),
            new ConceptNode("n4", "Voce rilevata", "Audio"),
            new ConceptNode("n5", "Modello streaming", "Motore"),
            new ConceptNode("n6", "Frase definitiva", "Motore"),
            new ConceptNode("n7", "Traduzione locale", "Testi"),
            new ConceptNode("n8", "Sottotitoli a schermo", "Testi"),
            new ConceptNode("n9", "Storico sessioni", "Dati"),
            new ConceptNode("n10", "Mappa concettuale", "Dati"),
            new ConceptNode("n11", "Report PDF", "Dati"),
        ],
        [
            new ConceptEdge("n1", "n2", "ascolta"),
            new ConceptEdge("n1", "n3", "ascolta"),
            new ConceptEdge("n2", "n4"),
            new ConceptEdge("n3", "n4"),
            new ConceptEdge("n4", "n5", "160 ms"),
            new ConceptEdge("n5", "n6", "rifinisce"),
            new ConceptEdge("n1", "n5"),
            new ConceptEdge("n6", "n7"),
            new ConceptEdge("n7", "n8"),
            new ConceptEdge("n1", "n8"),
            new ConceptEdge("n6", "n9"),
            new ConceptEdge("n1", "n9"),
            new ConceptEdge("n9", "n10", "genera"),
            new ConceptEdge("n9", "n11", "esporta"),
        ]);
}
