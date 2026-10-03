namespace WisperTranslator.Core.Models;

public sealed record AsrModelSpec(string Id, string DisplayName, string Note);

public static class AsrModels
{
    public static AsrModelSpec Base { get; } = new("whisper-base-q5_1", "Whisper base · Q5", "Leggero, 57 MB");
    public static AsrModelSpec Small { get; } = new("whisper-small-q5_1", "Whisper small · Q5", "Accurato, 181 MB");
    public static AsrModelSpec Turbo { get; } = new("whisper-large-v3-turbo-q5_0", "Whisper large v3 turbo · Q5", "548 MB");
    public static IReadOnlyList<AsrModelSpec> All { get; } = [Base, Small, Turbo];

    public static AsrModelSpec FromId(string? id) => id?.ToLowerInvariant() switch
    {
        "base" or "whisper-base-q5_1" => Base,
        "small" or "whisper-small-q5_1" => Small,
        "turbo" or "large-v3-turbo" or "whisper-large-v3-turbo-q5_0" => Turbo,
        _ => Base,
    };
}

public static class InstalledModel
{
    public static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):F1} MB" : $"{bytes / 1024.0:F1} KB";
}
