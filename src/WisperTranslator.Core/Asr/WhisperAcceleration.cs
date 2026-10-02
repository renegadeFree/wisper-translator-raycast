using Whisper.net.LibraryLoader;

namespace WisperTranslator.Core.Asr;

/// <summary>Accelerazione richiesta per la trascrizione.</summary>
public enum AccelerationPreference
{
    /// <summary>Sceglie da sola in base all'hardware e alle librerie presenti.</summary>
    Auto,

    Cpu,

    Gpu,
}

/// <summary>
/// Prepara il runtime nativo di whisper.cpp. Whisper.net prova già da solo CUDA → Vulkan → CPU:
/// qui si decide solo **quali** build native mettere a disposizione, in base alla macchina.
/// Le build GPU vivono in sottocartelle (`tools\whisper\vulkan`, `tools\whisper\cuda`) così la
/// versione CPU resta come rete di sicurezza su PC senza GPU.
/// </summary>
public static class WhisperAcceleration
{
    private static readonly object Gate = new();
    private static string? _applied;

    /// <summary>
    /// Le build native stanno accanto all'eseguibile (installazione) oppure nella cartella utente
    /// (sviluppo e download a runtime), come per il server di traduzione.
    /// </summary>
    public static string? ResolveDirectory(string name)
    {
        foreach (var root in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "tools", "whisper"),
                     Path.Combine(Core.AppPaths.Root, "tools", "whisper"),
                 })
        {
            var candidate = Path.Combine(root, name);
            if (HasNativeLibraries(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? VulkanDirectory => ResolveDirectory("vulkan");

    public static string? CudaDirectory => ResolveDirectory("cuda");

    /// <summary>Cosa è stato caricato davvero (Cuda/Vulkan/Cpu) dopo la prima decodifica.</summary>
    public static string? LoadedLibrary => RuntimeOptions.LoadedLibrary?.ToString();

    /// <summary>Accelerazione disponibile su questa macchina e su questa installazione.</summary>
    public static (string Name, string? Directory) BestAvailable()
    {
        if (CudaDirectory is { } cuda && IsVerified(cuda))
        {
            return ("CUDA", cuda);
        }

        if (VulkanDirectory is { } vulkan && IsVerified(vulkan))
        {
            return ("Vulkan", vulkan);
        }

        return ("CPU", null);
    }

    public static bool HasNativeLibraries(string directory) =>
        Directory.Exists(directory)
        && File.Exists(Path.Combine(directory, "runtimes", "win-x64", "native", "whisper.dll"));

    /// <summary>
    /// Una build GPU si considera attivabile solo se è stata verificata sul campo: il caricatore
    /// di Whisper.net cerca `{LibraryPath}\runtimes\{rid}\native` con una convenzione propria
    /// (<c>RuntimePathResolver</c>) e finché non è confermata si resta sulla CPU, che funziona
    /// sempre. Il file <c>verified.txt</c> nella cartella del pacchetto abilita la build.
    /// </summary>
    public static bool IsVerified(string directory) =>
        HasNativeLibraries(directory) && File.Exists(Path.Combine(directory, "verified.txt"));

    /// <summary>
    /// Cartella da passare a <c>RuntimeOptions.LibraryPath</c>: la ricerca del caricatore è
    /// <c>{LibraryPath}\runtimes\{rid}\native</c> ed è la **prima** della lista, quindi una build
    /// GPU in questa posizione ha la precedenza sulla CPU copiata accanto all'eseguibile.
    /// </summary>
    public static string PackRoot(string directory) => directory;

    /// <summary>
    /// Applica la preferenza. Con <paramref name="preference"/> Auto usa il meglio disponibile,
    /// altrimenti forza CPU o la build GPU presente. Va chiamata prima di creare la factory.
    /// </summary>
    public static string Apply(AccelerationPreference preference = AccelerationPreference.Auto)
    {
        lock (Gate)
        {
            var (name, directory) = BestAvailable();
            var target = preference switch
            {
                AccelerationPreference.Cpu => ("CPU", null),
                AccelerationPreference.Gpu when directory is not null => (name, directory),
                AccelerationPreference.Gpu => ("CPU", null),
                _ => (name, directory),
            };

            var key = $"{target.Item1}|{target.Item2}";
            if (_applied == key)
            {
                return target.Item1;
            }

            _applied = key;
            RuntimeOptions.LibraryPath = target.Item2;
            RuntimeOptions.RuntimeLibraryOrder =
            [
                RuntimeLibrary.Cuda,
                RuntimeLibrary.Cuda12,
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.Cpu,
            ];

            return target.Item1;
        }
    }
}
