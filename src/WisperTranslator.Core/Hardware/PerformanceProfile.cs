namespace WisperTranslator.Core.Hardware;

/// <summary>Profilo prestazioni scelto dall'utente (o dedotto dall'hardware).</summary>
public enum PerformancePreset
{
    Auto,
    Reattivo,
    Equilibrato,
    Qualita,
}

/// <summary>Motore ASR usato da una corsia della cascata.</summary>
public enum AsrBackend
{
    /// <summary>Whisper.cpp su CPU: sempre disponibile, nessun processo esterno.</summary>
    WhisperCpu,

    /// <summary>NeMo-Speech.cpp (Nemotron streaming / Parakeet) come processo locale.</summary>
    NeMoSpeech,

    /// <summary>Vosk: modello minuscolo con riconoscimento continuo, per i PC minimi.</summary>
    Vosk,
}

/// <summary>Runtime accelerato scaricato a parte; la CPU resta sempre la rete di sicurezza.</summary>
public enum GpuRuntime
{
    Nessuno,
    Vulkan,
    Cuda,
}

/// <summary>
/// Scelta concreta di motori e modelli per una sessione. Il profilo non è un vincolo:
/// se il motore scelto non è disponibile si ripiega su Whisper e lo si dice all'utente.
/// </summary>
public sealed record PerformanceProfile(
    PerformancePreset Preset,
    AsrBackend Live,
    AsrBackend Final,
    string LiveWhisperModelId,
    string FinalWhisperModelId,
    string Label,
    string Note)
{
    /// <summary>Meno di 8 GB o meno di 4 core: la priorità è non far arrancare la macchina.</summary>
    private static bool IsMinimal(HardwareProfile hardware) =>
        hardware.RamMegabytes < 8_500 || hardware.PhysicalCores < 4;

    private static bool IsCapable(HardwareProfile hardware) =>
        hardware.RamMegabytes >= 16_000
        && (hardware.Tier == HardwareTier.A
            || hardware.PhysicalCores >= 8);

    public static PerformancePreset Recommend(HardwareProfile hardware) =>
        IsMinimal(hardware) ? PerformancePreset.Reattivo
        : IsCapable(hardware) ? PerformancePreset.Qualita
        : PerformancePreset.Equilibrato;

    public static PerformanceProfile Resolve(PerformancePreset requested, HardwareProfile hardware)
    {
        var preset = requested == PerformancePreset.Auto ? Recommend(hardware) : requested;
        return preset switch
        {
            PerformancePreset.Reattivo => new PerformanceProfile(
                preset,
                AsrBackend.Vosk,
                AsrBackend.WhisperCpu,
                "whisper-base-q5_1",
                "whisper-base-q5_1",
                "Reattivo",
                "Vosk per il testo immediato, Whisper base per la frase definitiva. Circa 400 MB di RAM."),

            PerformancePreset.Qualita => new PerformanceProfile(
                preset,
                AsrBackend.NeMoSpeech,
                AsrBackend.NeMoSpeech,
                "whisper-base-q5_1",
                "whisper-small-q5_1",
                "Qualità",
                "Nemotron 3.5 streaming per entrambe le corsie: un solo modello, circa 0,9 GB di RAM, "
                + "qualità già superiore a Whisper small."),

            _ => new PerformanceProfile(
                PerformancePreset.Equilibrato,
                AsrBackend.NeMoSpeech,
                hardware.RamMegabytes >= 16_000 ? AsrBackend.NeMoSpeech : AsrBackend.WhisperCpu,
                "whisper-base-q5_1",
                "whisper-small-q5_1",
                "Equilibrato",
                hardware.RamMegabytes >= 16_000
                    ? "Nemotron streaming + Parakeet: massima velocità con qualità alta."
                    : "Nemotron streaming per il testo immediato, Whisper small per la frase definitiva."),
        };
    }
}
