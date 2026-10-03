namespace WisperTranslator.Core.Templates;

/// <summary>Motore di impaginazione Graphviz.</summary>
public enum MapEngine
{
    Dot,
    Neato,
    Fdp,
    Sfdp,
    Twopi,
    Circo,
    Osage,
    Patchwork,
}

/// <summary>Direzione/struttura della mappa.</summary>
public enum MapOrientation
{
    TopBottom,
    BottomTop,
    LeftRight,
    RightLeft,
    Radial,
    Timeline,
    Matrix,
    Fishbone,
}

public enum MapDensity
{
    Compatta,
    Normale,
    Ampia,
}

public enum MapNodeShape
{
    Box,
    Rounded,
    Ellipse,
    Pill,
    Note,
    Card,
}

public enum MapPalette
{
    ScuroCaldo,
    ScuroFreddo,
    ChiaroProfessionale,
    Pastello,
    StampaBiancoNero,
    AltoContrasto,
}

public sealed record MapStyle(
    MapPalette Palette = MapPalette.ScuroCaldo,
    MapNodeShape Shape = MapNodeShape.Rounded,
    MapDensity Density = MapDensity.Normale,
    string FontFamily = "Segoe UI",
    double FontSize = 11,
    bool ShowEdgeLabels = true,
    bool Arrows = true,
    bool GroupsAsClusters = true,
    bool Sketch = false);

public sealed record MapLimits(int MaxNodes = 20, int MaxDepth = 4, int MaxLabelWords = 6);

/// <summary>
/// Template di mappa concettuale: le istruzioni sono il "copione" che riceve il modello,
/// il resto dice a Graphviz come disegnare il risultato.
/// </summary>
public sealed record MapTemplate(
    string Id,
    string Name,
    string Description,
    MapEngine Engine,
    MapOrientation Orientation,
    MapStyle Style,
    MapLimits Limits,
    string Instructions,
    string? SourceUrl = null,
    string? Author = null,
    string License = "MIT",
    bool Bundled = false)
{
    public string Summary =>
        $"{Engine} · {Orientation} · {Style.Palette} · max {Limits.MaxNodes} nodi";
}
