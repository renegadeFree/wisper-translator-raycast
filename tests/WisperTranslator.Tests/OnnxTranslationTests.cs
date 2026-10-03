using WisperTranslator.Core;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Translation;
using Xunit.Abstractions;

namespace WisperTranslator.Tests;

/// <summary>
/// Il secondo stadio di traduzione (Marian ONNX) si prova solo se i modelli sono scaricati:
/// in CI o su una macchina pulita i test si saltano invece di fallire.
/// </summary>
public class OnnxTranslationTests(ITestOutputHelper output)
{
    /// <summary>Null quando il modello non è installato: il test si salta con il motivo scritto.</summary>
    private static string? Directory(string id)
    {
        var entry = ModelCatalog.ById(id);
        return ModelStore.IsInstalled(entry) ? ModelStore.PathFor(entry) : null;
    }

    [Fact]
    public void IlTokenizerMarianSegmentaERicomponeIlTesto()
    {
        var directory = Directory("opus-mt-it-en");
        if (directory is null)
        {
            output.WriteLine("saltato: modello opus-mt-it-en non scaricato");
            return;
        }

        var tokenizer = MarianTokenizer.Load(directory);
        var ids = tokenizer.Encode("Buongiorno, come stai?");
        Assert.NotEmpty(ids);
        Assert.Equal(tokenizer.EndTokenId, ids[^1]);

        var text = tokenizer.Decode(ids);
        Assert.Contains("Buongiorno", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("▁", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScaricaEVerificaIModelliDiRifinitura()
    {
        if (Environment.GetEnvironmentVariable("WISPER_DOWNLOAD_ONNX") != "1")
        {
            output.WriteLine("saltato: imposta WISPER_DOWNLOAD_ONNX=1 per scaricare ~270 MB");
            return;
        }

        foreach (var entry in new[] { ModelCatalog.OpusMtItalianEnglish, ModelCatalog.OpusMtEnglishItalian })
        {
            var directory = await ModelStore.EnsureAsync(entry);
            output.WriteLine($"{entry.Id}: {ModelStore.InstalledSize(entry) / (1024.0 * 1024):F0} MB in {directory}");
            var (ok, message) = await ModelStore.VerifyAsync(entry);
            Assert.True(ok, $"{entry.Id}: {message}");
        }
    }

    [Fact]
    public async Task TraduceItalianoInIngleseConIlModelloOnnx()
    {
        var directory = Directory("opus-mt-it-en");
        if (directory is null)
        {
            output.WriteLine("saltato: modello opus-mt-it-en non scaricato");
            return;
        }

        using var engine = new OnnxTranslationEngine(Path.GetDirectoryName(directory));
        var result = await engine.TranslateAsync("Buongiorno, questo è un test di traduzione.", "it", "en");

        output.WriteLine($"it→en: {result.Text} ({result.Elapsed.TotalMilliseconds:F0} ms)");
        Assert.Contains("Good morning", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TraduceIngleseInItalianoConIlModelloOnnx()
    {
        var directory = Directory("opus-mt-en-it");
        if (directory is null)
        {
            output.WriteLine("saltato: modello opus-mt-en-it non scaricato");
            return;
        }

        using var engine = new OnnxTranslationEngine(Path.GetDirectoryName(directory));
        var result = await engine.TranslateAsync("Good morning, the film grain is beautiful.", "en", "it");

        output.WriteLine($"en→it: {result.Text} ({result.Elapsed.TotalMilliseconds:F0} ms)");
        Assert.Contains("Buongiorno", result.Text, StringComparison.OrdinalIgnoreCase);
    }
}
