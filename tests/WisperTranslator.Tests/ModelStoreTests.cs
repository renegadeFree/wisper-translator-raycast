using System.Security.Cryptography;
using WisperTranslator.Core.Models;

namespace WisperTranslator.Tests;

/// <summary>
/// Guardia sul percorso di importazione: un file sbagliato non deve poter sostituire un
/// modello funzionante (era un difetto reale trovato durante i test di F6).
/// </summary>
public class ModelStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wisper-models-" + Guid.NewGuid().ToString("N"));

    public ModelStoreTests() => Directory.CreateDirectory(_directory);

    /// <summary>
    /// I motori di rifinitura ONNX sono multi-file: il catalogo deve descrivere ogni file con
    /// dimensione e hash, altrimenti lo scaricamento non è verificabile.
    /// </summary>
    [Fact]
    public void IModelliOnnxSonoDescrittiFilePerFile()
    {
        foreach (var entry in new[] { ModelCatalog.OpusMtItalianEnglish, ModelCatalog.OpusMtEnglishItalian })
        {
            Assert.Equal(ModelPackaging.Files, entry.Packaging);
            Assert.NotNull(entry.Files);
            Assert.Equal(5, entry.Files!.Count);
            Assert.All(entry.Files, file =>
            {
                Assert.StartsWith("https://huggingface.co/", file.Url, StringComparison.Ordinal);
                Assert.True(file.SizeBytes > 0);
                Assert.Equal(64, file.Sha256.Length);
            });
            Assert.Equal(entry.Files.Sum(file => file.SizeBytes), entry.ExpectedSizeBytes);
            Assert.Contains(entry.Files, file =>
                file.RelativePath.EndsWith("encoder_model_int8.onnx", StringComparison.Ordinal));
            Assert.Contains(entry.Files, file =>
                file.RelativePath.EndsWith("decoder_model_merged_int8.onnx", StringComparison.Ordinal));
        }

        // Le due direzioni vivono in cartelle distinte sotto models\mt\onnx.
        Assert.Contains("onnx", ModelStore.PathFor(ModelCatalog.OpusMtItalianEnglish), StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            ModelStore.PathFor(ModelCatalog.OpusMtItalianEnglish),
            ModelStore.PathFor(ModelCatalog.OpusMtEnglishItalian));
    }

    [Fact]
    public async Task AccettaUnFileCorrispondenteAlCatalogo()
    {
        var content = new byte[4096];
        Random.Shared.NextBytes(content);
        var path = Write(content);

        var (ok, message) = await ModelStore.CheckFileAsync(path, EntryFor(content));

        Assert.True(ok, message);
        Assert.Equal("Integro", message);
    }

    [Fact]
    public async Task RifiutaUnFileConContenutoDiverso()
    {
        var expected = new byte[4096];
        Random.Shared.NextBytes(expected);
        var path = Write(new byte[4096]);

        var (ok, message) = await ModelStore.CheckFileAsync(path, EntryFor(expected));

        Assert.False(ok);
        Assert.Contains("Hash diverso", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RifiutaUnFileTroppoPiccolo()
    {
        var path = Write([1, 2, 3]);

        var (ok, message) = await ModelStore.CheckFileAsync(path, EntryFor([1, 2, 3]));

        Assert.False(ok);
        Assert.Contains("troppo piccolo", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RifiutaUnFileDiDimensioneDiversa()
    {
        var expected = new byte[8192];
        Random.Shared.NextBytes(expected);
        var path = Write(new byte[4096]);

        var (ok, message) = await ModelStore.CheckFileAsync(path, EntryFor(expected));

        Assert.False(ok);
        Assert.Contains("Dimensione diversa", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnImportErratoNonSostituisceIlModelloInstallato()
    {
        var content = new byte[4096];
        Random.Shared.NextBytes(content);
        var entry = EntryFor(content) with { RelativePath = "review-test-" + Guid.NewGuid().ToString("N") + "/model.bin" };
        var destination = ModelStore.PathFor(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            await File.WriteAllBytesAsync(destination, content);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ModelStore.ImportAsync(Write(new byte[4096]), entry));
            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.import-*"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(destination)!, recursive: true); }
    }

    private string Write(byte[] content)
    {
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static ModelCatalogEntry EntryFor(byte[] content) => new(
        "test-model",
        "Modello di prova",
        ModelRole.Asr,
        "C",
        "https://example.invalid/model.bin",
        "model.bin",
        "model.bin",
        content.Length,
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
        "MIT",
        "modello fittizio per i test");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
            // cartella temporanea già rimossa
        }
    }
}
