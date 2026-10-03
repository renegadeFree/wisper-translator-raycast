using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WisperTranslator.Core.Templates;

/// <summary>
/// Template su disco: i pacchetti inclusi vengono materializzati la prima volta in
/// <c>%LOCALAPPDATA%\WisperTranslator\templates</c>, così si possono modificare a mano.
/// L'importazione accetta una cartella o uno zip con <c>template.json</c> oppure <c>SKILL.md</c>.
/// </summary>
public static class TemplateStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Ultimo avviso dell'importazione (costrutti non supportati e simili).</summary>
    public static string? LastWarning { get; private set; }

    /// <summary>Cartella dei template; i test la puntano a una cartella temporanea.</summary>
    public static string? RootOverride { get; set; }

    public static string Root => RootOverride ?? Path.Combine(AppPaths.Root, "templates");

    public static string MapsDirectory => Path.Combine(Root, "maps");

    public static string PdfsDirectory => Path.Combine(Root, "pdfs");

    /// <summary>Scrive i template inclusi la prima volta, senza ricrearli se l'utente li elimina.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(MapsDirectory);
        Directory.CreateDirectory(PdfsDirectory);

        var marker = Path.Combine(Root, ".inclusi-v1");
        if (File.Exists(marker))
        {
            return;
        }

        foreach (var template in BundledTemplates.Maps)
        {
            File.WriteAllText(Path.Combine(MapsDirectory, $"{template.Id}.json"), JsonSerializer.Serialize(template, Json));
        }

        foreach (var template in BundledTemplates.Pdfs)
        {
            File.WriteAllText(Path.Combine(PdfsDirectory, $"{template.Id}.json"), JsonSerializer.Serialize(template, Json));
        }

        File.WriteAllText(marker, DateTime.Now.ToString("O"));
    }

    public static IReadOnlyList<MapTemplate> Maps()
    {
        EnsureCreated();
        return [.. ReadAll<MapTemplate>(MapsDirectory).OrderBy(template => template.Name)];
    }

    public static IReadOnlyList<PdfTemplate> Pdfs()
    {
        EnsureCreated();
        return [.. ReadAll<PdfTemplate>(PdfsDirectory).OrderBy(template => template.Name)];
    }

    public static MapTemplate MapOrDefault(string? id) =>
        Maps().FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Maps().FirstOrDefault()
        ?? BundledTemplates.Maps[0];

    public static PdfTemplate PdfOrDefault(string? id) =>
        Pdfs().FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Pdfs().FirstOrDefault()
        ?? BundledTemplates.Pdfs[0];

    public static void Save(MapTemplate template)
    {
        EnsureCreated();
        File.WriteAllText(Path.Combine(MapsDirectory, $"{template.Id}.json"), JsonSerializer.Serialize(template, Json));
    }

    public static void Save(PdfTemplate template)
    {
        EnsureCreated();
        File.WriteAllText(Path.Combine(PdfsDirectory, $"{template.Id}.json"), JsonSerializer.Serialize(template, Json));
    }

    public static void Delete(MapTemplate template) => DeleteFile(MapsDirectory, template.Id);

    public static void Delete(PdfTemplate template) => DeleteFile(PdfsDirectory, template.Id);

    private static void DeleteFile(string directory, string id)
    {
        var path = Path.Combine(directory, $"{id}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Esporta un template in un file JSON scelto dall'utente.</summary>
    public static void Export(MapTemplate template, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(template, Json));

    public static void Export(PdfTemplate template, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(template, Json));

    /// <summary>
    /// Importa da una cartella o da uno zip. Ordine di lettura: <c>template.json</c>, poi
    /// <c>SKILL.md</c> (front-matter + istruzioni). Restituisce l'id importato.
    /// </summary>
    public static string Import(string path)
    {
        LastWarning = null;
        EnsureCreated();

        var temporary = false;
        var directory = path;
        if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            directory = Path.Combine(Path.GetTempPath(), $"wisper-template-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            ZipFile.ExtractToDirectory(path, directory, overwriteFiles: true);
            temporary = true;
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Cartella non trovata: {path}");
        }

        try
        {
            var jsonFile = FindFile(directory, "template.json");
            if (jsonFile is not null)
            {
                var text = File.ReadAllText(jsonFile);
                if (LooksLikePdf(text))
                {
                    var template = JsonSerializer.Deserialize<PdfTemplate>(text, Json)
                                   ?? throw new InvalidOperationException("template.json non leggibile come report PDF.");
                    Save(template with { Bundled = false });
                    return template.Id;
                }

                var parsedMap = JsonSerializer.Deserialize<MapTemplate>(text, Json)
                                ?? throw new InvalidOperationException("template.json non leggibile come mappa.");
                Save(parsedMap with { Bundled = false });
                return parsedMap.Id;
            }

            var skillFile = FindFile(directory, "SKILL.md") ?? FindFile(directory, "skill.md");
            if (skillFile is null)
            {
                throw new InvalidOperationException(
                    "Nella cartella non c'è né template.json né SKILL.md: niente da importare.");
            }

            var (meta, body) = ParseSkill(File.ReadAllText(skillFile));
            WarnAboutUnsupported(body);

            var name = Meta(meta, "name") ?? Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
            var description = Meta(meta, "description") ?? "Template importato.";
            var id = Slug(Meta(meta, "id") ?? name);
            var kind = (Meta(meta, "kind") ?? "map").ToLowerInvariant();

            if (kind is "pdf" or "report")
            {
                var template = new PdfTemplate(
                    id,
                    name,
                    description,
                    new PdfPageSettings(
                        Meta(meta, "page") ?? "A4",
                        string.Equals(Meta(meta, "orientation"), "landscape", StringComparison.OrdinalIgnoreCase)),
                    new PdfStyle(
                        Meta(meta, "palette") ?? "professionale",
                        Meta(meta, "font") ?? "Segoe UI",
                        ParseDouble(Meta(meta, "font_size"), 10.5),
                        !string.Equals(Meta(meta, "cover"), "false", StringComparison.OrdinalIgnoreCase),
                        TableOfContents: string.Equals(Meta(meta, "toc"), "true", StringComparison.OrdinalIgnoreCase)),
                    DefaultSections(body),
                    body,
                    Meta(meta, "source") ?? Meta(meta, "url"),
                    Meta(meta, "author"),
                    Meta(meta, "license") ?? "MIT");
                Save(template);
                return template.Id;
            }

            var map = new MapTemplate(
                id,
                name,
                description,
                ParseEnum(Meta(meta, "engine"), MapEngine.Dot),
                ParseEnum(Meta(meta, "orientation"), MapOrientation.LeftRight),
                new MapStyle(
                    ParseEnum(Meta(meta, "palette"), MapPalette.ScuroCaldo),
                    ParseEnum(Meta(meta, "shape"), MapNodeShape.Rounded),
                    ParseEnum(Meta(meta, "density"), MapDensity.Normale),
                    Meta(meta, "font") ?? "Segoe UI",
                    ParseDouble(Meta(meta, "font_size"), 11),
                    ShowEdgeLabels: !string.Equals(Meta(meta, "edge_labels"), "false", StringComparison.OrdinalIgnoreCase)),
                new MapLimits(
                    (int)ParseDouble(Meta(meta, "max_nodes"), 20),
                    (int)ParseDouble(Meta(meta, "max_depth"), 4),
                    (int)ParseDouble(Meta(meta, "max_words"), 6)),
                string.IsNullOrWhiteSpace(body) ? description : body,
                Meta(meta, "source") ?? Meta(meta, "url"),
                Meta(meta, "author"),
                Meta(meta, "license") ?? "MIT");
            Save(map);
            return map.Id;
        }
        finally
        {
            if (temporary)
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static IReadOnlyList<PdfSection> DefaultSections(string instructions) =>
    [
        new PdfSection("sintesi", "Sintesi", PdfSectionKind.Summary, MaxWords: 160),
        new PdfSection("punti", "Punti chiave", PdfSectionKind.KeyPoints),
        new PdfSection("approfondimento", "Approfondimento", PdfSectionKind.Ai, instructions, MaxWords: 300),
        new PdfSection("mappa", "Mappa concettuale", PdfSectionKind.Map, MapTemplateId: "gerarchica-lr"),
        new PdfSection("trascrizione", "Trascrizione", PdfSectionKind.Transcript, ShowOriginal: false),
    ];

    private static void WarnAboutUnsupported(string body)
    {
        foreach (var needle in new[] { "mermaid", "excalidraw", "lark", "feishu", "obsidian canvas" })
        {
            if (body.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                LastWarning =
                    $"Il template parla di \"{needle}\": quel formato non è supportato, il disegno avverrà "
                    + "comunque con Graphviz usando le istruzioni di contenuto.";
                return;
            }
        }
    }

    private static List<T> ReadAll<T>(string directory)
    {
        var items = new List<T>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) is { } item)
                {
                    items.Add(item);
                }
            }
            catch (JsonException)
            {
                // file rovinato: si ignora, gli altri template restano usabili
            }
        }

        return items;
    }

    private static bool LooksLikePdf(string json) =>
        json.Contains("\"sections\"", StringComparison.OrdinalIgnoreCase)
        || json.Contains("\"page\"", StringComparison.OrdinalIgnoreCase);

    private static string? FindFile(string directory, string name) =>
        Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).FirstOrDefault();

    /// <summary>Front-matter minimale: righe <c>chiave: valore</c> tra due righe <c>---</c>.</summary>
    internal static (Dictionary<string, string> Meta, string Body) ParseSkill(string text)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var index = 0;
        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
        {
            index++;
        }

        if (index >= lines.Length || lines[index].Trim() != "---")
        {
            return (meta, text.Trim());
        }

        index++;
        for (; index < lines.Length && lines[index].Trim() != "---"; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Length > 0)
            {
                meta[key] = value;
            }
        }

        var body = index < lines.Length ? string.Join('\n', lines[(index + 1)..]).Trim() : string.Empty;
        return (meta, body);
    }

    private static string? Meta(Dictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value?.Replace("-", string.Empty), ignoreCase: true, out var parsed) ? parsed : fallback;

    private static string Slug(string text)
    {
        var cleaned = new string([.. text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]);
        var collapsed = string.Join('-', cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > 0 ? collapsed : $"template-{Guid.NewGuid():N}"[..24];
    }
}
