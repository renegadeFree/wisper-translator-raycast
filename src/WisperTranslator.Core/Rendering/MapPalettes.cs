using WisperTranslator.Core.Templates;

namespace WisperTranslator.Core.Rendering;

/// <summary>Colori di una mappa: sfondo, nodi, testo, archi e tinte per i gruppi.</summary>
public sealed record MapColors(
    string Background,
    string NodeFill,
    string NodeBorder,
    string Text,
    string Edge,
    string Title,
    IReadOnlyList<string> Groups);

public static class MapPalettes
{
    public static MapColors For(MapPalette palette) => palette switch
    {
        MapPalette.ScuroFreddo => new(
            "#141821", "#1f2733", "#5ac8fa", "#f3f6fa", "#8fa3b8", "#7fd4ff",
            ["#2f6fed", "#00a3a3", "#7b61ff", "#2f9e44", "#c2255c", "#f08c00"]),

        MapPalette.ChiaroProfessionale => new(
            "#ffffff", "#f4f6f9", "#3b6ea5", "#141821", "#9aa7b4", "#1f4e79",
            ["#dbe7f5", "#e3f2e1", "#fde9d9", "#f1e0f0", "#e6e6fa", "#fff2cc"]),

        MapPalette.Pastello => new(
            "#fffdf8", "#fff3e0", "#e0a458", "#3b2f2f", "#c9b8a8", "#c98a3c",
            ["#ffe0b2", "#dcedc8", "#b3e5fc", "#f8bbd0", "#e1bee7", "#fff9c4"]),

        MapPalette.StampaBiancoNero => new(
            "#ffffff", "#ffffff", "#000000", "#000000", "#333333", "#000000",
            ["#ffffff", "#f2f2f2", "#e6e6e6", "#d9d9d9", "#cccccc", "#bfbfbf"]),

        MapPalette.AltoContrasto => new(
            "#000000", "#101010", "#ffd400", "#ffffff", "#ffd400", "#ffd400",
            ["#ffd400", "#00d1ff", "#ff5c8a", "#7cff6b", "#ff9f1c", "#c084fc"]),

        _ => new(
            "#18181c", "#26262c", "#ff8c3a", "#ffffff", "#b0a0a8", "#ff9c46",
            ["#ff8c3a", "#4ecdc4", "#ffd166", "#ef476f", "#8ac926", "#a78bfa"]),
    };
}
