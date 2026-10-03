using WisperTranslator.Core.Models;
using WisperTranslator.Core.Hardware;

namespace WisperTranslator.Core.Session;

/// <summary>Architettura della pipeline di traduzione.</summary>
public enum TranslationPipelineMode
{
    /// <summary>Doppia passata: istantanea parola per parola + rifinitura neurale Marian ONNX.</summary>
    DualPass,

    /// <summary>Solo istantanea (Bergamot): continuo e leggero, senza secondo stadio.</summary>
    FastOnly,

    /// <summary>Solo qualità (Marian ONNX): nessun parziale grezzo, traduzione accurata su frase conclusa.</summary>
    QualityOnly,
}

/// <summary>Come usare il secondo motore di traduzione (rifinitura di qualità).</summary>
public enum QualityTranslationMode
{
    /// <summary>Attivo da solo sulle macchine che lo reggono, spento su quelle minime.</summary>
    Auto,

    /// <summary>Mai: resta la sola corsia rapida.</summary>
    Off,

    /// <summary>Sempre, anche su macchine deboli (scelta esplicita dell'utente).</summary>
    Forced,
}

/// <summary>Configurazione di una sessione di trascrizione e traduzione.</summary>
public sealed class SessionOptions
{
    /// <summary>Modello usato per i parziali: deve essere veloce.</summary>
    public AsrModelSpec PartialModel { get; set; } = AsrModels.Base;

    /// <summary>Modello usato per le frasi finali: deve essere accurato.</summary>
    public AsrModelSpec FinalModel { get; set; } = AsrModels.Small;

    /// <summary>Corsia del testo immediato (parziali).</summary>
    public AsrBackend LiveBackend { get; set; } = AsrBackend.WhisperCpu;

    /// <summary>Corsia della frase definitiva.</summary>
    public AsrBackend FinalBackend { get; set; } = AsrBackend.WhisperCpu;

    /// <summary>Profilo scelto: decide cosa si può scaricare da soli senza appesantire la macchina.</summary>
    public PerformancePreset PerformancePreset { get; set; } = PerformancePreset.Auto;

    public string SourceLanguage { get; set; } = "it";

    public string TargetLanguage { get; set; } = "en";

    public bool Translate { get; set; } = true;

    public bool SystemAudio { get; set; } = true;

    public bool Microphone { get; set; }

    /// <summary>
    /// Modalità conversazione: ogni sorgente ha la sua corsia, il microfono è "Tu" e l'audio di
    /// sistema viene diarizzato per dare un'etichetta a chi parla nella call.
    /// </summary>
    public bool ConversationMode { get; set; }

    /// <summary>Diarizzatore scelto (id di catalogo, vedi <see cref="Asr.NeMoModels.Diarizers"/>).</summary>
    public string DiarizerModelId { get; set; } = Asr.NeMoModels.DiarizerDefaultId;

    public string? SystemDeviceId { get; set; }

    public string? MicrophoneDeviceId { get; set; }

    public int TranslationPort { get; set; } = 8989;

    /// <summary>Architettura di traduzione (doppia passata, solo istantanea, solo qualità).</summary>
    public TranslationPipelineMode TranslationPipeline { get; set; } = TranslationPipelineMode.DualPass;

    /// <summary>
    /// Secondo stadio di traduzione: Auto lo attiva sulle macchine che lo reggono, Forzato lo
    /// impone, Spento lascia la sola corsia rapida.
    /// </summary>
    public QualityTranslationMode QualityTranslation { get; set; } = QualityTranslationMode.Auto;

    public int RetentionDays { get; set; }

    /// <summary>
    /// Scrive un log diagnostico della sessione (una riga JSON per aggiornamento): serve per
    /// misurare latenze e comportamento sul campo senza indovinare.
    /// </summary>
    public bool DiagnosticLog { get; set; }

    public string? DiagnosticLogPath { get; set; }
}
