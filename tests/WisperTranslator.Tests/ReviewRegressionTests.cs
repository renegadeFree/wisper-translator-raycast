using System.Reflection;
using System.Security.Cryptography;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Asr;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;
using WisperTranslator.Core.Templates;
using WisperTranslator.Core.Translation;

namespace WisperTranslator.Tests;

public class ReviewRegressionTests
{
    [Fact]
    public void IlMixerDrenaAncheUnaSorgenteSpenta()
    {
        var source = new BufferedSource();
        using var mixer = new AudioMixer();
        mixer.Add(source);
        var block = new float[2];
        Assert.Equal(2, mixer.Read(block));
        Assert.Equal([0f, 0f], block);
        source.Enabled = true;
        Assert.Equal(0, mixer.Read(block));
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public void LoSnapshotCircolareNonConsumaIAudio()
    {
        var buffer = new FloatRingBuffer(4);
        buffer.Write([1, 2, 3]);
        buffer.Read(new float[2]);
        buffer.Write([4, 5, 6, 7]);
        Assert.Equal([4f, 5f, 6f, 7f], buffer.Snapshot());
        Assert.Equal(4, buffer.Count);
        var read = new float[4];
        buffer.Read(read);
        Assert.Equal([4f, 5f, 6f, 7f], read);
    }

    [Fact]
    public async Task LaCacheEspelleUnaVoceSenzaSvuotareTutto()
    {
        using var service = new TranslationService(new EchoEngine(), cacheLimit: 2);
        await service.TranslateAsync("uno", "it", "en");
        await service.TranslateAsync("due", "it", "en");
        await service.TranslateAsync("tre", "it", "en");
        Assert.Equal(2, service.CacheCount);
        Assert.True((await service.TranslateAsync("due", "it", "en")).FromCache);
        Assert.False((await service.TranslateAsync("uno", "it", "en")).FromCache);
    }

    [Fact]
    public async Task LaCacheNonConfondeChiaviContenentiSeparatori()
    {
        using var service = new TranslationService(new EchoEngine());
        await service.TranslateAsync("c|d", "a", "b");
        Assert.False((await service.TranslateAsync("d", "a", "b|c")).FromCache);
    }

    [Fact]
    public void RetentionZeroConservaAncheLeSessioniVecchie()
    {
        var path = Temporary(".db");
        try
        {
            using var store = new SessionStore(path, retentionDays: 0);
            var id = store.BeginSession("it", "en");
            store.SaveNote(id, "locale", summary: "appunti");
            Assert.Equal(0, store.PurgeExpired(DateTime.Now.AddYears(100)));
            Assert.Single(store.Sessions());
            Assert.NotNull(store.GetNote(id));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public void LaRetentionEliminaAncheLeNoteScadute()
    {
        var path = Temporary(".db");
        try
        {
            using var store = new SessionStore(path, retentionDays: 1);
            var id = store.BeginSession("it", "en");
            store.SaveNote(id, "locale", summary: "appunti");
            Assert.Equal(1, store.PurgeExpired(DateTime.Now.AddDays(3)));
            Assert.Null(store.GetNote(id));
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task LaTraduzioneDefinitivaVieneSalvataNelloStorico()
    {
        var path = Temporary(".db");
        try
        {
            using var store = new SessionStore(path);
            var id = store.BeginSession("it", "en");
            await using var session = new TranscriptionSession(history: store);
            Field(session, "_historySessionId", id);
            Field(session, "_translationService", new TranslationService(new EchoEngine()));
            Publish(session, "testo", true);

            // La traduzione arriva dalle corsie in background: si attende che sia pubblicata.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var translation = string.Empty;
            while (DateTime.UtcNow < deadline)
            {
                translation = Assert.Single(store.Cues(id)).Translation;
                if (translation.Length > 0)
                {
                    break;
                }

                await Task.Delay(50);
            }

            Assert.Equal("tradotto: testo", translation);
            Assert.Equal(1, session.Transcribed);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task UnParzialeTardivoNonSovrascriveIlFinale()
    {
        var path = Temporary(".db");
        try
        {
            using var store = new SessionStore(path);
            var id = store.BeginSession("it", "en");
            await using var session = new TranscriptionSession(new SessionOptions { Translate = false }, store);
            Field(session, "_historySessionId", id);
            Publish(session, "finale", true);
            Publish(session, "vecchio parziale", false);
            Publish(session, "finale corretto", true);
            var cue = Assert.Single(store.Cues(id));
            Assert.True(cue.IsFinal);
            Assert.Equal("finale corretto", cue.Original);
            Assert.Equal(1, session.Transcribed);
        }
        finally { DeleteDatabase(path); }
    }

    [Fact]
    public async Task ITurniDiarizzatiMantengonoIlTempoAssolutoDellaSessione()
    {
        await using var session = new TranscriptionSession(new SessionOptions { Translate = false });
        var laneType = typeof(TranscriptionSession).GetNestedType("Lane", BindingFlags.NonPublic)!;
        var lane = Activator.CreateInstance(laneType, [0, Speakers.Unknown, null, null]);
        var cues = new List<Cue>();
        session.CueUpdated += cues.Add;
        var update = new TranscriptUpdate(1, "ciao grazie", true, TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5), TimeSpan.Zero, Turns:
            [new(1, "ciao", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
             new(2, "grazie", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1))]);
        typeof(TranscriptionSession).GetMethod("OnTranscriptUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [lane, update]);
        Assert.Equal([21.0, 23.0], cues.Select(cue => cue.AudioStart.TotalSeconds));
    }

    [Fact]
    public void ITemplateNonPossonoScrivereFuoriDallaCartella()
    {
        Assert.Throws<ArgumentException>(() => TemplateStore.Save(BundledTemplates.Maps[0] with { Id = "../../settings" }));
        Assert.Throws<ArgumentException>(() => TemplateStore.Delete(BundledTemplates.Maps[0] with { Id = "../settings" }));
    }

    [Fact]
    public void LaMappaRimuoveIDDuplicatiERiferimentiInesistenti()
    {
        var map = ConceptMap.Parse("""
            {"nodes":[{"id":"a","label":"uno"},{"id":"a","label":"doppio"},{"id":"b","label":"due"}],
             "edges":[{"from":"a","to":"b"},{"from":"a","to":"inesistente"}]}
            """);
        Assert.Equal(2, map.Nodes.Count);
        Assert.Single(map.Edges);
    }

    [Fact]
    public void LePreferenzeCorrotteTornanoAValoriValidi()
    {
        var settings = new AppSettings { BarWidth = double.NaN, BarRows = 99, BarBuffer = 0,
            BarText = (BarTextMode)99, FontSize = double.PositiveInfinity, RetentionDays = -1,
            PartialModelId = "inesistente", BarLeft = double.NaN };
        settings.NormalizeValues();
        Assert.Equal(BarGeometry.DefaultWidth, settings.BarWidth);
        Assert.Equal(BarGeometry.MaxRows, settings.BarRows);
        Assert.Equal(3, settings.BarBuffer);
        Assert.Equal(BarTextMode.Entrambi, settings.BarText);
        Assert.Equal(16, settings.FontSize);
        Assert.Equal(0, settings.RetentionDays);
        Assert.Equal(AsrModels.Base.Id, settings.PartialModelId);
        Assert.Null(settings.BarLeft);
    }

    [Fact]
    public void IlCatalogoHaHashValidiPerOgniDownload()
    {
        Assert.All(ModelCatalog.All.Where(entry => !entry.ManagedByServer), entry =>
        {
            Assert.True(entry.ExpectedSizeBytes > 1024);
            if (entry.Packaging == ModelPackaging.Files)
            {
                // I modelli multi-file hanno un hash per file, non uno solo per il pacchetto.
                Assert.NotNull(entry.Files);
                Assert.All(entry.Files!, file =>
                {
                    Assert.Equal(64, file.Sha256.Length);
                    Assert.True(file.Sha256.All(Uri.IsHexDigit));
                    Assert.Equal("https", new Uri(file.Url).Scheme);
                });
                return;
            }

            Assert.Equal(64, entry.Sha256.Length);
            Assert.True(entry.Sha256.All(Uri.IsHexDigit));
            Assert.Equal("https", new Uri(entry.Url).Scheme);
        });
    }

    private static void Field(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private static void Publish(TranscriptionSession session, string text, bool final) =>
        typeof(TranscriptionSession).GetMethod("PublishCue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [1, 0, text, final, TimeSpan.Zero, TimeSpan.FromSeconds(1), "", TimeSpan.Zero]);

    private static string Temporary(string suffix) => Path.Combine(Path.GetTempPath(), "wisper-review-" + Guid.NewGuid().ToString("N") + suffix);
    private static void DeleteDatabase(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    private sealed class BufferedSource : IAudioSource
    {
        private int _count = 2;
        public int Reads { get; private set; }
        public string Name => "buffer";
        public bool Enabled { get; set; }
        public float Gain { get; set; } = 1;
        public int Read(Span<float> destination)
        {
            if (_count == 0) return 0;
            Reads++;
            destination[.._count].Fill(1);
            var read = _count;
            _count = 0;
            return read;
        }
    }

    private sealed class EchoEngine : ITranslationEngine
    {
        public string Name => "eco";
        public Task<TranslationResult> TranslateAsync(string text, string from, string to, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResult("tradotto: " + text, from, to, TimeSpan.Zero, false));
        public void Dispose() { }
    }
}
