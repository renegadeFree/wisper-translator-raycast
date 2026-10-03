using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Rendering;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Tests;

/// <summary>
/// Il sorgente DOT deve essere sempre valido: accenti, virgolette, numeri con il punto
/// (con la cultura italiana diventerebbero "10,5") e nessun nodo inventato.
/// </summary>
public class DotGraphBuilderTests
{
    private static ConceptMap Map() => SampleContent.Map();

    [Fact]
    public void ProteggeVirgoletteAccentiEAcapo()
    {
        var escaped = DotGraphBuilder.Escape("Ciao \"mondo\"\nseconda riga àèìòù");

        Assert.Contains("\\\"mondo\\\"", escaped);
        Assert.Contains("\\n", escaped);
        Assert.Contains("àèìòù", escaped);
        Assert.DoesNotContain("\n", escaped);
    }

    [Fact]
    public void UsaIlPuntoPerINumeri()
    {
        var dot = DotGraphBuilder.Build(Map(), BundledTemplates.Maps[0]);

        Assert.Contains("nodesep=0.45;", dot);
        Assert.DoesNotContain(",45", dot);
    }

    [Fact]
    public void OrientamentoDeterminaLaDirezione()
    {
        var template = BundledTemplates.Map("gerarchica-lr")!;
        Assert.Contains("rankdir=LR;", DotGraphBuilder.Build(Map(), template));

        var topBottom = BundledTemplates.Map("albero-tb")!;
        Assert.Contains("rankdir=TB;", DotGraphBuilder.Build(Map(), topBottom));
    }

    [Fact]
    public void NonCreaNodiFittiziPerIGruppi()
    {
        var template = BundledTemplates.Map("swot")!;
        var dot = DotGraphBuilder.Build(Map(), template);

        // Le vecchie righe {rank=same; cluster_0; …} creavano nodi vuoti.
        Assert.DoesNotContain("rank=same; cluster", dot);
        Assert.DoesNotContain("label=\"cluster_", dot);
    }

    [Fact]
    public void ApplicaIlimitiDelTemplate()
    {
        var template = BundledTemplates.Map("swot")! with { Limits = new MapLimits(MaxNodes: 4, MaxDepth: 2, MaxLabelWords: 6) };
        var dot = DotGraphBuilder.Build(Map(), template);

        var nodes = dot
            .Split('\n')
            .Count(line => line.Contains("[label=") && !line.Contains("->") && !line.Contains("edge ["));
        Assert.InRange(nodes, 1, 4);
    }

    [Fact]
    public void SullaPaginaPdfNonUsaIlDpiNeIlTitoloProprio()
    {
        var dot = DotGraphBuilder.Build(
            Map(),
            BundledTemplates.Maps[0],
            new MapPageInches(6.97, 10.19, 8.27, 11.69));

        Assert.Contains("size=\"6.97,10.19!\";", dot);
        Assert.Contains("page=\"8.27,11.69\";", dot);
        Assert.DoesNotContain("dpi=", dot);
    }

    [Fact]
    public void MescolaIColori()
    {
        Assert.Equal("#ffffff", DotGraphBuilder.Mix("#ffffff", "#ffffff", 0.5));
        Assert.Equal("#000000", DotGraphBuilder.Mix("#000000", "#000000", 0.5));
        Assert.Equal("#808080", DotGraphBuilder.Mix("#ffffff", "#000000", 0.5));
    }
}
