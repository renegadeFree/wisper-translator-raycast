using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace WisperTranslator.Core.Rendering;

/// <summary>
/// PdfSharp 6 non usa i font di sistema da solo: questo resolver legge i TTF dalla cartella
/// Fonts di Windows. Serve per avere testo accentato corretto nei PDF (à, è, ù…).
/// </summary>
public sealed class WindowsFontResolver : IFontResolver
{
    private static readonly Dictionary<string, string> FamilyFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI"] = "segoeui",
        ["Arial"] = "arial",
        ["Calibri"] = "calibri",
        ["Consolas"] = "consola",
        ["Georgia"] = "georgia",
        ["Times New Roman"] = "times",
        ["Verdana"] = "verdana",
        ["Tahoma"] = "tahoma",
    };

    private static readonly Dictionary<string, string> FallbackFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI"] = "arial",
    };

    private readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private readonly string _fontsFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var family = FamilyFiles.ContainsKey(familyName) ? familyName : "Segoe UI";
        var suffix = (isBold, isItalic) switch
        {
            (true, true) => "z",
            (true, false) => "b",
            (false, true) => "i",
            _ => string.Empty,
        };

        return new FontResolverInfo($"{family}#{suffix}");
    }

    public byte[]? GetFont(string faceName)
    {
        return _cache.GetOrAdd(faceName, key =>
        {
            var parts = key.Split('#');
            var family = parts[0];
            var suffix = parts.Length > 1 ? parts[1] : string.Empty;

            if (TryRead(FamilyFiles.GetValueOrDefault(family, "arial") + suffix, out var bytes))
            {
                return bytes;
            }

            // alcuni font usano nomi diversi per italic/bold-italic
            var alternative = suffix switch
            {
                "i" => "i",
                "z" => "z",
                "b" => "bd",
                _ => string.Empty,
            };

            if (TryRead(FamilyFiles.GetValueOrDefault(family, "arial") + alternative, out bytes))
            {
                return bytes;
            }

            if (FallbackFiles.TryGetValue(family, out var fallback)
                && TryRead(fallback + suffix, out bytes))
            {
                return bytes;
            }

            if (TryRead("arial", out bytes))
            {
                return bytes;
            }

            throw new InvalidOperationException($"Nessun font utilizzabile trovato in {_fontsFolder}.");
        });
    }

    private bool TryRead(string baseName, out byte[] bytes)
    {
        foreach (var extension in new[] { ".ttf", ".TTF", ".otf" })
        {
            var path = Path.Combine(_fontsFolder, baseName + extension);
            if (!File.Exists(path))
            {
                continue;
            }

            bytes = File.ReadAllBytes(path);
            return true;
        }

        bytes = [];
        return false;
    }

    /// <summary>Registra il resolver una sola volta per processo (PdfSharp non permette di cambiarlo).</summary>
    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new WindowsFontResolver();
        }
    }
}
