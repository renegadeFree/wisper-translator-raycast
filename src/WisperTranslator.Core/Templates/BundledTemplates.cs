namespace WisperTranslator.Core.Templates;

/// <summary>
/// Template inclusi nell'applicazione. Le istruzioni sono adattate alle pratiche dei
/// pacchetti di skill più diffusi (Obsidian Visual Skills, Mermaid Skill, md2mindmap):
/// struttura prima, testo breve, gruppi espliciti.
/// </summary>
public static class BundledTemplates
{
    private const string ObsidianSkills = "https://github.com/axtonliu/axton-obsidian-visual-skills";
    private const string MermaidSkill = "https://github.com/WH-2099/mermaid-skill";
    private const string Md2Mindmap = "https://github.com/chrisjianghp/md2mindmap";

    private static readonly MapStyle Scuro = new(MapPalette.ScuroCaldo);
    private static readonly MapStyle Freddo = new(MapPalette.ScuroFreddo);
    private static readonly MapStyle Chiaro = new(MapPalette.ChiaroProfessionale);
    private static readonly MapStyle Pastello = new(MapPalette.Pastello);
    private static readonly MapStyle Stampa = new(MapPalette.StampaBiancoNero);
    private static readonly MapStyle Contrasto = new(MapPalette.AltoContrasto);

    public static IReadOnlyList<MapTemplate> Maps { get; } =
    [
        new(
            "radiale-classica", "Radiale classica", "Un tema centrale con 5-8 rami e sotto-concetti.",
            MapEngine.Twopi, MapOrientation.Radial, Scuro, new MapLimits(16, 3, 5),
            "Costruisci una mappa radiale: un solo nodo centrale con il tema principale e 5-8 rami di "
            + "primo livello. Ogni ramo ha 1-3 sotto-concetti. Assegna a ogni ramo un 'group' con il nome "
            + "del ramo (es. \"Costi\", \"Persone\"). Non superare i 3 livelli. Etichette di 2-5 parole, "
            + "in forma di sostantivo o sintagma breve. Etichetta gli archi solo se la relazione non è ovvia.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "gerarchica-lr", "Gerarchica (sinistra→destra)", "Dal concetto generale ai dettagli, come un indice visivo.",
            MapEngine.Dot, MapOrientation.LeftRight, Chiaro, new MapLimits(20, 4, 6),
            "Costruisci una gerarchia da sinistra a destra: il nodo radice è il tema generale, ogni livello "
            + "scende nel dettaglio. Massimo 4 livelli. Usa i 'group' per i macro-argomenti e mantieni le "
            + "etichette di 2-6 parole. Nessun ciclo: gli archi vanno sempre dal generale al particolare.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "albero-tb", "Albero (alto→basso)", "Struttura ad albero verticale, adatta alla stampa.",
            MapEngine.Dot, MapOrientation.TopBottom, Freddo, new MapLimits(20, 4, 6),
            "Costruisci un albero verticale: radice in alto, figli sotto. Al massimo 4 livelli e 20 nodi. "
            + "Raggruppa i fratelli con lo stesso 'group'. Etichette brevi e parallele tra loro.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "raggiera-alto-contrasto", "Raggiera ad alto contrasto", "Rami molto leggibili, per proiezioni e schermi grandi.",
            MapEngine.Twopi, MapOrientation.Radial, Contrasto, new MapLimits(18, 3, 4),
            "Costruisci una raggiera con non più di 7 rami principali e 1-2 dettagli ciascuno. Etichette "
            + "cortissime (2-4 parole) e molto diverse tra loro: serviranno a orientarsi a colpo d'occhio. "
            + "Un 'group' per ramo.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "anelli", "Anelli concentrici", "Concetti ordinati per importanza dal centro verso l'esterno.",
            MapEngine.Circo, MapOrientation.Radial, Pastello, new MapLimits(16, 3, 5),
            "Disponi i concetti in anelli: al centro il tema, poi i concetti principali, poi i dettagli. "
            + "Assegna 'group' uguale ai nodi dello stesso anello (\"anello-1\", \"anello-2\"...). "
            + "Etichette di 2-5 parole.",
            Md2Mindmap, "chrisjianghp", "MIT"),

        new(
            "timeline", "Linea del tempo", "Fasi o eventi in ordine cronologico.",
            MapEngine.Dot, MapOrientation.Timeline, Scuro, new MapLimits(18, 3, 6),
            "Costruisci una sequenza cronologica: ogni nodo è una fase o un momento, collegato al successivo "
            + "in ordine. Ogni fase può avere 1-2 dettagli. Usa 'group' con il nome della fase. Le etichette "
            + "degli archi indicano il passaggio (\"poi\", \"dopo 2 settimane\", \"infine\").",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "confronto-ab", "Confronto A/B", "Due alternative messe a confronto punto per punto.",
            MapEngine.Dot, MapOrientation.Matrix, Chiaro, new MapLimits(14, 3, 5),
            "Metti a confronto due opzioni: crea due gruppi principali con i nomi delle alternative e "
            + "collega a ciascuna gli stessi criteri di valutazione, in modo che si leggano affiancati. "
            + "Aggiungi un nodo finale con il criterio di scelta. Etichette di 2-5 parole.",
            Md2Mindmap, "chrisjianghp", "MIT"),

        new(
            "swot", "SWOT 2×2", "Forze, debolezze, opportunità, minacce.",
            MapEngine.Dot, MapOrientation.Matrix, Contrasto, new MapLimits(12, 2, 5),
            "Costruisci un'analisi SWOT: esattamente quattro gruppi chiamati \"Forze\", \"Debolezze\", "
            + "\"Opportunità\", \"Minacce\", con 2-4 voci ciascuno. Nessun altro gruppo. "
            + "Etichette di 2-5 parole, nessuna frase intera.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "spina-pesce", "Spina di pesce (cause)", "Cause raggruppate per categoria a partire da un effetto.",
            MapEngine.Dot, MapOrientation.Fishbone, Stampa, new MapLimits(18, 3, 5),
            "Parti da un nodo finale con l'effetto o il problema e collega 4-6 categorie di cause "
            + "(persone, processi, strumenti, ambiente, materiali, misure). Ogni categoria ha 1-3 cause "
            + "concrete. Il 'group' è il nome della categoria. Etichette di 2-5 parole.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "albero-problemi", "Albero dei problemi", "Cause in alto, effetto al centro, conseguenze in basso.",
            MapEngine.Dot, MapOrientation.TopBottom, Freddo, new MapLimits(18, 4, 6),
            "Costruisci un albero dei problemi: le cause risalgono verso il problema centrale, e dal problema "
            + "scendono le conseguenze. Tre gruppi: \"Cause\", \"Problema\", \"Conseguenze\". Massimo 4 livelli.",
            Md2Mindmap, "chrisjianghp", "MIT"),

        new(
            "flusso-procedurale", "Flusso procedurale", "Passi di un processo o di una procedura.",
            MapEngine.Dot, MapOrientation.LeftRight, Chiaro, new MapLimits(14, 3, 6),
            "Descrivi la procedura come una sequenza di passi da sinistra a destra. Ogni passo è un nodo; "
            + "dove c'è una decisione, aggiungi due archi etichettati \"sì\" e \"no\". Raggruppa i passi per "
            + "fase con i 'group'. Etichette di 2-6 parole, verbo all'infinito.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "mappa-studio", "Mappa di studio", "Concetti, definizioni e domande di autoverifica.",
            MapEngine.Neato, MapOrientation.Radial, Pastello, new MapLimits(20, 3, 6),
            "Costruisci una mappa per lo studio: per ogni concetto importante aggiungi un nodo con la "
            + "definizione in 2-6 parole e, dove ha senso, un nodo-domanda collegato con arco etichettato "
            + "\"verifica\". Raggruppa per argomento. Non ripetere lo stesso concetto due volte.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "minimal-mono", "Minimal monocromatica", "Bianco e nero, poche parole, per appunti e stampa.",
            MapEngine.Dot, MapOrientation.LeftRight, Stampa, new MapLimits(16, 3, 4),
            "Costruisci la mappa più essenziale possibile: solo concetti chiave, nessuna ripetizione, "
            + "etichette di 1-4 parole, massimo 16 nodi. Nessuna etichetta sugli archi se non indispensabile.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "note-colorate", "Note colorate", "Post-it collegati, per brainstorming e idee.",
            MapEngine.Fdp, MapOrientation.Radial,
            new MapStyle(MapPalette.Pastello, MapNodeShape.Note, MapDensity.Ampia, Sketch: true),
            new MapLimits(18, 3, 6),
            "Raccogli le idee come post-it: frasi brevi (3-6 parole), un'idea per nodo, collegamenti "
            + "trasversali dove un'idea ne richiama un'altra. Raggruppa per tema. Nessuna gerarchia rigida.",
            Md2Mindmap, "chrisjianghp", "MIT"),
    ];

    public static IReadOnlyList<PdfTemplate> Pdfs { get; } =
    [
        new(
            "verbale-riunione", "Verbale di riunione", "Decisioni, azioni e responsabili.",
            new PdfPageSettings(), new PdfStyle(Palette: "professionale", TableOfContents: false),
            [
                new PdfSection("sintesi", "Sintesi", PdfSectionKind.Summary, MaxWords: 160),
                new PdfSection("partecipanti", "Partecipanti e ruoli", PdfSectionKind.Ai,
                    "Elenca solo le persone che parlano o vengono nominate, con il ruolo se deducibile. "
                    + "Se non ci sono nomi, scrivi \"Non desumibile dalla trascrizione\".", MaxWords: 120),
                new PdfSection("decisioni", "Decisioni prese", PdfSectionKind.Ai,
                    "Elenca le decisioni effettivamente prese, una per riga, con chi le ha proposte. "
                    + "Se non ce ne sono, dillo esplicitamente.", MaxWords: 200),
                new PdfSection("azioni", "Azioni da fare", PdfSectionKind.Ai,
                    "Elenca le azioni con: azione, responsabile, scadenza (anche \"da definire\"). "
                    + "Una riga per azione.", MaxWords: 220),
                new PdfSection("voci", "Partecipanti rilevati", PdfSectionKind.Speakers),
                new PdfSection("mappa", "Mappa concettuale", PdfSectionKind.Map, MapTemplateId: "gerarchica-lr"),
                new PdfSection("trascrizione", "Trascrizione", PdfSectionKind.Transcript),
            ],
            "Scrivi in tono asciutto e verificabile: solo ciò che è deducibile dalla trascrizione, "
            + "nessuna interpretazione. Non inventare nomi o date.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "appunti-lezione", "Appunti di lezione", "Concetti, definizioni e domande di verifica.",
            new PdfPageSettings(), new PdfStyle(Palette: "chiaro"),
            [
                new PdfSection("inbreve", "In breve", PdfSectionKind.Summary, MaxWords: 140),
                new PdfSection("concetti", "Concetti chiave", PdfSectionKind.KeyPoints),
                new PdfSection("definizioni", "Definizioni e formule", PdfSectionKind.Ai,
                    "Riporta le definizioni e le formule citate, una per riga nel formato "
                    + "\"termine: definizione\". Non aggiungere concetti non presenti.", MaxWords: 260),
                new PdfSection("mappa", "Mappa concettuale", PdfSectionKind.Map, MapTemplateId: "mappa-studio"),
                new PdfSection("verifica", "Domande di autoverifica", PdfSectionKind.Ai,
                    "Scrivi 5-8 domande a risposta breve sugli argomenti trattati, in ordine di difficoltà.",
                    MaxWords: 200),
                new PdfSection("trascrizione", "Trascrizione", PdfSectionKind.Transcript, ShowOriginal: false),
            ],
            "Usa un linguaggio da manuale: frasi brevi, termini tecnici solo se presenti nella lezione.",
            Md2Mindmap, "chrisjianghp", "MIT"),

        new(
            "intervista", "Intervista", "Domande, risposte e temi ricorrenti.",
            new PdfPageSettings(), new PdfStyle(Palette: "professionale"),
            [
                new PdfSection("sintesi", "Sintesi", PdfSectionKind.Summary, MaxWords: 150),
                new PdfSection("temi", "Temi emersi", PdfSectionKind.KeyPoints),
                new PdfSection("voci", "Voci rilevate", PdfSectionKind.Speakers),
                new PdfSection("citazioni", "Citazioni significative", PdfSectionKind.Ai,
                    "Scegli 5-8 frasi testuali rilevanti e riportale tra virgolette con il minuto in cui "
                    + "sono state dette, se disponibile. Nessuna parafrasi.", MaxWords: 260),
                new PdfSection("mappa", "Mappa dei temi", PdfSectionKind.Map, MapTemplateId: "radiale-classica"),
                new PdfSection("trascrizione", "Trascrizione integrale", PdfSectionKind.Transcript),
            ],
            "Distingui sempre chi parla quando è possibile; se non lo è, non inventare i turni.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "report-tecnico", "Report tecnico", "Contesto, requisiti, soluzioni e rischi.",
            new PdfPageSettings(), new PdfStyle(Palette: "chiaro", TableOfContents: true),
            [
                new PdfSection("contesto", "Contesto", PdfSectionKind.Ai,
                    "Descrivi il contesto e il problema affrontato in 3-6 righe.", MaxWords: 140),
                new PdfSection("requisiti", "Requisiti e vincoli", PdfSectionKind.Ai,
                    "Elenca requisiti e vincoli emersi, uno per riga.", MaxWords: 200),
                new PdfSection("soluzioni", "Soluzioni proposte", PdfSectionKind.Ai,
                    "Elenca le soluzioni discusse con vantaggi e svantaggi, una per paragrafo breve.",
                    MaxWords: 260),
                new PdfSection("rischi", "Rischi e punti aperti", PdfSectionKind.Ai,
                    "Elenca i rischi e le domande rimaste aperte, uno per riga.", MaxWords: 180),
                new PdfSection("mappa", "Mappa concettuale", PdfSectionKind.Map, MapTemplateId: "albero-problemi"),
                new PdfSection("trascrizione", "Trascrizione tecnica", PdfSectionKind.Transcript, ShowOriginal: true),
            ],
            "Usa un registro tecnico e neutro, con elenchi puntati brevi.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "executive-summary", "Executive summary", "Una pagina: sintesi, decisioni, numeri.",
            new PdfPageSettings(), new PdfStyle(Palette: "professionale", Cover: false, TableOfContents: false),
            [
                new PdfSection("messaggio", "Messaggio principale", PdfSectionKind.Ai,
                    "In 3-5 righe: che cosa è successo e perché conta.", MaxWords: 110),
                new PdfSection("punti", "Punti chiave", PdfSectionKind.KeyPoints),
                new PdfSection("numeri", "Numeri e dati citati", PdfSectionKind.Ai,
                    "Elenca solo i numeri, le date e le misure menzionate, uno per riga. "
                    + "Se non ce ne sono, dillo.", MaxWords: 120),
                new PdfSection("prossimi", "Prossimi passi", PdfSectionKind.Ai,
                    "Elenca i prossimi passi con il responsabile se indicato.", MaxWords: 140),
            ],
            "Massima densità informativa, frasi brevi, niente ripetizioni.",
            ObsidianSkills, "Axton Liu", "MIT"),

        new(
            "trascrizione-fedele", "Trascrizione fedele", "Solo la trascrizione bilingue con timecode.",
            new PdfPageSettings(), new PdfStyle(Palette: "chiaro", Cover: false, Header: false),
            [
                new PdfSection("trascrizione", "Trascrizione", PdfSectionKind.Transcript,
                    ShowOriginal: true, ShowTimestamps: true),
            ],
            "Nessun testo generato: il documento è la trascrizione così com'è.",
            MermaidSkill, "WH-2099", "MIT"),

        new(
            "brainstorming", "Brainstorming", "Idee raccolte e raggruppate, con mappa delle note.",
            new PdfPageSettings(), new PdfStyle(Palette: "pastello"),
            [
                new PdfSection("sintesi", "Sintesi", PdfSectionKind.Summary, MaxWords: 140),
                new PdfSection("idee", "Idee emerse", PdfSectionKind.Ai,
                    "Elenca tutte le idee emerse, una per riga, senza valutarle. Non fondere idee diverse.",
                    MaxWords: 300),
                new PdfSection("mappa", "Mappa delle idee", PdfSectionKind.Map, MapTemplateId: "note-colorate"),
                new PdfSection("trascrizione", "Trascrizione", PdfSectionKind.Transcript, ShowOriginal: false),
            ],
            "Non giudicare le idee e non scartarne: raccoglile tutte, anche quelle abbozzate.",
            Md2Mindmap, "chrisjianghp", "MIT"),

        new(
            "post-mortem", "Post-mortem", "Cosa è andato bene, cosa no, cosa cambiare.",
            new PdfPageSettings(), new PdfStyle(Palette: "professionale"),
            [
                new PdfSection("sintesi", "Sintesi dell'evento", PdfSectionKind.Summary, MaxWords: 150),
                new PdfSection("bene", "Cosa ha funzionato", PdfSectionKind.Ai,
                    "Elenca i fattori positivi citati, uno per riga.", MaxWords: 180),
                new PdfSection("male", "Cosa non ha funzionato", PdfSectionKind.Ai,
                    "Elenca i problemi e le cause, uno per riga.", MaxWords: 200),
                new PdfSection("azioni", "Azioni correttive", PdfSectionKind.Ai,
                    "Elenca le azioni correttive con responsabile e scadenza se indicati.", MaxWords: 200),
                new PdfSection("mappa", "Mappa delle cause", PdfSectionKind.Map, MapTemplateId: "spina-pesce"),
                new PdfSection("trascrizione", "Trascrizione", PdfSectionKind.Transcript, ShowOriginal: false),
            ],
            "Tono fattuale e non accusatorio: cause e azioni, non colpevoli.",
            ObsidianSkills, "Axton Liu", "MIT"),
    ];

    public static MapTemplate? Map(string id) =>
        Maps.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));

    public static PdfTemplate? Pdf(string id) =>
        Pdfs.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));
}
