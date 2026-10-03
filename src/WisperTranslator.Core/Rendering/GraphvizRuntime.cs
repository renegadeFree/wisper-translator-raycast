using System.Diagnostics;
using WisperTranslator.Core.Models;

namespace WisperTranslator.Core.Rendering;

/// <summary>
/// Graphviz portatile (zip ufficiale 9 MB, licenza EPL-1.0): si scarica al primo uso nella
/// cartella tools e serve a impaginare le mappe. Se manca, il disegno usa il renderer interno.
/// </summary>
public static class GraphvizRuntime
{
    /// <summary>Ultimo messaggio di errore di Graphviz (utile per la diagnostica in UI).</summary>
    public static string? LastError { get; private set; }

    public static string? FindExecutable() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "tools", "graphviz", "bin", "dot.exe"))
            ? Path.Combine(AppContext.BaseDirectory, "tools", "graphviz", "bin", "dot.exe")
            : ModelStore.FindFile(ModelCatalog.GraphvizRuntime, "dot.exe");

    public static bool IsInstalled => FindExecutable() is not null;

    public static async Task<string> EnsureAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (FindExecutable() is { } existing)
        {
            return existing;
        }

        await ModelStore
            .EnsurePackAsync(ModelCatalog.GraphvizRuntime, progress, cancellationToken)
            .ConfigureAwait(false);

        return FindExecutable()
               ?? throw new InvalidOperationException("Il pacchetto Graphviz non contiene dot.exe.");
    }

    /// <summary>Esegue Graphviz: sorgente DOT su standard input, immagine su standard output.</summary>
    public static async Task<byte[]?> RunAsync(
        string dotSource,
        string engine,
        string format,
        CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable();
        if (executable is null)
        {
            LastError = "Graphviz non installato.";
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = null,
        };
        startInfo.ArgumentList.Add($"-K{engine}");
        startInfo.ArgumentList.Add($"-T{format}");

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            LastError = "Avvio di dot.exe non riuscito.";
            return null;
        }

        var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.StandardInput.WriteAsync(dotSource.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();

        await copy.ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var message = await error.ConfigureAwait(false);

        if (process.ExitCode == 0 && output.Length > 0)
        {
            LastError = null;
            return output.ToArray();
        }

        LastError = $"dot.exe ha restituito {process.ExitCode}: {message.Trim()}";
        return null;
    }
}
