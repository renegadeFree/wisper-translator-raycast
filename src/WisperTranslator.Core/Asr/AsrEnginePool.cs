namespace WisperTranslator.Core.Asr;

/// <summary>
/// Tiene caldi i modelli ASR tra una sessione e l'altra: riaprire un ggml da disco e
/// riallocare il contesto costa centinaia di millisecondi a ogni "Avvia". I motori restano
/// vivi finché il processo è vivo e vengono liberati solo all'uscita.
/// </summary>
public static class AsrEnginePool
{
    private static readonly Dictionary<string, WhisperAsrEngine> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static WhisperAsrEngine Rent(string modelPath, string? name = null)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(modelPath, out var existing))
            {
                return existing;
            }

            var created = new WhisperAsrEngine(modelPath, name);
            Cache[modelPath] = created;
            return created;
        }
    }

    /// <summary>Da chiamare solo all'uscita dell'applicazione: nessuna sessione deve essere attiva.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            foreach (var engine in Cache.Values)
            {
                engine.Dispose();
            }

            Cache.Clear();
        }
    }
}
