using System.Text.Json;

namespace WisperTranslator.Core.Ai;

public sealed record ConceptNode(string Id, string Label, string? Group = null);

public sealed record ConceptEdge(string From, string To, string? Label = null);

public sealed record ConceptMap(string Title, IReadOnlyList<ConceptNode> Nodes, IReadOnlyList<ConceptEdge> Edges)
{
    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>
    /// Legge la mappa dal JSON prodotto dal modello. I modelli aggiungono spesso testo attorno
    /// al JSON o i fence di markdown: si prova prima il parsing diretto, poi il primo blocco
    /// `{...}`. Se non è una mappa valida si restituisce una mappa vuota, senza far fallire nulla.
    /// </summary>
    public static ConceptMap Parse(string? payload, string fallbackTitle = "Mappa concettuale")
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new ConceptMap(fallbackTitle, [], []);
        }

        var json = ExtractJson(payload);
        if (json is null)
        {
            return new ConceptMap(fallbackTitle, [], []);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var title = TryGet(root, "title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() ?? fallbackTitle
                : fallbackTitle;

            var nodes = new List<ConceptNode>();
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (TryGet(root, "nodes", out var nodesElement) && nodesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodesElement.EnumerateArray())
                {
                    var id = ReadString(node, "id") ?? ReadString(node, "label");
                    var label = ReadString(node, "label") ?? id;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label) || !nodeIds.Add(id))
                    {
                        continue;
                    }

                    nodes.Add(new ConceptNode(id!, label!, ReadString(node, "group")));
                }
            }

            var edges = new List<ConceptEdge>();
            if (TryGet(root, "edges", out var edgesElement) && edgesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edgesElement.EnumerateArray())
                {
                    var from = ReadString(edge, "from") ?? ReadString(edge, "source");
                    var to = ReadString(edge, "to") ?? ReadString(edge, "target");
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                    {
                        continue;
                    }

                    if (!nodeIds.Contains(from) || !nodeIds.Contains(to))
                    {
                        continue;
                    }

                    edges.Add(new ConceptEdge(from!, to!, ReadString(edge, "label")));
                }
            }

            return new ConceptMap(title, nodes, edges);
        }
        catch (JsonException)
        {
            return new ConceptMap(fallbackTitle, [], []);
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        TryGet(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Number
                ? value.ToString()
                : null;

    /// <summary>
    /// Cerca una proprietà ignorando maiuscole/minuscole: il JSON salvato dal programma usa i nomi
    /// PascalCase, quello suggerito ai modelli è camelCase, ed entrambi devono funzionare.
    /// </summary>
    private static bool TryGet(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    value = candidate.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ExtractJson(string payload)
    {
        var trimmed = payload.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        var start = trimmed.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var end = trimmed.LastIndexOf('}');
        if (end <= start)
        {
            return null;
        }

        _ = fenceStart;
        return trimmed[start..(end + 1)];
    }
}
