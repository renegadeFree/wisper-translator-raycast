namespace WisperTranslator.Core.Templates;

public enum PdfSectionKind
{
    /// <summary>Testo generato dall'IA con il prompt della sezione.</summary>
    Ai,

    /// <summary>Riassunto generato con il prompt standard.</summary>
    Summary,

    /// <summary>Elenco di punti chiave.</summary>
    KeyPoints,

    /// <summary>Mappa concettuale (vettoriale) con il template indicato.</summary>
    Map,

    /// <summary>Trascrizione, con o senza originale e timecode.</summary>
    Transcript,
}

public sealed record PdfSection(
    string Id,
    string Title,
    PdfSectionKind Kind,
    string Prompt = "",
    string MapTemplateId = "",
    int MaxWords = 180,
    bool ShowOriginal = true,
    bool ShowTimestamps = true);

public sealed record PdfPageSettings(string Size = "A4", bool Landscape = false, double Margin = 46);

public sealed record PdfStyle(
    string Palette = "professionale",
    string FontFamily = "Segoe UI",
    double FontSize = 10.5,
    bool Cover = true,
    bool Header = true,
    bool Footer = true,
    bool PageNumbers = true,
    bool TableOfContents = false);

/// <summary>Template di report PDF: pagina, stile e sequenza delle sezioni.</summary>
public sealed record PdfTemplate(
    string Id,
    string Name,
    string Description,
    PdfPageSettings Page,
    PdfStyle Style,
    IReadOnlyList<PdfSection> Sections,
    string Instructions = "",
    string? SourceUrl = null,
    string? Author = null,
    string License = "MIT",
    bool Bundled = false)
{
    public string Summary =>
        $"{Sections.Count} sezioni · {Page.Size}{(Page.Landscape ? " orizzontale" : " verticale")}"
        + (Style.Cover ? " · copertina" : string.Empty);
}
