using WisperTranslator.Core.Asr;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Translation;
using WisperTranslator.Core.Vad;

namespace WisperTranslator.Core.Session;

/// <summary>
/// Compone l'intera catena (sorgenti audio → VAD → ASR → traduzione) e pubblica le battute
/// pronte per la UI. È il punto unico da cui l'applicazione avvia e ferma tutto.
/// </summary>
public sealed class TranscriptionSession : IAsyncDisposable
{
    private readonly Dictionary<int, Cue> _cues = [];
    private readonly Dictionary<int, int> _translationVersions = [];
    private readonly Dictionary<int, CancellationTokenSource> _pendingTranslations = [];
    private readonly object _cueGate = new();
    private SessionStore? _history;
    private readonly bool _ownsHistory;
    private long _historySessionId;
    private string? _diagnosticPath;
    private readonly object _diagnosticGate = new();

    private AudioMixer? _mixer;
    private VoiceCapture? _systemCapture;
    private VoiceCapture? _microphoneCapture;
    private SpeechSegmenter? _segmenter;
    private IAsrEngine? _partialEngine;
    private IAsrEngine? _finalEngine;
    private bool _ownsPartialEngine;
    private bool _ownsFinalEngine;
    private RealtimeTranscriber? _transcriber;
    private TranslationServer? _translationServer;
    private TranslationService? _translationService;
    private CancellationTokenSource? _cancellation;
    private Task? _pipeline;
    private bool _disposed;

    /// <summary>Attesa prima di tradurre un parziale: evita di tradurre ogni singola parola.</summary>
    private static readonly TimeSpan ProgressiveTranslationDelay = TimeSpan.FromMilliseconds(300);

    public TranscriptionSession(SessionOptions? options = null, SessionStore? history = null)
    {
        Options = options ?? new SessionOptions();
        _history = history;
        _ownsHistory = history is null;
    }

    public SessionOptions Options { get; }

    /// <summary>Battuta creata o aggiornata (parziale o finale).</summary>
    public event Action<Cue>? CueUpdated;

    /// <summary>Messaggi di stato leggibili, per la barra di stato della UI.</summary>
    public event Action<string>? StatusChanged;

    public event Action<Cue>? CueCompleted;

    public bool IsRunning => _pipeline is { IsCompleted: false };

    public string? LastError { get; private set; }

    public long Transcribed { get; private set; }

    /// <summary>Storico locale: viene creato automaticamente al primo avvio della sessione.</summary>
    public SessionStore? History => _history;

    public long HistorySessionId => _historySessionId;

    public async Task StartAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        void Report(string message)
        {
            status?.Report(message);
            StatusChanged?.Invoke(message);
        }

        Report("Preparo il rilevatore di voce...");
        await ModelStore.EnsureVadModelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        Report("Preparo il riconoscitore istantaneo...");
        var partial = await AsrBackendFactory
            .CreateAsync(
                Options.LiveBackend,
                Options.PartialModel,
                Options.SourceLanguage,
                Report,
                cancellationToken)
            .ConfigureAwait(false);
        _partialEngine = partial.Engine;
        _ownsPartialEngine = partial.Owned;

        Report("Preparo il riconoscitore accurato...");
        var final = await AsrBackendFactory
            .CreateAsync(
                Options.FinalBackend,
                Options.FinalModel,
                Options.SourceLanguage,
                Report,
                cancellationToken)
            .ConfigureAwait(false);
        _finalEngine = final.Engine;
        _ownsFinalEngine = final.Owned;

        if (Options.Translate)
        {
            Report("Avvio il traduttore locale...");
            await StartTranslationAsync(Report, cancellationToken).ConfigureAwait(false);
        }

        Report("Apro le sorgenti audio...");
        BuildPipeline();

        StartHistory();
        _cancellation = new CancellationTokenSource();
        _pipeline = Task.Run(() => _transcriber!.RunAsync(_cancellation.Token), CancellationToken.None);
        Report("In ascolto");
    }

    private void StartHistory()
    {
        _history ??= new SessionStore();
        if (_history.RecoveredFromCorruption)
        {
            StatusChanged?.Invoke(
                "Lo storico era danneggiato ed è stato messo da parte: riparto con un archivio nuovo.");
        }

        _history.PurgeExpired();
        _historySessionId = _history.BeginSession(Options.SourceLanguage, Options.TargetLanguage);

        if (Options.DiagnosticLog)
        {
            _diagnosticPath = Options.DiagnosticLogPath
                              ?? Path.Combine(AppPaths.EnsureSubdirectory("logs"), $"sessione-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticPath)!);
        }
    }

    /// <summary>Una riga JSON per aggiornamento: timestamp, tipo, latenza, durata e testo.</summary>
    private void WriteDiagnostic(Cue cue, TimeSpan latency, int textLength)
    {
        if (_diagnosticPath is null)
        {
            return;
        }

        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            t = DateTime.Now.ToString("HH:mm:ss.fff"),
            utterance = cue.Id,
            final = cue.IsFinal,
            audioStart = Math.Round(cue.AudioStart.TotalSeconds, 2),
            audioDuration = Math.Round(cue.Duration.TotalSeconds, 2),
            latencyMs = (int)latency.TotalMilliseconds,
            chars = textLength,
            text = cue.Original,
        });

        lock (_diagnosticGate)
        {
            File.AppendAllText(_diagnosticPath, line + Environment.NewLine);
        }
    }

    private async Task StartTranslationAsync(Action<string> report, CancellationToken cancellationToken)
    {
        try
        {
            var executable = await TranslationServer
                .EnsureExecutableAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _translationServer = new TranslationServer(executable, port: Options.TranslationPort);
            if (!await _translationServer.EnsureStartedAsync(TimeSpan.FromSeconds(30), cancellationToken)
                    .ConfigureAwait(false))
            {
                report("Traduttore non disponibile: continuo solo con la trascrizione");
                _translationServer.Dispose();
                _translationServer = null;
                return;
            }

            _translationService = new TranslationService(
                new LocalHttpEngine(Options.TranslationPort));
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            report($"Traduttore non disponibile ({exception.Message}): continuo solo con la trascrizione");
        }
    }

    private void BuildPipeline()
    {
        _mixer = new AudioMixer();

        if (Options.SystemAudio)
        {
            _systemCapture = new VoiceCapture(SourceKind.System, Options.SystemDeviceId);
            _mixer.Add(_systemCapture);
        }

        if (Options.Microphone)
        {
            _microphoneCapture = new VoiceCapture(SourceKind.Microphone, Options.MicrophoneDeviceId);
            _mixer.Add(_microphoneCapture);
        }

        if (_mixer.Sources.Count == 0)
        {
            throw new InvalidOperationException("Nessuna sorgente audio selezionata.");
        }

        var vad = new SileroVad(AppPaths.VadModelPath);
        _segmenter = new SpeechSegmenter(vad);
        _transcriber = new RealtimeTranscriber(
            _mixer,
            _segmenter,
            _partialEngine!,
            null,
            _finalEngine!)
        {
            Language = Options.SourceLanguage,
        };

        _transcriber.Update += OnTranscriptUpdate;

        foreach (var source in _mixer.Sources.OfType<VoiceCapture>())
        {
            source.Start();
        }
    }

    private void OnTranscriptUpdate(TranscriptUpdate update)
    {
        Cue cue;
        lock (_cueGate)
        {
            var translation = _cues.TryGetValue(update.UtteranceId, out var previous) ? previous.Translation : string.Empty;
            cue = new Cue(
                update.UtteranceId,
                update.Text,
                translation,
                update.IsFinal,
                update.Start,
                update.Duration,
                previous?.CreatedAt ?? DateTime.Now,
                update.Provisional);
            _cues[update.UtteranceId] = cue;
            if (update.IsFinal)
            {
                Transcribed++;
            }
        }

        CueUpdated?.Invoke(cue);
        WriteDiagnostic(cue, update.Latency, cue.Original.Length);
        if (update.IsFinal)
        {
            CueCompleted?.Invoke(cue);
        }

        if (_history is not null && _historySessionId > 0)
        {
            try
            {
                _history.SaveCue(_historySessionId, cue);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
            }
        }

        if (Options.Translate && _translationService is not null && cue.Original.Length > 1)
        {
            ScheduleTranslation(cue, update.IsFinal);
        }
    }

    /// <summary>Accoda la traduzione annullando quella precedente della stessa battuta.</summary>
    private void ScheduleTranslation(Cue cue, bool isFinal)
    {
        CancellationTokenSource cancellation;
        lock (_cueGate)
        {
            if (_pendingTranslations.TryGetValue(cue.Id, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            cancellation = new CancellationTokenSource();
            _pendingTranslations[cue.Id] = cancellation;
        }

        _ = TranslateCueAsync(cue, isFinal, cancellation.Token);
    }

    private async Task TranslateCueAsync(Cue cue, bool isFinal, CancellationToken cancellationToken)
    {
        int version;
        lock (_cueGate)
        {
            version = _translationVersions.TryGetValue(cue.Id, out var current) ? current + 1 : 1;
            _translationVersions[cue.Id] = version;
        }

        try
        {
            if (!isFinal)
            {
                // Debounce: mentre l'utente parla il testo si assesta di continuo.
                await Task.Delay(ProgressiveTranslationDelay, cancellationToken).ConfigureAwait(false);
            }

            var result = await _translationService!
                .TranslateAsync(cue.Original, Options.SourceLanguage, Options.TargetLanguage, cancellationToken)
                .ConfigureAwait(false);

            Cue updated;
            lock (_cueGate)
            {
                if (_translationVersions.TryGetValue(cue.Id, out var latest) && latest != version)
                {
                    return;
                }

                if (!_cues.TryGetValue(cue.Id, out var existing))
                {
                    return;
                }

                updated = existing with { Translation = result.Text };
                _cues[cue.Id] = updated;
            }

            CueUpdated?.Invoke(updated);
        }
        catch (OperationCanceledException)
        {
            // superata da un aggiornamento più recente
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            StatusChanged?.Invoke($"Traduzione non riuscita: {exception.Message}");
        }
    }

    public void SetSystemAudio(bool enabled)
    {
        Options.SystemAudio = enabled;
        if (_systemCapture is not null)
        {
            _systemCapture.Enabled = enabled;
        }
    }

    /// <summary>Livelli audio correnti delle sorgenti: la UI li usa come spia di funzionamento.</summary>
    public IReadOnlyList<(string Name, float Level, bool Enabled)> Levels()
    {
        if (_mixer is null)
        {
            return [];
        }

        return
        [
            .. _mixer.Sources
                .OfType<VoiceCapture>()
                .Select(capture => (capture.Name, capture.Level, capture.Enabled)),
        ];
    }

    public void SetMicrophone(bool enabled)
    {
        Options.Microphone = enabled;
        if (_microphoneCapture is not null)
        {
            _microphoneCapture.Enabled = enabled;
        }
    }

    public void SetDirection(string sourceLanguage, string targetLanguage)
    {
        Options.SourceLanguage = sourceLanguage;
        Options.TargetLanguage = targetLanguage;
        if (_transcriber is not null)
        {
            _transcriber.Language = sourceLanguage;
        }

        if (_translationService is not null)
        {
            _translationService.ClearCache();
        }
    }

    public async Task StopAsync()
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (_pipeline is not null)
        {
            try
            {
                // Tetto di sicurezza: la decodifica nativa non deve poter bloccare la UI.
                await _pipeline.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                LastError ??= "Arresto oltre il tempo massimo: la decodifica in corso è stata abbandonata.";
            }
            catch (Exception)
            {
                // arresto richiesto
            }
        }

        _pipeline = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);

        lock (_cueGate)
        {
            foreach (var pending in _pendingTranslations.Values)
            {
                pending.Cancel();
                pending.Dispose();
            }

            _pendingTranslations.Clear();
        }

        Safe(() =>
        {
            if (_transcriber is not null)
            {
                _transcriber.Update -= OnTranscriptUpdate;
                _transcriber.Dispose();
            }
        });

        // Whisper appartiene al pool e resta caldo; Vosk/NeMo sono nostri e si liberano qui.
        Safe(() =>
        {
            if (_ownsPartialEngine)
            {
                _partialEngine?.Dispose();
            }
        });
        Safe(() =>
        {
            if (_ownsFinalEngine)
            {
                _finalEngine?.Dispose();
            }
        });

        Safe(() => _segmenter?.Dispose());
        Safe(() => _mixer?.Dispose());
        Safe(() => _translationService?.Dispose());
        Safe(() => _translationServer?.Dispose());
        Safe(() => _cancellation?.Dispose());

        if (_history is not null && _historySessionId > 0)
        {
            Safe(() => _history.EndSession(_historySessionId));
        }

        if (_ownsHistory)
        {
            Safe(() => _history?.Dispose());
        }
    }

    /// <summary>La pulizia non deve mai trasformare uno stop in un errore fatale.</summary>
    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
    }
}
