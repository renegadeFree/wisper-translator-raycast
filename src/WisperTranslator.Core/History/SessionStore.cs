using Microsoft.Data.Sqlite;
using WisperTranslator.Core.Session;

namespace WisperTranslator.Core.History;

public sealed record HistorySession(
    long Id,
    DateTime StartedAt,
    DateTime? EndedAt,
    string SourceLanguage,
    string TargetLanguage,
    int CueCount);

public sealed record HistoryCue(
    long Id,
    long SessionId,
    DateTime Timestamp,
    TimeSpan AudioStart,
    TimeSpan Duration,
    string Original,
    string Translation,
    bool IsFinal);

public sealed record SessionNote(
    long SessionId,
    string Provider,
    string Summary,
    string KeyPoints,
    string ConceptMapJson,
    DateTime UpdatedAt)
{
    public bool IsEmpty => Summary.Length == 0 && KeyPoints.Length == 0 && ConceptMapJson.Length == 0;
}

/// <summary>
/// Storico locale delle sessioni in SQLite con retention a giorni. La scrittura è in WAL:
/// una chiusura anomala non fa perdere le battute già salvate.
/// </summary>
public sealed class SessionStore : IDisposable
{
    private const string Schema =
        """
        CREATE TABLE IF NOT EXISTS sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at TEXT NOT NULL,
            ended_at TEXT,
            source_language TEXT NOT NULL,
            target_language TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS cues (
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

        CREATE INDEX IF NOT EXISTS ix_cues_session ON cues (session_id);
        CREATE INDEX IF NOT EXISTS ix_sessions_started ON sessions (started_at);

        CREATE TABLE IF NOT EXISTS session_notes (
            session_id INTEGER PRIMARY KEY,
            provider TEXT NOT NULL DEFAULT '',
            summary TEXT NOT NULL DEFAULT '',
            key_points TEXT NOT NULL DEFAULT '',
            concept_map TEXT NOT NULL DEFAULT '',
            updated_at TEXT NOT NULL
        );
        """;

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private bool _disposed;

    public SessionStore(string? databasePath = null, int retentionDays = 5)
    {
        DatabasePath = databasePath ?? Path.Combine(Core.AppPaths.Root, "history.db");
        RetentionDays = retentionDays;

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connection = new SqliteConnection($"Data Source={DatabasePath}");
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();

        command.CommandText = Schema;
        command.ExecuteNonQuery();
    }

    public string DatabasePath { get; }

    public int RetentionDays { get; }

    public long BeginSession(string sourceLanguage, string targetLanguage)
    {
        lock (_gate)
        {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sessions (started_at, source_language, target_language)
            VALUES ($started, $source, $target);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$started", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$source", sourceLanguage);
        command.Parameters.AddWithValue("$target", targetLanguage);
        return (long)(command.ExecuteScalar() ?? 0L);
        }
    }

    public void EndSession(long sessionId)
    {
        lock (_gate)
        {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET ended_at = $ended WHERE id = $id AND ended_at IS NULL;";
        command.Parameters.AddWithValue("$ended", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$id", sessionId);
        command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Salva o aggiorna una battuta: la stessa battuta viene riscritta mano a mano che i
    /// parziali arrivano e poi sostituita dalla versione finale.
    /// </summary>
    public void SaveCue(long sessionId, Cue cue)
    {
        lock (_gate)
        {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO cues (session_id, utterance_id, timestamp, audio_start, duration_seconds, original, translation, is_final)
            VALUES ($session, $utterance, $timestamp, $start, $duration, $original, $translation, $final)
            ON CONFLICT (session_id, utterance_id) DO UPDATE SET
                timestamp = excluded.timestamp,
                audio_start = excluded.audio_start,
                duration_seconds = excluded.duration_seconds,
                original = excluded.original,
                translation = excluded.translation,
                is_final = excluded.is_final;
            """;
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$utterance", cue.Id);
        command.Parameters.AddWithValue("$timestamp", cue.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$start", cue.AudioStart.TotalSeconds);
        command.Parameters.AddWithValue("$duration", cue.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$original", cue.Original);
        command.Parameters.AddWithValue("$translation", cue.Translation);
        command.Parameters.AddWithValue("$final", cue.IsFinal ? 1 : 0);
        command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<HistorySession> Sessions(int limit = 50)
    {
        lock (_gate)
        {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.id, s.started_at, s.ended_at, s.source_language, s.target_language,
                   (SELECT COUNT(*) FROM cues c WHERE c.session_id = s.id)
            FROM sessions s
            ORDER BY s.started_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var sessions = new List<HistorySession>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sessions.Add(new HistorySession(
                reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTime.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5)));
        }

        return sessions;
        }
    }

    public IReadOnlyList<HistoryCue> Cues(long sessionId)
    {
        lock (_gate)
        {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, session_id, timestamp, audio_start, duration_seconds, original, translation, is_final
            FROM cues
            WHERE session_id = $session
            ORDER BY utterance_id;
            """;
        command.Parameters.AddWithValue("$session", sessionId);

        var cues = new List<HistoryCue>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cues.Add(new HistoryCue(
                reader.GetInt64(0),
                reader.GetInt64(1),
                DateTime.Parse(reader.GetString(2)),
                TimeSpan.FromSeconds(reader.GetDouble(3)),
                TimeSpan.FromSeconds(reader.GetDouble(4)),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) == 1));
        }

        return cues;
        }
    }

    /// <summary>Elimina le sessioni più vecchie della retention. Restituisce quante ne ha rimosse.</summary>
    public int PurgeExpired(DateTime? now = null)
    {
        lock (_gate)
        {
        var cutoff = (now ?? DateTime.Now).AddDays(-RetentionDays).ToString("O");

        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = "DELETE FROM cues WHERE session_id IN (SELECT id FROM sessions WHERE started_at < $cutoff);";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        var cues = command.ExecuteNonQuery();

        command.CommandText = "DELETE FROM sessions WHERE started_at < $cutoff;";
        var sessions = command.ExecuteNonQuery();
        transaction.Commit();

        return sessions > 0 ? sessions : cues > 0 ? 1 : 0;
        }
    }

    public long TotalSizeBytes()
    {
        lock (_gate)
        {
        long total = 0;
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = DatabasePath + suffix;
            if (File.Exists(file))
            {
                total += new FileInfo(file).Length;
            }
        }

        return total;
        }
    }

    /// <summary>Salva (o aggiorna) i contenuti generati dall'IA per una sessione.</summary>
    public void SaveNote(
        long sessionId,
        string provider,
        string? summary = null,
        string? keyPoints = null,
        string? conceptMapJson = null)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO session_notes (session_id, provider, summary, key_points, concept_map, updated_at)
                VALUES ($session, $provider, $summary, $points, $map, $updated)
                ON CONFLICT (session_id) DO UPDATE SET
                    provider = excluded.provider,
                    summary = CASE WHEN $summary = '' THEN session_notes.summary ELSE excluded.summary END,
                    key_points = CASE WHEN $points = '' THEN session_notes.key_points ELSE excluded.key_points END,
                    concept_map = CASE WHEN $map = '' THEN session_notes.concept_map ELSE excluded.concept_map END,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$session", sessionId);
            command.Parameters.AddWithValue("$provider", provider);
            command.Parameters.AddWithValue("$summary", summary ?? string.Empty);
            command.Parameters.AddWithValue("$points", keyPoints ?? string.Empty);
            command.Parameters.AddWithValue("$map", conceptMapJson ?? string.Empty);
            command.Parameters.AddWithValue("$updated", DateTime.Now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public SessionNote? GetNote(long sessionId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT provider, summary, key_points, concept_map, updated_at FROM session_notes WHERE session_id = $session;";
            command.Parameters.AddWithValue("$session", sessionId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new SessionNote(
                sessionId,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTime.Parse(reader.GetString(4)));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Close();
        _connection.Dispose();
    }
}
