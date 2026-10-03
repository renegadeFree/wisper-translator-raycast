using WisperTranslator.Core.Models;
using WisperTranslator.Core.Hardware;

namespace WisperTranslator.Core.Session;

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

    public string SourceLanguage { get; set; } = "it";

    public string TargetLanguage { get; set; } = "en";

    public bool Translate { get; set; } = true;

    public bool SystemAudio { get; set; } = true;

    public bool Microphone { get; set; }

    public string? SystemDeviceId { get; set; }

    public string? MicrophoneDeviceId { get; set; }

    public int TranslationPort { get; set; } = 8989;

    /// <summary>
    /// Scrive un log diagnostico della sessione (una riga JSON per aggiornamento): serve per
    /// misurare latenze e comportamento sul campo senza indovinare.
    /// </summary>
    public bool DiagnosticLog { get; set; }

    public string? DiagnosticLogPath { get; set; }
}
