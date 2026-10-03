namespace WisperTranslator.Core.Asr;

/// <summary>
/// Un solo server NeMo per processo: le due corsie condividono lo stesso modello caricato.
/// Il server resta caldo tra una sessione e l'altra e viene spento all'uscita.
/// </summary>
public static class NeMoSpeechHost
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static NeMoSpeechServer? _server;

    static NeMoSpeechHost() =>
        // Il server è un processo figlio: se il processo padre termina senza spegnerlo
        // resterebbe vivo con 700 MB di modello in memoria.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

    public static bool IsRunning => _server is { StartedByUs: true };

    public static async Task<NeMoSpeechServer?> EnsureAsync(
        Action<string> report,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_server is { StartedByUs: true } running
                && await running.IsReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return running;
            }

            _server?.Dispose();
            _server = null;

            report("Preparo NeMo-Speech (runtime)...");
            var executable = await NeMoSpeechServer
                .EnsureExecutableAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!NeMoModels.IsInstalled)
            {
                report("Scarico il modello Nemotron 3.5 (circa 708 MB, una volta sola)...");
            }

            var model = await NeMoModels
                .EnsureAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            report("Carico Nemotron 3.5 in memoria...");
            var server = new NeMoSpeechServer(executable, model);
            if (!await server.EnsureStartedAsync(TimeSpan.FromSeconds(120), cancellationToken).ConfigureAwait(false))
            {
                server.Dispose();
                return null;
            }

            _server = server;
            return server;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Spegne il server (solo all'uscita dell'applicazione).</summary>
    public static void Shutdown()
    {
        Gate.Wait(TimeSpan.FromSeconds(5));
        try
        {
            _server?.Dispose();
            _server = null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
