using WisperTranslator.Core.Translation;

namespace WisperTranslator.Tests;

public class TranslationServiceTests
{
    [Fact]
    public async Task UsaLaCachePerTestiRipetuti()
    {
        using var service = new TranslationService(new CountingEngine());

        var first = await service.TranslateAsync("buongiorno", "it", "en");
        var second = await service.TranslateAsync("buongiorno", "it", "en");

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(1, service.Hits);
        Assert.Equal("buongiorno", second.Text);
    }

    [Fact]
    public async Task NonUsaLaCacheSeCambiaLaDirezione()
    {
        using var service = new TranslationService(new CountingEngine());

        await service.TranslateAsync("ciao", "it", "en");
        var result = await service.TranslateAsync("ciao", "en", "it");

        Assert.False(result.FromCache);
    }

    [Fact]
    public async Task TestoVuotoNonInterrogaIlMotore()
    {
        var engine = new CountingEngine();
        using var service = new TranslationService(engine);

        var result = await service.TranslateAsync("   ", "it", "en");

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task RimetteLoSpazioDopoLaPunteggiatura()
    {
        using var service = new TranslationService(new CountingEngine());

        var result = await service.TranslateAsync("ciao.Come stai?Bene", "en", "it");

        Assert.Equal("ciao. Come stai? Bene", result.Text);
    }

    private sealed class CountingEngine : ITranslationEngine
    {
        public string Name => "counting";

        public int Calls { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string text,
            string from,
            string to,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TranslationResult(text, from, to, TimeSpan.FromMilliseconds(1), false));
        }

        public void Dispose()
        {
        }
    }
}
