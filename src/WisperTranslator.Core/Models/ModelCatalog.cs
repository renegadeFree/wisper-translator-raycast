namespace WisperTranslator.Core.Models;

public enum ModelRole { Asr, Vad, Translation, Diarization, Runtime }
public enum ModelPackaging { SingleFile, Zip, Runtime }

public sealed record ModelCatalogEntry(string Id, string DisplayName, ModelRole Role, string Tier,
    string Url, string FileName, string RelativePath, long ExpectedSizeBytes, string Sha256,
    string License, string About, ModelPackaging Packaging = ModelPackaging.SingleFile,
    bool ManagedByServer = false, string? RequiredFile = null);

/// <summary>Origini ufficiali e revisioni fissate; hash SHA-256 dei file, non dei puntatori LFS.</summary>
public static class ModelCatalog
{
    public static IReadOnlyList<ModelCatalogEntry> Asr { get; } =
    [
        new("whisper-base-q5_1", "Whisper base Q5", ModelRole.Asr, "C",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base-q5_1.bin",
            "ggml-base-q5_1.bin", "whisper-base-q5_1/ggml-base-q5_1.bin", 59707625,
            "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898", "MIT", "Trascrizione multilingue locale."),
        new("whisper-small-q5_1", "Whisper small Q5", ModelRole.Asr, "B",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small-q5_1.bin",
            "ggml-small-q5_1.bin", "whisper-small-q5_1/ggml-small-q5_1.bin", 190085487,
            "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb", "MIT", "Trascrizione multilingue locale."),
        new("whisper-large-v3-turbo-q5_0", "Whisper large v3 turbo Q5", ModelRole.Asr, "A",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin",
            "ggml-large-v3-turbo-q5_0.bin", "whisper-large-v3-turbo-q5_0/ggml-large-v3-turbo-q5_0.bin", 574041195,
            "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2", "MIT", "Trascrizione multilingue locale."),
    ];

    public static ModelCatalogEntry NemotronStreaming { get; } = new(
        "nemotron-3.5-asr-streaming-0.6b", "Nemotron 3.5 streaming", ModelRole.Asr, "B",
        "https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b/resolve/ea30d66debe3740a08b573244286791d423d6b3e/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf",
        "nemotron-3.5-asr-streaming-0.6b.q8_0.gguf", "nemotron-3.5-asr-streaming-0.6b/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf", 742090464,
        "3fc991d3badad7277c11030a7519832cddaf2057aafed6d4b25147e953a070b1", "OpenMDW-1.1", "Trascrizione multilingue in streaming.");

    public static ModelCatalogEntry NemotronDiarization { get; } = new(
        "nemotron-3-diarization", "Nemotron 3 Diarization", ModelRole.Diarization, "B",
        "https://huggingface.co/nvidia/Nemotron-3-Diarization/resolve/f667ed73aee57d40cc39428eb768b4fd87a0a29e/Nemotron-3-Diarization.q8_0.gguf",
        "Nemotron-3-Diarization.q8_0.gguf", "nemotron-3-diarization/Nemotron-3-Diarization.q8_0.gguf", 107012128,
        "08456d9e22cd9a323c0364d98375f3746d6e68507ebb705cd46438c534c7a3a1", "OpenMDW-1.1", "Fino a 8 parlanti.");

    public static ModelCatalogEntry SortformerDiarization { get; } = new(
        "diar-streaming-sortformer-4spk-v2", "Sortformer 4 parlanti v2", ModelRole.Diarization, "B",
        "https://huggingface.co/nvidia/diar_streaming_sortformer_4spk-v2/resolve/84edd514b8ef68004c10086918cd62f2148cbd59/diar_streaming_sortformer_4spk-v2.q8_0.gguf",
        "diar_streaming_sortformer_4spk-v2.q8_0.gguf", "diar-streaming-sortformer-4spk-v2/diar_streaming_sortformer_4spk-v2.q8_0.gguf", 147075776,
        "0679cfeb1ce356d0dea9470b31274f4bfc7eb927497d82005483770666da998a", "CC-BY-4.0", "Modello NVIDIA, attribuzione: nvidia/diar_streaming_sortformer_4spk-v2.");

    public static ModelCatalogEntry NeMoRuntime { get; } = new(
        "nemo-speech-runtime", "NeMo-Speech.cpp 0.2.0 CPU", ModelRole.Runtime, "B",
        "https://github.com/NVIDIA/NeMo-Speech.cpp/releases/download/v0.2.0/nemo-speech-0.2.0-windows-x86_64-cpu.zip", "nemo-speech-0.2.0-windows-x86_64-cpu.zip", "nemo-speech-0.2.0-windows-x86_64-cpu.zip", 5528418,
        "82c80451086e86194aba9af19f800da77cd66d1c28d993d0efceffd6cdb49bb8", "Apache-2.0", "Runtime Windows x64.", ModelPackaging.Runtime,
        RequiredFile: "nemo-speech.exe");

    public static ModelCatalogEntry GraphvizRuntime { get; } = new(
        "graphviz-runtime", "Graphviz 16.1.0", ModelRole.Runtime, "C",
        "https://gitlab.com/api/v4/projects/4207231/packages/generic/graphviz-releases/16.1.0/windows_10_cmake_Release_Graphviz-16.1.0-win64.zip",
        "graphviz-16.1.0-win64.zip", "graphviz-16.1.0-win64.zip", 9767733,
        "733e49626c492242eb8dca30ea627b6ead20710e207998c7933b4909d92d6abc", "EPL-1.0", "Mappe locali.",
        ModelPackaging.Runtime, RequiredFile: "dot.exe");

    public static ModelCatalogEntry Vad { get; } = new(
        "silero-vad-v5", "Silero VAD v5", ModelRole.Vad, "C",
        "https://raw.githubusercontent.com/snakers4/silero-vad/v5.1.2/src/silero_vad/data/silero_vad.onnx",
        "silero_vad.onnx", "silero-vad-v5/silero_vad.onnx", 2327524,
        "2623a2953f6ff3d2c1e61740c6cdb7168133479b267dfef114a4a3cc5bdd788f", "MIT", "Rilevamento voce locale.");

    public static ModelCatalogEntry VoskItalian { get; } = new(
        "vosk-model-small-it-0.22", "Vosk small italiano", ModelRole.Asr, "C",
        "https://alphacephei.com/vosk/models/vosk-model-small-it-0.22.zip",
        "vosk-model-small-it-0.22.zip", "vosk-model-small-it-0.22.zip", 49665141,
        "9ec65e75861d1c6c2e457cccd932705340dcdf233f5b239f00733b4de0bf3267", "Apache-2.0", "Modello Alpha Cephei.",
        ModelPackaging.Zip, RequiredFile: "final.mdl");

    public static ModelCatalogEntry VoskEnglish { get; } = new(
        "vosk-model-small-en-us-0.15", "Vosk small inglese", ModelRole.Asr, "C",
        "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip",
        "vosk-model-small-en-us-0.15.zip", "vosk-model-small-en-us-0.15.zip", 41205931,
        "30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498", "Apache-2.0", "Modello Alpha Cephei.",
        ModelPackaging.Zip, RequiredFile: "final.mdl");

    public static ModelCatalogEntry TranslationModels { get; } = new(
        "bergamot-it-en", "Bergamot IT ↔ EN", ModelRole.Translation, "C", "", "", "mt", 0, "",
        "MPL-2.0", "Modelli Mozilla gestiti e verificati da MTranServer.", ManagedByServer: true);

    public static IReadOnlyList<ModelCatalogEntry> All { get; } =
        [.. Asr, Vad, VoskItalian, VoskEnglish, NemotronStreaming,
         NemotronDiarization, SortformerDiarization, NeMoRuntime, GraphvizRuntime, TranslationModels];

    public static ModelCatalogEntry ById(string id) => All.FirstOrDefault(entry =>
        entry.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Modello sconosciuto: {id}", nameof(id));
}
