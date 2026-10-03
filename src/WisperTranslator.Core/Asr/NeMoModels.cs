using WisperTranslator.Core.Models;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Modelli GGUF per NeMo-Speech.cpp. Nemotron 3.5 ASR Streaming (0,6 B, q8_0) copre
/// 40 lingue compreso l'italiano, lavora a chunk da 160 ms e pesa circa 708 MB.
/// </summary>
public static class NeMoModels
{
    static NeMoModels() =>
        // Alla prima lettura il modello ancora nel vecchio percorso viene spostato:
        // così l'app lo vede subito come installato, senza riscaricare 708 MB.
        MigrateFromLegacyLayout();

    private static ModelCatalogEntry Entry => ModelCatalog.NemotronStreaming;

    public const string StreamingId = "nemotron-3.5-asr-streaming-0.6b";

    public static string StreamingFileName => Entry.FileName;

    public static string StreamingPath => ModelStore.PathFor(Entry);

    public static bool IsInstalled => ModelStore.IsInstalled(Entry);

    public static long InstalledSize => ModelStore.InstalledSize(Entry);

    public static Task<string> EnsureAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        ModelStore.EnsureAsync(Entry, progress: null, cancellationToken);

    /// <summary>
    /// La v1.1 salvava il modello in <c>models\nemo</c>: spostarlo evita di riscaricare 708 MB.
    /// </summary>
    private static void MigrateFromLegacyLayout()
    {
        if (IsInstalled)
        {
            return;
        }

        var legacy = Path.Combine(AppPaths.ModelsDirectory, "nemo", Entry.FileName);
        if (!File.Exists(legacy))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StreamingPath)!);
            File.Move(legacy, StreamingPath, overwrite: true);
        }
        catch (IOException)
        {
            // se non si riesce, si scarica di nuovo
        }
    }

    public static void Delete() => ModelStore.Delete(Entry);

    /// <summary>Diarizzatore predefinito: 8 parlanti, 107 MB, licenza OpenMDW-1.1.</summary>
    public const string DiarizerDefaultId = "nemotron-3-diarization";

    public static IReadOnlyList<ModelCatalogEntry> Diarizers { get; } =
        [ModelCatalog.NemotronDiarization, ModelCatalog.SortformerDiarization];

    /// <summary>La voce di catalogo del diarizzatore scelto (il predefinito se l'id è ignoto).</summary>
    public static ModelCatalogEntry DiarizerEntry(string? id) =>
        Diarizers.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? ModelCatalog.NemotronDiarization;

    public static bool IsDiarizerInstalled(string? id) => ModelStore.IsInstalled(DiarizerEntry(id));

    public static long DiarizerSize(string? id) => ModelStore.InstalledSize(DiarizerEntry(id));

    /// <summary>Percorso del diarizzatore, solo se già installato (non fa partire download).</summary>
    public static string? DiarizerPathIfInstalled(string? id)
    {
        var entry = DiarizerEntry(id);
        var path = ModelStore.PathFor(entry);
        return File.Exists(path) ? path : null;
    }

    public static Task<string> EnsureDiarizerAsync(
        string? id,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        ModelStore.EnsureAsync(DiarizerEntry(id), progress, cancellationToken);

    public static void DeleteDiarizer(string? id) => ModelStore.Delete(DiarizerEntry(id));
}
