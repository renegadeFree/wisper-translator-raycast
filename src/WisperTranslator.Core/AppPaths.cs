namespace WisperTranslator.Core;

/// <summary>Percorsi di lavoro dell'applicazione (nessun permesso amministrativo richiesto).</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WisperTranslator");

    public static string ModelsDirectory => Path.Combine(Root, "models");

    public static string AudioDumpDirectory => Path.Combine(Root, "audio-dump");

    public static string TestClipDirectory => Path.Combine(Root, "test-clips");

    public static string VadModelPath => Path.Combine(ModelsDirectory, "vad", "silero-vad-v5", "silero_vad.onnx");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ModelsDirectory);
    }

    public static string EnsureSubdirectory(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
