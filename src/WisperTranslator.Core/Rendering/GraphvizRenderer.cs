using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Core.Rendering;

/// <summary>Misure in pollici della pagina PDF: riquadro utile e pagina intera.</summary>
public sealed record MapPageInches(
    double ContentWidth,
    double ContentHeight,
    double PageWidth,
    double PageHeight);

/// <summary>Disegna le mappe con Graphviz; se manca, si usa il renderer interno.</summary>
public static class GraphvizRenderer
{
    public static bool IsAvailable => GraphvizRuntime.IsInstalled;

    public static string EngineName(MapEngine engine) => engine switch
    {
        MapEngine.Neato => "neato",
        MapEngine.Fdp => "fdp",
        MapEngine.Sfdp => "sfdp",
        MapEngine.Twopi => "twopi",
        MapEngine.Circo => "circo",
        MapEngine.Osage => "osage",
        MapEngine.Patchwork => "patchwork",
        _ => "dot",
    };

    /// <summary>PNG per l'anteprima; null se Graphviz non è disponibile o fallisce.</summary>
    public static async Task<byte[]?> RenderPngAsync(
        ConceptMap map,
        MapTemplate template,
        CancellationToken cancellationToken = default) =>
        await GraphvizRuntime
            .RunAsync(DotGraphBuilder.Build(map, template), EngineName(template.Engine), "png", cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Pagina PDF vettoriale (una sola pagina) da importare nel report.</summary>
    public static async Task<bool> RenderPdfAsync(
        ConceptMap map,
        MapTemplate template,
        string outputPath,
        MapPageInches? pageInches = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = await GraphvizRuntime
            .RunAsync(DotGraphBuilder.Build(map, template, pageInches), EngineName(template.Engine), "pdf", cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllBytesAsync(outputPath, bytes, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Anteprima di ripiego senza Graphviz (stile interno).</summary>
    public static byte[] RenderFallbackPng(ConceptMap map) => ConceptMapRenderer.RenderPng(map);
}
