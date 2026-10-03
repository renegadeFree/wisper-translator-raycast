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
}
