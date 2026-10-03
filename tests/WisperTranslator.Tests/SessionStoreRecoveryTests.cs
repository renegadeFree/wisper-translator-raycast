using WisperTranslator.Core.History;

namespace WisperTranslator.Tests;

/// <summary>
/// Un database dello storico danneggiato non deve impedire di usare il programma:
/// va messo da parte e sostituito con uno nuovo, senza errori a schermo.
/// </summary>
public class SessionStoreRecoveryTests
{
    [Fact]
    public void RecuperaUnDatabaseDanneggiato()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"wisper-db-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "history.db");

        // Intestazione SQLite valida ma contenuto spazzatura: è il caso reale osservato.
        var bytes = new byte[(4096 * 2) + 16];
        "SQLite format 3\0"u8.CopyTo(bytes);
        File.WriteAllBytes(database, bytes);

        try
        {
            using var store = new SessionStore(database);

            Assert.True(store.RecoveredFromCorruption);
            Assert.False(string.IsNullOrEmpty(store.RecoveryBackupPath));
            Assert.True(File.Exists(store.RecoveryBackupPath));

            var session = store.BeginSession("en", "it");
            Assert.True(session > 0);
            Assert.Single(store.Sessions());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnDatabaseNuovoFunzionaSubito()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"wisper-db-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "history.db");

        try
        {
            using var store = new SessionStore(database);
            Assert.False(store.RecoveredFromCorruption);
            var session = store.BeginSession("it", "en");
            Assert.True(session > 0);
            Assert.Single(store.Sessions());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
