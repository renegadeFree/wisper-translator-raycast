using System.Text;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Core.Rendering;

/// <summary>
/// Traduce una mappa concettuale nel linguaggio DOT di Graphviz applicando il template:
/// motore, orientamento, palette, forme, densità, gruppi come cluster e limiti di dimensione.
/// </summary>
public static class DotGraphBuilder
{
    /// <param name="pdfPage">
    /// Se indicata, la mappa viene scalata nel riquadro utile e impaginata su una pagina
    /// della misura esatta del report (mappa vettoriale pronta da importare).
    /// </param>
    public static string Build(ConceptMap map, MapTemplate template, MapPageInches? pdfPage = null)
    {
        var colors = MapPalettes.For(template.Style.Palette);
        var nodes = Trim(map, template.Limits);
        var ids = nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = map.Edges
            .Where(edge => ids.Contains(edge.From) && ids.Contains(edge.To) && !edge.From.Equals(edge.To, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var (nodesep, ranksep) = template.Style.Density switch
        {
            MapDensity.Compatta => (0.28, 0.42),
            MapDensity.Ampia => (0.7, 0.95),
            _ => (0.45, 0.65),
        };

        var builder = new StringBuilder();
        builder.AppendLine("digraph mappa {");
        builder.AppendLine("  charset=\"UTF-8\";");
        builder.AppendLine("  newrank=true;");
        builder.AppendLine($"  bgcolor=\"{colors.Background}\";");
        // Il dpi serve solo al PNG: su PDF altera la scala e manda a monte l'adattamento alla pagina.
        if (pdfPage is null)
        {
            builder.AppendLine($"  dpi={PreviewDpi.ToString(System.Globalization.CultureInfo.InvariantCulture)};");
        }
        builder.AppendLine($"  fontname=\"{Escape(template.Style.FontFamily)}\";");
        builder.AppendLine($"  rankdir={RankDirection(template.Orientation)};");
        builder.AppendLine($"  nodesep={Num(nodesep)};");
        builder.AppendLine($"  ranksep={Num(ranksep)};");
        builder.AppendLine($"  splines={Splines(template)};");

        if (pdfPage is { } page)
        {
            builder.AppendLine(
                $"  size=\"{Num(page.ContentWidth)},{Num(page.ContentHeight)}!\"; "
                + $"page=\"{Num(page.PageWidth)},{Num(page.PageHeight)}\";");
        }

        // Nel report il titolo è già l'intestazione della sezione: sulla pagina vettoriale non serve.
        if (pdfPage is null && !string.IsNullOrWhiteSpace(map.Title))
        {
            builder.AppendLine(
                $"  label={Quote(map.Title)}; labelloc=t; fontsize={FontSize(template, +5)}; "
                + $"fontcolor=\"{colors.Title}\";");
        }

        var shape = Shape(template.Style.Shape);
        var style = NodeStyle(template.Style.Shape);
        builder.AppendLine(
            $"  node [shape={shape}, style=\"{style}\", fillcolor=\"{colors.NodeFill}\", color=\"{colors.NodeBorder}\", "
            + $"fontcolor=\"{colors.Text}\", fontname=\"{Escape(template.Style.FontFamily)}\", "
            + $"fontsize={FontSize(template, 0)}, penwidth=1.4, margin=\"0.14,0.09\"];");
        builder.AppendLine(
            $"  edge [color=\"{colors.Edge}\", fontcolor=\"{colors.Edge}\", "
            + $"fontname=\"{Escape(template.Style.FontFamily)}\", fontsize={FontSize(template, -1)}, "
            + $"arrowsize=0.7, penwidth=1.1{(template.Style.Arrows ? string.Empty : ", dir=none")}];");

        var groups = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Group))
            .GroupBy(node => node.Group!, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var loose = nodes.Where(node => string.IsNullOrWhiteSpace(node.Group)).ToList();

        if (template.Style.GroupsAsClusters && groups.Count > 0)
        {
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var accent = colors.Groups[index % colors.Groups.Count];
                // Il riempimento è una tinta del colore del gruppo mescolata con lo sfondo:
                // il testo resta leggibile, il bordo tiene il colore pieno.
                var fill = Mix(accent, colors.Background, 0.72);
                var border = Mix(accent, colors.Text, 0.45);
                builder.AppendLine($"  subgraph cluster_{index} {{");
                builder.AppendLine($"    label={Quote(group.Key)};");
                builder.AppendLine($"    fontcolor=\"{colors.Text}\"; fontname=\"{Escape(template.Style.FontFamily)}\";");
                builder.AppendLine($"    fontsize={FontSize(template, -0.5)}; color=\"{border}\"; style=\"rounded\"; penwidth=1.6;");
                foreach (var node in group)
                {
                    builder.AppendLine(
                        $"    {NodeId(node.Id)} [label={Quote(node.Label)}, fillcolor=\"{fill}\", color=\"{border}\"];");
                }

                builder.AppendLine("  }");
            }
        }
        else
        {
            for (var index = 0; index < groups.Count; index++)
            {
                var fill = colors.Groups[index % colors.Groups.Count];
                foreach (var node in groups[index])
                {
                    builder.AppendLine(
                        $"  {NodeId(node.Id)} [label={Quote(node.Label)}, "
                        + $"fillcolor=\"{Mix(fill, colors.Background, 0.72)}\", color=\"{Mix(fill, colors.Text, 0.45)}\"];");
                }
            }
        }

        foreach (var node in loose)
        {
            builder.AppendLine($"  {NodeId(node.Id)} [label={Quote(node.Label)}];");
        }

        foreach (var edge in edges)
        {
            var label = template.Style.ShowEdgeLabels && !string.IsNullOrWhiteSpace(edge.Label)
                ? $" [label={Quote(edge.Label!)}]"
                : string.Empty;
            builder.AppendLine($"  {NodeId(edge.From)} -> {NodeId(edge.To)}{label};");
        }

        AppendOrientationHelpers(builder, template, nodes, edges);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private const int PreviewDpi = 120;

    /// <summary>
    /// Casi che Graphviz non disegna da sé: matrice 2×2 (gruppi a coppie di ranghi) e
    /// spina di pesce (effetto all'estremità destra).
    /// </summary>
    private static void AppendOrientationHelpers(
        StringBuilder builder,
        MapTemplate template,
        List<ConceptNode> nodes,
        List<ConceptEdge> edges)
    {
        if (template.Orientation == MapOrientation.Matrix && template.Style.GroupsAsClusters)
        {
            // I cluster si dispongono orizzontalmente (rankdir=LR): quattro gruppi danno
            // una griglia leggibile senza nodi fittizi.
            builder.AppendLine("  ranksep=0.9;");
        }

        if (template.Orientation == MapOrientation.Radial && template.Engine == MapEngine.Twopi)
        {
            var withIncoming = edges.Select(edge => edge.To).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var root = nodes.FirstOrDefault(node => !withIncoming.Contains(node.Id)) ?? nodes.FirstOrDefault();
            if (root is not null)
            {
                builder.AppendLine($"  root={NodeId(root.Id)};");
            }

            builder.AppendLine("  overlap=false;");
        }

        if (template.Orientation == MapOrientation.Fishbone && nodes.Count > 0)
        {
            var withOutgoing = edges.Select(edge => edge.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var effect = nodes.LastOrDefault(node => !withOutgoing.Contains(node.Id)) ?? nodes[^1];
            builder.AppendLine($"  {{rank=sink; {NodeId(effect.Id)};}}");
        }

        if (template.Orientation == MapOrientation.Timeline && nodes.Count > 1)
        {
            // La sequenza è già data dagli archi: si allarga la distanza fra i passi.
            builder.AppendLine("  ranksep=1.1;");
        }
    }

    /// <summary>Applica i limiti del template: numero di nodi e profondità massima.</summary>
    private static List<ConceptNode> Trim(ConceptMap map, MapLimits limits)
    {
        var byId = map.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var incoming = map.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in map.Edges)
        {
            if (incoming.ContainsKey(edge.To))
            {
                incoming[edge.To]++;
            }
        }

        var roots = map.Nodes.Where(node => incoming[node.Id] == 0).ToList();
        if (roots.Count == 0 && map.Nodes.Count > 0)
        {
            roots.Add(map.Nodes[0]);
        }

        var selected = new List<ConceptNode>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(ConceptNode Node, int Depth)>();
        foreach (var root in roots)
        {
            queue.Enqueue((root, 0));
        }

        while (queue.Count > 0 && selected.Count < Math.Max(1, limits.MaxNodes))
        {
            var (node, depth) = queue.Dequeue();
            if (!seen.Add(node.Id))
            {
                continue;
            }

            selected.Add(node);
            if (depth >= Math.Max(1, limits.MaxDepth))
            {
                continue;
            }

            foreach (var edge in map.Edges.Where(edge => edge.From.Equals(node.Id, StringComparison.OrdinalIgnoreCase)))
            {
                if (byId.TryGetValue(edge.To, out var child) && !seen.Contains(child.Id))
                {
                    queue.Enqueue((child, depth + 1));
                }
            }
        }

        foreach (var node in map.Nodes.Where(node => !seen.Contains(node.Id)))
        {
            if (selected.Count >= Math.Max(1, limits.MaxNodes))
            {
                break;
            }

            selected.Add(node);
        }

        return selected;
    }

    private static string RankDirection(MapOrientation orientation) => orientation switch
    {
        MapOrientation.LeftRight or MapOrientation.Timeline or MapOrientation.Fishbone or MapOrientation.Matrix => "LR",
        MapOrientation.RightLeft => "RL",
        MapOrientation.BottomTop => "BT",
        _ => "TB",
    };

    private static string Splines(MapTemplate template) =>
        template.Orientation is MapOrientation.Matrix or MapOrientation.Fishbone ? "ortho" : "spline";

    private static string Shape(MapNodeShape shape) => shape switch
    {
        MapNodeShape.Ellipse => "ellipse",
        MapNodeShape.Note => "note",
        MapNodeShape.Pill => "box",
        _ => "box",
    };

    private static string NodeStyle(MapNodeShape shape) => shape switch
    {
        MapNodeShape.Note => "filled",
        MapNodeShape.Box => "filled",
        _ => "filled,rounded",
    };

    /// <summary>I numeri in DOT usano sempre il punto: con la cultura italiana diventerebbero "10,5".</summary>
    private static string Num(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string FontSize(MapTemplate template, double delta) =>
        Num(Math.Max(7, template.Style.FontSize + delta));

    private static string NodeId(string id) =>
        "n" + Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(id)))[..10];

    private static string Quote(string text) => $"\"{Escape(text)}\"";

    /// <summary>Mescola due colori #RRGGBB: <paramref name="amount"/> è quanto pesa il secondo.</summary>
    internal static string Mix(string first, string second, double amount)
    {
        if (first.Length != 7 || second.Length != 7 || first[0] != '#' || second[0] != '#')
        {
            return first;
        }

        try
        {
            var a = Convert.ToInt32(first[1..], 16);
            var b = Convert.ToInt32(second[1..], 16);
            var mix = 0;
            for (var shift = 0; shift <= 16; shift += 8)
            {
                var ca = (a >> shift) & 0xFF;
                var cb = (b >> shift) & 0xFF;
                var value = (int)Math.Round(ca * (1 - amount) + cb * amount);
                mix |= Math.Clamp(value, 0, 255) << shift;
            }

            return $"#{mix:x6}";
        }
        catch (Exception)
        {
            return first;
        }
    }

    /// <summary>Escape per DOT: virgolette, backslash e a capo; gli accenti restano UTF-8.</summary>
    internal static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", string.Empty)
        .Replace("\n", "\\n");
}
