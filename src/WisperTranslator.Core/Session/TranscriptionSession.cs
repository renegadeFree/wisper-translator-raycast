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
    private readonly object _cueGate = new();
    private SessionStore? _history;
    private readonly bool _ownsHistory;
    private long _historySessionId;

    private AudioMixer? _mixer;
    private VoiceCapture? _systemCapture;
    private VoiceCapture? _microphoneCapture;
    private SpeechSegmenter? _segmenter;
    private WhisperAsrEngine? _partialEngine;
    private WhisperAsrEngine? _finalEngine;
    private RealtimeTranscriber? _transcriber;
    private TranslationServer? _translationServer;
    private TranslationService? _translationService;
    private CancellationTokenSource? _cancellation;
    private Task? _pipeline;
    private bool _disposed;

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

        Report($"Preparo il modello veloce ({Options.PartialModel.Id})...");
        var partialPath = await ModelStore
            .EnsureAsrModelAsync(Options.PartialModel, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Report($"Preparo il modello accurato ({Options.FinalModel.Id})...");
        var finalPath = await ModelStore
            .EnsureAsrModelAsync(Options.FinalModel, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (Options.Translate)
        {
            Report("Avvio il traduttore locale...");
            await StartTranslationAsync(Report, cancellationToken).ConfigureAwait(false);
        }

        Report("Apro le sorgenti audio...");
        BuildPipeline(partialPath, finalPath);

        StartHistory();
        _cancellation = new CancellationTokenSource();
        _pipeline = Task.Run(() => _transcriber!.RunAsync(_cancellation.Token), CancellationToken.None);
        Report("In ascolto");
    }

    private void StartHistory()
    {
        _history ??= new SessionStore();
        _history.PurgeExpired();
        _historySessionId = _history.BeginSession(Options.SourceLanguage, Options.TargetLanguage);
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

    private void BuildPipeline(string partialModelPath, string finalModelPath)
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
        _partialEngine = new WhisperAsrEngine(partialModelPath, Options.PartialModel.Id);
        _finalEngine = new WhisperAsrEngine(finalModelPath, Options.FinalModel.Id);
        _transcriber = new RealtimeTranscriber(
            _mixer,
            _segmenter,
            _partialEngine,
            null,
            _finalEngine)
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
                previous?.CreatedAt ?? DateTime.Now);
            _cues[update.UtteranceId] = cue;
            if (update.IsFinal)
            {
                Transcribed++;
            }
        }

        CueUpdated?.Invoke(cue);
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
            _ = TranslateCueAsync(cue, update.IsFinal);
        }
    }

    private async Task TranslateCueAsync(Cue cue, bool isFinal)
    {
        int version;
        lock (_cueGate)
        {
            version = _translationVersions.TryGetValue(cue.Id, out var current) ? current + 1 : 1;
            _translationVersions[cue.Id] = version;
        }

        try
        {
            var result = await _translationService!
                .TranslateAsync(cue.Original, Options.SourceLanguage, Options.TargetLanguage)
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
                await _pipeline.ConfigureAwait(false);
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

        if (_transcriber is not null)
        {
            _transcriber.Update -= OnTranscriptUpdate;
            _transcriber.Dispose();
        }

        _partialEngine?.Dispose();
        _finalEngine?.Dispose();
        _segmenter?.Dispose();
        _mixer?.Dispose();
        _translationService?.Dispose();
        _translationServer?.Dispose();
        _cancellation?.Dispose();

        if (_history is not null && _historySessionId > 0)
        {
            _history.EndSession(_historySessionId);
        }

        if (_ownsHistory)
        {
            _history?.Dispose();
        }
    }
}
