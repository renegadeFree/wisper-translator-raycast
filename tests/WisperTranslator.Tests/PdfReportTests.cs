using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Rendering;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Tests;

/// <summary>Impaginazione del report: sezioni del template, mappa vettoriale e Markdown.</summary>
public class PdfReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wisper-pdf-{Guid.NewGuid():N}");

    public PdfReportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // file ancora aperto dal lettore PDF: la cartella temporanea resta
        }
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void CostruisceIlReportConLeSezioniDelTemplate()
    {
        var template = BundledTemplates.Pdf("verbale-riunione")!;
        var path = Path.Combine(_directory, "report.pdf");

        PdfReportBuilder.Build(path, Session(), Cues(), Note(), SampleContent.Map(), template, Sections(), null);

        Assert.True(File.Exists(path));
        using var reader = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        // copertina + almeno una pagina di contenuto
        Assert.InRange(reader.PageCount, 2, 8);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void ImportaLaMappaVettorialeComePagina()
    {
        // Il verbale ha una sezione mappa: la pagina vettoriale deve aggiungersi al report.
        var template = BundledTemplates.Pdf("verbale-riunione")!;
        var mapPage = Path.Combine(_directory, "mappa.pdf");
        CreatePdf(mapPage);

        var withMap = Path.Combine(_directory, "con-mappa.pdf");
        var withoutMap = Path.Combine(_directory, "senza-mappa.pdf");
        PdfReportBuilder.Build(withMap, Session(), Cues(), Note(), SampleContent.Map(), template, Sections(), mapPage);
        PdfReportBuilder.Build(withoutMap, Session(), Cues(), Note(), SampleContent.Map(), template, Sections(), null);

        using var imported = PdfReader.Open(withMap, PdfDocumentOpenMode.Import);
        using var raster = PdfReader.Open(withoutMap, PdfDocumentOpenMode.Import);

        // La mappa occupa una pagina propria e il testo riprende su una pagina nuova.
        Assert.True(
            imported.PageCount > raster.PageCount,
            $"con la mappa vettoriale il report deve avere più pagine ({imported.PageCount} vs {raster.PageCount})");
    }

    [Fact]
    public void IlMarkdownContieneLeSezioniGenerate()
    {
        var template = BundledTemplates.Pdf("verbale-riunione")!;
        var markdown = PdfReportBuilder.BuildMarkdown(Session(), Cues(), Note(), template, Sections());

        Assert.Contains("# Verbale di riunione", markdown);
        Assert.Contains("## Decisioni prese", markdown);
        Assert.Contains("## Trascrizione", markdown);
        Assert.Contains("First, let's see", markdown);
    }

    private static void CreatePdf(string path)
    {
        WindowsFontResolver.EnsureRegistered();
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(595.28);
        page.Height = XUnit.FromPoint(841.89);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawString("mappa di prova", new XFont("Segoe UI", 12), XBrushes.Black, 40, 40);
        document.Save(path);
    }

    private static HistorySession Session() =>
        new(7, DateTime.Now.AddMinutes(-10), DateTime.Now, "en", "it", 3);

    private static IReadOnlyList<HistoryCue> Cues() =>
    [
        new(1, 7, DateTime.Now, TimeSpan.Zero, TimeSpan.FromSeconds(5),
            "First, let's see how the machine handles the most demanding games.",
            "Per prima cosa vediamo come la macchina gestisce i giochi più esigenti.", true),
        new(2, 7, DateTime.Now, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
            "The fan is quiet and the case stays cool.",
            "La ventola è silenziosa e il case resta fresco.", true),
    ];

    private static SessionNote Note() =>
        new(7, "Ollama (locale)", "Riassunto di prova.", "• Primo punto\n• Secondo punto", string.Empty, DateTime.Now);

    private static Dictionary<string, string> Sections() => new()
    {
        ["partecipanti"] = "Partecipanti di prova.",
        ["decisioni"] = "Decisioni di prova.",
        ["azioni"] = "Azioni di prova.",
    };
}
