using System.IO.Compression;
using System.Text.Json;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Tests;

/// <summary>Importazione dei template/SKILL.md, salvataggio e limiti.</summary>
public class TemplateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"wisper-template-{Guid.NewGuid():N}");

    public TemplateStoreTests()
    {
        TemplateStore.RootOverride = _root;
        TemplateStore.EnsureCreated();
    }

    public void Dispose() => TemplateStore.RootOverride = null;

    [Fact]
    public void ScriveIPacchettiInclusiLaPrimaVolta()
    {
        Assert.True(TemplateStore.Maps().Count >= 14);
        Assert.True(TemplateStore.Pdfs().Count >= 8);
        Assert.Contains(TemplateStore.Maps(), template => template.Id == "radiale-classica");
    }

    [Fact]
    public void LeggeIlFrontMatterDiUnSkill()
    {
        var (meta, body) = TemplateStore.ParseSkill(
            """
            ---
            name: Mappa di prova
            kind: map
            engine: circo
            orientation: radial
            max_nodes: 9
            ---
            Istruzioni per il modello.
            """);

        Assert.Equal("Mappa di prova", meta["name"]);
        Assert.Equal("circo", meta["engine"]);
        Assert.Contains("Istruzioni", body);
    }

    [Fact]
    public void ImportaUnaCartellaConSkillMarkdown()
    {
        var folder = Path.Combine(_root, "import-skill");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "SKILL.md"),
            """
            ---
            name: Mappa importata
            description: Da uno skill su disco
            kind: map
            engine: twopi
            orientation: radial
            palette: pastello
            max_nodes: 12
            author: Prova
            license: MIT
            ---
            Costruisci una mappa con pochi rami e nomi brevi.
            """);

        var id = TemplateStore.Import(folder);

        var template = TemplateStore.Maps().First(item => item.Id == id);
        Assert.Equal("Mappa importata", template.Name);
        Assert.Equal(MapEngine.Twopi, template.Engine);
        Assert.Equal(12, template.Limits.MaxNodes);
        Assert.Contains("pochi rami", template.Instructions);
    }

    [Fact]
    public void AvvisaSeLoSkillParlaDiFormatiNonSupportati()
    {
        var folder = Path.Combine(_root, "import-mermaid");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "SKILL.md"),
            """
            ---
            name: Skill mermaid
            kind: map
            ---
            Genera un diagramma Mermaid con mindmap.
            """);

        TemplateStore.Import(folder);

        Assert.NotNull(TemplateStore.LastWarning);
        Assert.Contains("mermaid", TemplateStore.LastWarning!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportaUnTemplateJsonDaZip()
    {
        var zip = Path.Combine(_root, "template.zip");
        var payload = JsonSerializer.Serialize(
            BundledTemplates.Maps[0] with { Id = "zip-template", Name = "Da zip" },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("template.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(payload);
        }

        var id = TemplateStore.Import(zip);

        Assert.Equal("zip-template", id);
        Assert.Contains(TemplateStore.Maps(), template => template.Id == "zip-template");
    }

    [Fact]
    public void EsportaEImportaUnTemplatePdf()
    {
        var template = BundledTemplates.Pdf("verbale-riunione")!;
        var path = Path.Combine(_root, "verbale.json");
        TemplateStore.Export(template, path);

        var folder = Path.Combine(_root, "import-pdf");
        Directory.CreateDirectory(folder);
        File.Copy(path, Path.Combine(folder, "template.json"));
        var id = TemplateStore.Import(folder);

        var imported = TemplateStore.Pdfs().First(item => item.Id == id);
        Assert.Equal(template.Sections.Count, imported.Sections.Count);
        Assert.Equal("Decisioni prese", imported.Sections[2].Title);
    }
}
