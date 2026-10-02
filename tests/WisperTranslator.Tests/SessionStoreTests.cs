using WisperTranslator.Core.History;
using WisperTranslator.Core.Session;

namespace WisperTranslator.Tests;

public class SessionStoreTests : IDisposable
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"wisper-history-{Guid.NewGuid():N}.db");

    [Fact]
    public void AggiornaLaStessaBattutaInveceDiDuplicarla()
    {
        using var store = new SessionStore(_database);
        var session = store.BeginSession("it", "en");

        store.SaveCue(session, Cue(1, "buongiorno", string.Empty, false));
        store.SaveCue(session, Cue(1, "buongiorno, questa è una prova", "Good morning, this is a test", true));

        var cues = store.Cues(session);
        var cue = Assert.Single(cues);
        Assert.Equal("buongiorno, questa è una prova", cue.Original);
        Assert.Equal("Good morning, this is a test", cue.Translation);
        Assert.True(cue.IsFinal);
    }

    [Fact]
    public void EliminaLeSessioniPiuVecchieDellaRetention()
    {
        using var store = new SessionStore(_database, retentionDays: 5);
        var old = store.BeginSession("it", "en");
        store.SaveCue(old, Cue(1, "vecchia", "old", true));
        var recent = store.BeginSession("it", "en");
        store.SaveCue(recent, Cue(1, "recente", "new", true));

        // Si retrodatano le sessioni scrivendo direttamente nel database.
        RewriteStart(store, old, DateTime.Now.AddDays(-6));
        RewriteStart(store, recent, DateTime.Now.AddDays(-1));

        var removed = store.PurgeExpired();

        Assert.Equal(1, removed);
        Assert.Single(store.Sessions());
        Assert.Equal(recent, store.Sessions()[0].Id);
        Assert.Empty(store.Cues(old));
    }

    [Fact]
    public void ContaLeBattuteDellaSessione()
    {
        using var store = new SessionStore(_database);
        var session = store.BeginSession("en", "it");
        store.SaveCue(session, Cue(1, "one", "uno", true));
        store.SaveCue(session, Cue(2, "two", "due", true));
        store.EndSession(session);

        var info = Assert.Single(store.Sessions());
        Assert.Equal(2, info.CueCount);
        Assert.Equal("en", info.SourceLanguage);
        Assert.NotNull(info.EndedAt);
    }

    [Fact]
    public void EsportaUnSrtConTimecodeValidi()
    {
        var cues = new List<HistoryCue>
        {
            new(1, 1, DateTime.Now, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), "buongiorno", "good morning", true),
            new(2, 1, DateTime.Now, TimeSpan.FromSeconds(4.25), TimeSpan.FromSeconds(1.5), "come stai", "how are you", true),
        };

        var srt = HistoryExporter.ToSrt(cues);
        var lines = srt
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        Assert.Equal("1", lines[0]);
        Assert.Equal("00:00:01,500 --> 00:00:03,500", lines[1]);
        Assert.Equal("good morning", lines[2]);
        Assert.Equal("buongiorno", lines[3]);
        Assert.Equal("2", lines[4]);
        Assert.Equal("00:00:04,250 --> 00:00:05,750", lines[5]);
    }

    [Fact]
    public void EsportaJsonConLeBattuteComplete()
    {
        var session = new HistorySession(7, DateTime.Now, null, "it", "en", 1);
        var cues = new List<HistoryCue>
        {
            new(1, 7, DateTime.Now, TimeSpan.Zero, TimeSpan.FromSeconds(3), "ciao", "hello", true),
        };

        var json = HistoryExporter.ToJson(session, cues);

        Assert.Contains("\"SourceLanguage\": \"it\"", json);
        Assert.Contains("\"Translation\": \"hello\"", json);
    }

    private static Cue Cue(int id, string original, string translation, bool isFinal) =>
        new(id, original, translation, isFinal, TimeSpan.FromSeconds(id * 2), TimeSpan.FromSeconds(2), DateTime.Now);

    private static void RewriteStart(SessionStore store, long sessionId, DateTime startedAt)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET started_at = $started WHERE id = $id;";
        command.Parameters.AddWithValue("$started", startedAt.ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _database + suffix;
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    // file ancora in uso dal provider: verrà rimosso dal sistema
                }
            }
        }
    }
}
