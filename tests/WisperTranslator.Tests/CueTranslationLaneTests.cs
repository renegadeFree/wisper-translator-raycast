using WisperTranslator.Core.Translation;
using WisperTranslator.Core.Models;
using Xunit.Abstractions;

namespace WisperTranslator.Tests;

/// <summary>
/// La corsia rapida non deve mai annullare la traduzione in corso: era il motivo per cui,
/// parlando di continuo, la traduzione non compariva finché non si faceva una pausa.
/// </summary>
public class CueTranslationLaneTests(ITestOutputHelper output)
{
    /// <summary>
    /// Prova sul campo: parziale che cresce ogni 200 ms con i motori veri (Bergamot + Marian ONNX)
    /// e cronometro su quando la traduzione compare. Si salta se i motori non sono disponibili.
    /// </summary>
    [Fact]
    public async Task LaTraduzioneRealeArrivaMentreSiParla()
    {
        var fastEngine = new LocalHttpEngine();
        if (!await fastEngine.IsAvailableAsync())
        {
            output.WriteLine("saltato: MTranServer non in ascolto");
            fastEngine.Dispose();
            return;
        }

        var directory = Path.GetDirectoryName(ModelStore.PathFor(ModelCatalog.OpusMtItalianEnglish));
        if (!ModelStore.IsInstalled(ModelCatalog.OpusMtItalianEnglish))
        {
            output.WriteLine("saltato: modello opus-mt-it-en non scaricato");
            fastEngine.Dispose();
            return;
        }

        var published = new List<(string Text, bool Quality, long At)>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var fast = new TranslationService(fastEngine);
        using var quality = new TranslationService(new OnnxTranslationEngine(directory));
        using var lane = new CueTranslationLane(
            1, fast, quality, new SemaphoreSlim(4, 4), new SemaphoreSlim(1, 1),
            () => ("it", "en"),
            (_, text, isQuality) =>
            {
                lock (published)
                {
                    published.Add((text, isQuality, watch.ElapsedMilliseconds));
                }
            });

        var words = "No la gran pellicola rischia di mettere in pericolo cioé mi piace a me la grana pellicola".Split(' ');
        for (var index = 0; index < words.Length; index++)
        {
            lane.Update(string.Join(' ', words.Take(index + 1)), false);
            await Task.Delay(200);
        }

        lane.Update(string.Join(' ', words), true);
        await Task.Delay(4000);

        lock (published)
        {
            foreach (var item in published)
            {
                output.WriteLine($"{item.At,6} ms · {(item.Quality ? "qualità" : "rapida")} · {item.Text}");
            }

            Assert.True(published.Count >= 4, $"pubblicate solo {published.Count} traduzioni");
            Assert.True(published[0].At < 1500, $"prima traduzione dopo {published[0].At} ms");
            Assert.Contains(published, item => item.Quality);
        }
    }

    /// <summary>Motore finto: risponde dopo un ritardo e conta le richieste ricevute.</summary>
    private sealed class FakeEngine(string prefix, TimeSpan delay) : ITranslationEngine
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Name => $"fake:{prefix}";

        public async Task<TranslationResult> TranslateAsync(string text, string from, string to,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(delay, cancellationToken);
            return new TranslationResult($"{prefix}{text}", from, to, delay, false);
        }

        public void Dispose()
        {
        }
    }

    private static CueTranslationLane CreateLane(
        TranslationService fast,
        TranslationService? quality,
        List<(string Text, bool Quality)> published)
    {
        return new CueTranslationLane(
            1,
            fast,
            quality,
            new SemaphoreSlim(2, 2),
            new SemaphoreSlim(1, 1),
            () => ("it", "en"),
            (_, text, isQuality) =>
            {
                lock (published)
                {
                    published.Add((text, isQuality));
                }
            },
            report: null,
            sessionToken: CancellationToken.None);
    }

    [Fact]
    public async Task IParzialiContinuiVengonoTradottiSenzaAspettareLaPausa()
    {
        var engine = new FakeEngine("F:", TimeSpan.FromMilliseconds(30));
        using var fast = new TranslationService(engine);
        var published = new List<(string Text, bool Quality)>();
        using var lane = CreateLane(fast, null, published);

        for (var step = 1; step <= 6; step++)
        {
            lane.Update($"frase numero {step}", false);
            await Task.Delay(200);
        }

        await Task.Delay(300);
        lock (published)
        {
            // La prima traduzione esce subito e le successive non vengono uccise dai parziali.
            Assert.True(published.Count >= 3, $"pubblicate solo {published.Count} traduzioni");
            Assert.StartsWith("F:frase numero 1", published[0].Text, StringComparison.Ordinal);
            Assert.False(published[0].Quality);
        }
    }

    [Fact]
    public async Task IlTestoDefinitivoVinceSulParziale()
    {
        var engine = new FakeEngine("F:", TimeSpan.FromMilliseconds(20));
        var quality = new FakeEngine("Q:", TimeSpan.FromMilliseconds(20));
        using var fastService = new TranslationService(engine);
        using var qualityService = new TranslationService(quality);
        var published = new List<(string Text, bool Quality)>();
        using var lane = CreateLane(fastService, qualityService, published);

        lane.Update("frase parziale", false);
        await Task.Delay(150);
        lane.Update("frase definitiva completa", true);
        await Task.Delay(600);

        lock (published)
        {
            var seen = string.Join(" | ", published.Select(item => $"{item.Text}({item.Quality})"));
            Assert.True(published.Any(item => item.Text == "F:frase parziale"), seen);
            Assert.True(published.Any(item => item.Text == "Q:frase definitiva completa" && item.Quality), seen);
            // La versione approssimativa della frase finale non deve arrivare dopo quella rifinita.
            Assert.Equal("Q:frase definitiva completa", published[^1].Text);
        }
    }

    [Fact]
    public async Task LaRifinituraDiMetaFraseArrivaDopoAlcuneParole()
    {
        var fast = new TranslationService(new FakeEngine("F:", TimeSpan.FromMilliseconds(10)));
        var quality = new TranslationService(new FakeEngine("Q:", TimeSpan.FromMilliseconds(10)));
        var published = new List<(string Text, bool Quality)>();
        using var lane = CreateLane(fast, quality, published);

        lane.Update("una frase di prova", false);
        await Task.Delay(1300);
        lane.Update("una frase di prova con molte altre parole aggiunte", false);
        await Task.Delay(400);

        lock (published)
        {
            Assert.Contains(published, item => item.Quality);
        }
    }

    [Fact]
    public async Task LoStopChiudeLaCorsiaSenzaTradurreAltro()
    {
        var engine = new FakeEngine("F:", TimeSpan.FromMilliseconds(200));
        using var fast = new TranslationService(engine);
        var published = new List<(string Text, bool Quality)>();
        var lane = CreateLane(fast, null, published);
        lane.Update("frase lunga da tradurre", false);
        await Task.Delay(30);

        lane.Dispose();
        await lane.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        lock (published)
        {
            Assert.Empty(published);
        }
    }
}
