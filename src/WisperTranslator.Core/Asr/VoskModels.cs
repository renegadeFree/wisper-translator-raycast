using WisperTranslator.Core.Models;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Modelli Vosk per il riconoscimento continuo sui PC minimi: piccoli (34-50 MB),
/// Apache-2.0, pensati per girare su CPU deboli.
/// </summary>
public static class VoskModels
{
    public static string ModelId(string? language) =>
        (language ?? "it").StartsWith("it", StringComparison.OrdinalIgnoreCase)
            ? "vosk-model-small-it-0.22"
            : "vosk-model-small-en-us-0.15";

    private static ModelCatalogEntry Entry(string? language) => ModelCatalog.ById(ModelId(language));

    public static string DirectoryFor(string? language) => ModelStore.PackDirectory(Entry(language));

    public static bool IsInstalled(string? language) => ModelStore.IsPackInstalled(Entry(language));

    public static long InstalledSize(string? language) => ModelStore.DirectorySize(DirectoryFor(language));

    /// <summary>Scarica, verifica ed estrae il modello della lingua richiesta (una volta sola).</summary>
    public static Task<string> EnsureAsync(
        string? language,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        ModelStore.EnsurePackAsync(Entry(language), progress, cancellationToken);

    /// <summary>Elimina i modelli Vosk scaricati.</summary>
    public static void Delete()
    {
        ModelStore.DeletePack(ModelCatalog.VoskItalian);
        ModelStore.DeletePack(ModelCatalog.VoskEnglish);
    }
}
