using System.Globalization;
using System.Text;

namespace WisperTranslator.Cli;

/// <summary>Appende i risultati misurati in docs/BENCHMARKS.md.</summary>
internal static class BenchmarkWriter
{
    private const string Header =
        """
        # Benchmark

        Misure reali, non stime. Ogni riga è prodotta da `WisperTranslator.Cli bench`.

        RTF = tempo di calcolo / durata dell'audio (più basso è meglio, < 1 è più veloce del tempo reale).

        | Data | Macchina | Modello | Lingua | Durata audio | Tempo | RTF | RAM picco | Accuratezza |
        |---|---|---|---|---|---|---|---|---|
        """;

    public static void Append(string projectRoot, BenchmarkRow row)
    {
        var path = Path.Combine(projectRoot, "docs", "BENCHMARKS.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var builder = new StringBuilder();
        if (!File.Exists(path))
        {
            builder.AppendLine(Header);
        }

        builder.AppendLine(row.ToMarkdownRow());
        File.AppendAllText(path, builder.ToString());
    }
}

internal sealed record BenchmarkRow(
    string Machine,
    string Model,
    string Language,
    double AudioSeconds,
    double ElapsedSeconds,
    double RealTimeFactor,
    long PeakMemoryMb,
    double? Accuracy)
{
    public string ToMarkdownRow()
    {
        var accuracy = Accuracy is null ? "n/d" : $"{Accuracy.Value * 100:F1}%";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"| {DateTime.Now:yyyy-MM-dd HH:mm} | {Machine} | {Model} | {Language} | {AudioSeconds:F1} s | {ElapsedSeconds:F2} s | {RealTimeFactor:F3} | {PeakMemoryMb} MB | {accuracy} |");
    }
}
