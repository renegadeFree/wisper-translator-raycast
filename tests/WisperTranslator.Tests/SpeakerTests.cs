using WisperTranslator.Core.Asr;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Rendering;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Templates;

namespace WisperTranslator.Tests;

/// <summary>Parlanti: raggruppamento delle parole, etichette, esportazioni e sezione PDF.</summary>
public class SpeakerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wisper-voci-{Guid.NewGuid():N}");

    public SpeakerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // file ancora aperto: la cartella temporanea resta
        }
    }

    [Fact]
    public void UnisceLeParoleConsecutiveDelloStessoParlante()
    {
        var words = new List<(string Word, double Start, double End, int Speaker)>
        {
            ("ciao", 0.0, 0.4, 1),
            ("come", 0.4, 0.7, 1),
            ("va", 0.7, 1.0, 1),
            ("bene", 1.2, 1.6, 2),
            ("grazie", 1.6, 2.0, 2),
        };

        var segments = Group(words);

        Assert.Equal(2, segments.Count);
        Assert.Equal("ciao come va", segments[0].Text);
        Assert.Equal(1, segments[0].Speaker);
        Assert.Equal("bene grazie", segments[1].Text);
        Assert.Equal(2, segments[1].Speaker);
        Assert.Equal(TimeSpan.FromSeconds(1.0), segments[0].Duration);
    }

    [Fact]
    public void SenzaParlantiNonProduceTurni()
    {
        var words = new List<(string Word, double Start, double End, int Speaker)>
        {
            ("ciao", 0.0, 0.4, 0),
            ("a", 0.4, 0.6, 0),
        };

        Assert.Empty(Group(words));
    }

    [Fact]
    public void IlTrascrittoreEmetteUnTurnoPerParlante()
    {
        var result = new AsrResult(
            "ciao bene",
            "it",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMilliseconds(120),
            [
                new AsrSegment("ciao", TimeSpan.Zero, TimeSpan.FromSeconds(1), 1),
                new AsrSegment("a", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(0.3), 1),
                new AsrSegment("bene", TimeSpan.FromSeconds(1.3), TimeSpan.FromSeconds(0.7), 2),
            ]);

        var turns = RealtimeTranscriber.BuildTurns(result);

        Assert.Equal(2, turns.Count);
        Assert.Equal("ciao a", turns[0].Text);
        Assert.Equal($"Speaker 1", Speakers.Label(turns[0].Speaker));
        Assert.Equal(Speakers.Label(2), Speakers.Label(turns[1].Speaker));
    }

    [Fact]
    public void LeEtichetteCopronoTuAnonimoEParlanti()
    {
        Assert.Equal("Tu", Speakers.Label(Speakers.You));
        Assert.Equal("Speaker 3", Speakers.Label(3));
        Assert.Equal("Partecipanti", Speakers.Label(Speakers.Unknown, "Partecipanti"));
        Assert.NotEqual(Speakers.Color(1), Speakers.Color(2));
    }

    [Fact]
    public void LaBattutaConParlanteSiMostraConIlNome()
    {
        var anonymous = new Cue(1, "ciao", "hello", true, TimeSpan.Zero, TimeSpan.FromSeconds(1), DateTime.Now);
        var spoken = anonymous with { Speaker = -1 };

        Assert.False(anonymous.HasSpeaker);
        Assert.Empty(anonymous.SpeakerLabel);
        Assert.True(spoken.HasSpeaker);
        Assert.Equal("Tu", spoken.SpeakerLabel);
    }

    [Fact]
    public void SrtUsaIlTagDelParlanteSoloQuandoServe()
    {
        var cues = new[]
        {
            new HistoryCue(1, 1, DateTime.Now, TimeSpan.Zero, TimeSpan.FromSeconds(2), "ciao", "hello", true, 1),
            new HistoryCue(2, 1, DateTime.Now, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "bene", "fine", true),
        };

        var srt = HistoryExporter.ToSrt(cues, includeOriginal: false);

        Assert.Contains("<v Speaker 1>hello", srt);
        Assert.DoesNotContain("<v Speaker 1>fine", srt);
        Assert.Contains("fine", srt);
    }

    [Fact]
    public void CalcolaQuantoHaParlatoOgnuno()
    {
        var anonymous = new HistoryCue(1, 1, DateTime.Now, TimeSpan.Zero, TimeSpan.FromSeconds(4), "a", "b", true);
        var cues = new[]
        {
            anonymous with { Speaker = 1, Duration = TimeSpan.FromSeconds(6) },
            anonymous with { Speaker = 1, Duration = TimeSpan.FromSeconds(2) },
            anonymous with { Speaker = 2, Duration = TimeSpan.FromSeconds(2) },
            anonymous with { Speaker = Speakers.You, Duration = TimeSpan.FromSeconds(4) },
        };

        var totals = PdfReportBuilder.SpeakerTotals(cues);

        Assert.Equal(3, totals.Count);
        Assert.Equal("Speaker 1", totals[0].Label);
        Assert.Equal(2, totals[0].Cues);
        // 8 secondi su 14 totali.
        Assert.Equal(8.0 / 14.0, totals[0].Share, 3);
        Assert.Contains(totals, total => total.Label == "Tu");
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void IlReportConSezionePartecipantiSiGenera()
    {
        var template = BundledTemplates.Pdf("verbale-riunione")!;
        Assert.Contains(template.Sections, section => section.Kind == PdfSectionKind.Speakers);

        var path = Path.Combine(_directory, "voci.pdf");
        var session = new HistorySession(1, DateTime.Now, DateTime.Now, "it", "en", 2);
        var cues = new[]
        {
            new HistoryCue(1, 1, DateTime.Now, TimeSpan.Zero, TimeSpan.FromSeconds(3), "ciao", "hello", true, 1),
            new HistoryCue(2, 1, DateTime.Now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), "bene", "fine", true, 2),
        };

        PdfReportBuilder.Build(path, session, cues, null, null, template, null, null);

        Assert.True(new FileInfo(path).Length > 1024);
    }

    [Fact]
    public void IlDatabaseVecchioRiceveLaColonnaDelParlante()
    {
        var database = Path.Combine(_directory, "vecchio.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // Schema v1: nessuna colonna speaker.
            command.CommandText =
                """
                CREATE TABLE sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at TEXT NOT NULL,
                    ended_at TEXT,
                    source_language TEXT NOT NULL,
                    target_language TEXT NOT NULL
                );
                CREATE TABLE cues (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id INTEGER NOT NULL,
                    utterance_id INTEGER NOT NULL,
                    timestamp TEXT NOT NULL,
                    audio_start REAL NOT NULL,
                    duration_seconds REAL NOT NULL,
                    original TEXT NOT NULL,
                    translation TEXT NOT NULL DEFAULT '',
                    is_final INTEGER NOT NULL DEFAULT 0,
                    UNIQUE (session_id, utterance_id)
                );
                """;
            command.ExecuteNonQuery();
        }

        using var store = new SessionStore(database);
        var session = store.BeginSession("it", "en");
        store.SaveCue(
            session,
            new Cue(1, "ciao", "hello", true, TimeSpan.Zero, TimeSpan.FromSeconds(1), DateTime.Now, string.Empty, Speakers.You));

        var cue = Assert.Single(store.Cues(session));
        Assert.Equal(Speakers.You, cue.Speaker);
        Assert.Equal("Tu", cue.SpeakerLabel);
    }

    [Fact]
    public void LeImpostazioniBarraHannoValoriSensati()
    {
        var settings = new WisperTranslator.Core.Settings.AppSettings();

        Assert.False(settings.StartWithBar);
        Assert.Equal(2, settings.BarRows);
        Assert.Equal(5, settings.BarBuffer);
        Assert.Equal(WisperTranslator.Core.Settings.BarTextMode.Entrambi, settings.BarText);
        Assert.False(settings.BarDiscreet);
        Assert.True(settings.BarAnimations);
        Assert.Equal(NeMoModels.DiarizerDefaultId, settings.DiarizerModelId);
    }

    [Fact]
    public void IlCatalogoContieneIDueDiarizzatori()
    {
        Assert.Equal(2, NeMoModels.Diarizers.Count);
        Assert.Equal(107_012_128, ModelCatalog.NemotronDiarization.ExpectedSizeBytes);
        Assert.Equal(147_075_776, ModelCatalog.SortformerDiarization.ExpectedSizeBytes);
        Assert.All(NeMoModels.Diarizers, entry => Assert.Equal(WisperTranslator.Core.Models.ModelRole.Diarization, entry.Role));
        Assert.Equal("diar", WisperTranslator.Core.Models.ModelStore.RoleFolder(WisperTranslator.Core.Models.ModelRole.Diarization));
        Assert.Equal(ModelCatalog.SortformerDiarization, NeMoModels.DiarizerEntry("id-ignoto"));
    }

    private static IReadOnlyList<AsrSegment> Group(IEnumerable<(string Word, double Start, double End, int Speaker)> words) =>
        NeMoSpeechEngine.GroupBySpeaker(
            [.. words.Select(word => new NeMoSpeechEngine.TranscriptionWord(word.Word, word.Start, word.End, word.Speaker))]);

    [Fact]
    public void LeParoleSenzaDiarizzazioneMantengonoITempi()
    {
        var words = NeMoSpeechEngine.Words(
        [
            new NeMoSpeechEngine.TranscriptionWord("ciao", 0.2, 0.6, null),
            new NeMoSpeechEngine.TranscriptionWord("a", 0.7, 0.8, null),
            new NeMoSpeechEngine.TranscriptionWord("tutti", 0.9, 1.2, null),
        ]);

        Assert.Equal(3, words.Count);
        Assert.Equal("ciao", words[0].Text);
        Assert.Equal(0, words[0].Speaker);
        Assert.Equal(0.2, words[0].Start.TotalSeconds, 3);
    }

    [Fact]
    public void LaDiarizzazioneDifferitaAssegnaIlParlanteAlCentroDellaParola()
    {
        var words = new[]
        {
            new AsrSegment("ciao", TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(0.3)),
            new AsrSegment("a", TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(0.2)),
            new AsrSegment("tutti", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(0.4)),
        };
        var segments = new[]
        {
            new DiarizationSegment(0.0, 1.5, 1),
            new DiarizationSegment(2.0, 3.0, 2),
        };

        var tagged = NeMoDiarizationClient.ApplySpeakers(words, segments);

        Assert.Equal(1, tagged[0].Speaker);
        Assert.Equal(1, tagged[1].Speaker);
        Assert.Equal(2, tagged[2].Speaker);
    }
}
